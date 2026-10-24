using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;

namespace OMniX.AutoUpdater;

public partial class MainPageVM : ObservableRecipient
{
    private readonly string WorkDir = Path.Combine(Path.GetTempPath(), "OMniX_Auto_Updater");
    private string _zipPath = string.Empty;
    private DirectoryInfo _extractedOmniXFolder = null!;
    private DirectoryInfo _installedOmniXFolder = null!;
    private string _newOmniXExePath = string.Empty;

    private readonly string _7zPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Assets\7z\", "7z.exe");

    [ObservableProperty] private bool _inStartupView = true;

    [ObservableProperty] private bool _updateProcessStarted = false;
    [ObservableProperty] private string _latestVersion = "-----";
    [ObservableProperty] private Uri _defaultBrowserUri = new("https://github.com/aiertana/OMnix/releases");

    public ObservableCollection<LogEntry> ProgressLog { get; } = new();

    public UpdateProgress UpdateProgress { get; } = new();

    public Version InstalledVersion { get; }

    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private bool _finishedSuccessfully = false;

    [ObservableProperty] private bool _stopped;
    [ObservableProperty] private string? _stopReason;

    public MainPageVM(string installedOmniXVersion)
    {
        InstalledVersion = Version.TryParse(installedOmniXVersion, out var version) ? version : new Version(0, 0, 0, 0);
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task StartUpdateAsync(CancellationToken cancellationToken)
    {
        UpdateProgress.Reset();
        IsLoading = true;
        InStartupView = false;
        UpdateProcessStarted = true;
        Stopped = false;
        StopReason = null;

        Log(InstalledVersion.Equals(new Version(0, 0, 0, 0))
            ? "Could not determine installed OMniX version..."
            : $"Installed OMniX version: {InstalledVersion}");


        try
        {
            var release = await IsNewerVersionAvailable(cancellationToken);
            UpdateProgress.NextStage();
            if (Stopped || release is null)
                return;

            await Task.Delay(1000, cancellationToken);
            await DownloadLatestVersion(release, cancellationToken);
            UpdateProgress.NextStage();

            if (Stopped)
            {
                CleanUp();
                return;
            }

            await Task.Delay(1000, cancellationToken);
            await UnzipLatestVersion(cancellationToken);
            UpdateProgress.NextStage();

            if (Stopped)
            {
                CleanUp();
                return;
            }

            await Task.Delay(1000, cancellationToken);
            await InstallLatestVersion();
            if (Stopped)
            {
                CleanUp();
                return;
            }

            UpdateProgress.NextStage();
        }
        catch (TaskCanceledException e)
        {
            Stop("User cancelled");
        }
        catch (OperationCanceledException e)
        {
            Stop("User cancelled");
        }
        catch (Exception e)
        {
            Log("An error occurred!", e.Message);
            Serilog.Log.Error(e, "An error occurred! Full error");
            Stop(e.Message);
        }
        finally
        {
            IsLoading = false;
        }

        if (Stopped)
        {
            CleanUp();
            return;
        }

        CleanUp();
        Finish();
    }


    private void Finish()
    {
        IsLoading = false;
        FinishedSuccessfully = true;
    }

    private async Task<GitHubRelease?> IsNewerVersionAvailable(CancellationToken cancellationToken)
    {
        var newestVersionFound = await GetLatestVersionAsync(cancellationToken);

        Log($"Newest version found: {newestVersionFound?.tag_name}");

        var release = new GitHubRelease()
        {
            Version = new Version(newestVersionFound?.tag_name?.Trim('v') ?? ""),
            PreRelease = newestVersionFound?.prerelease ?? false,
            PublishedAt = newestVersionFound?.published_at ?? DateTime.MinValue
        };

        if (release.Version <= InstalledVersion)
        {
            Stop("Installed version is newer than or equal to the newest version found on GitHub");
            return null;
        }

        // 二开：附件名是 OMniX<版本>.zip（OMniX1.0.1.zip 这种），中间没有下划线。
        // 原来写的是 "OMniX_" 前缀，永远匹配不上，于是每次都报
        // 「找不到 OMniX 压缩包」，看着像作者忘了传附件，其实是前缀写错了。
        var getOmniXAsset = newestVersionFound?.assets?.FirstOrDefault(a => a.name?.StartsWith("OMniX") ?? false);

        if (getOmniXAsset?.browser_download_url is null)
        {
            Stop(
                "Could not find OMniX archive in the newest release on GitHub. This may be due to the developer having to manually upload the zip which can take a few minutes. " +
                "If the problem persists, then you may have to update OMniX manually");
            return null;
        }

        release.DownloadUrl = new Uri(getOmniXAsset.browser_download_url);
        release.BrowserUrl = new Uri(newestVersionFound?.html_url ?? "https://github.com/aiertana/OMnix/releases");
        release.FileName = getOmniXAsset.name ?? "OMniX.zip";

        LatestVersion = release.Version.ToString();

        DefaultBrowserUri = release.BrowserUrl;

        return release;
    }

    public void Stop(string stopReason)
    {
        Stopped = true;
        StopReason = stopReason;
    }

    private async Task DownloadLatestVersion(GitHubRelease gitHubRelease, CancellationToken cancellationToken)
    {
        if (Directory.Exists(WorkDir))
        {
            Directory.Delete(WorkDir, true);
        }

        Directory.CreateDirectory(WorkDir);


        _zipPath = Path.Combine(WorkDir, gitHubRelease.FileName);
        if (File.Exists(_zipPath))
        {
            File.Delete(_zipPath);
        }

        var httpClient = CreateHttpClient();
        httpClient.DefaultRequestHeaders.Add("Accept", "application/octet-stream");

        Log("Downloading latest version...");
        var result = await httpClient.GetAsync(gitHubRelease.DownloadUrl, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!result.IsSuccessStatusCode)
        {
            Stop($"Failed to download latest version. Status Code: {result.StatusCode}, Reason: {result.ReasonPhrase}");
            return;
        }

        await using var stream = await result.Content.ReadAsStreamAsync(cancellationToken);

        await using var fileStream = File.Create(_zipPath);
        await stream.CopyToAsync(fileStream, cancellationToken);
        Log($"Latest version downloaded from {gitHubRelease.DownloadUrl}");
    }

    private async Task UnzipLatestVersion(CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo =
            {
                FileName = _7zPath,
                Arguments = $"x \"{_zipPath}\" -o\"{WorkDir}\" -y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        Log("Extracting downloaded zip file...");
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            Stop($"Failed to extract downloaded zip file. Exit Code: {process.ExitCode}");
            return;
        }


        _extractedOmniXFolder = new DirectoryInfo(WorkDir).EnumerateDirectories().FirstOrDefault(folder =>
            folder.Name.StartsWith("OMniX", StringComparison.CurrentCultureIgnoreCase))!;

        if (_extractedOmniXFolder is null)
        {
            Stop("Failed to find OMniX folder in extracted zip file");
            return;
        }

        Log($"OMniX Application folder extracted successfully. Path: {_extractedOmniXFolder.FullName}");
    }

    private async Task InstallLatestVersion()
    {
        _installedOmniXFolder = new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory)).Parent!;
        if (_installedOmniXFolder is null)
        {
            Stop("Failed to find installed OMniX folder in path");
            return;
        }

        const string omnixExe = "OMniX.exe";
        _newOmniXExePath = Path.Combine(_installedOmniXFolder.FullName, omnixExe);

        var containsOmniXExe = false;
        var containsSystemFiles = false;
        var systemFileFound = string.Empty;
        var warningFiles = new List<string>();

        foreach (var fileSystemInfo in _installedOmniXFolder.EnumerateFileSystemInfos())
        {
            if (fileSystemInfo.Name.Equals(omnixExe,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                containsOmniXExe = true;
            }

            if (SystemEntries.WindowsEntries.Any(fileEntry => fileSystemInfo.Name.Equals(fileEntry,
                    StringComparison.CurrentCultureIgnoreCase)))
            {
                containsSystemFiles = true;
                systemFileFound = fileSystemInfo.Name;
            }

            if (fileSystemInfo.Name.Equals("3DMigoto Loader.exe", StringComparison.CurrentCultureIgnoreCase))
            {
                warningFiles.Add(fileSystemInfo.Name);
            }

            if (fileSystemInfo.Name.Equals("3dmigoto", StringComparison.CurrentCultureIgnoreCase))
            {
                warningFiles.Add(fileSystemInfo.Name);
            }

            if (fileSystemInfo.Name.Equals("Mods", StringComparison.CurrentCultureIgnoreCase))
            {
                warningFiles.Add("Mods");
            }
        }

        if (!containsOmniXExe)
        {
            Stop(
                $"Failed to find '{omnixExe}' in installed OMniX folder. Path: {_installedOmniXFolder}");
            return;
        }

        if (containsSystemFiles)
        {
            Stop(
                $"OMniX folder seems to contain windows system files, this should never happen. File Found: '{systemFileFound}' at " +
                $"Path: {_installedOmniXFolder}");
            return;
        }

        var result = await ShowDeleteWarning(warningFiles);

        if (result is ContentDialogResult.Secondary or ContentDialogResult.None)
        {
            Stop("User cancelled");
            return;
        }

        var autoUpdaterFolder = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

        Log("Deleting old files...", $"Path: {_installedOmniXFolder.FullName}");

        string[] doNotDeleteFiles = ["Elevator.exe", "OMniX.exe.WebView2", "logs"];

        foreach (var fileSystemInfo in _installedOmniXFolder.EnumerateFileSystemInfos())
        {
            if (fileSystemInfo.Name.StartsWith(autoUpdaterFolder.Name,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            if (doNotDeleteFiles.Any(fileEntry => fileSystemInfo.Name.Equals(fileEntry,
                    StringComparison.CurrentCultureIgnoreCase)))
            {
                Serilog.Log.Logger.Information("Not deleting file: {FileName}", fileSystemInfo.Name);
                continue;
            }

            if (fileSystemInfo is DirectoryInfo directoryInfo)
                directoryInfo.Delete(true);
            else
                fileSystemInfo.Delete();
        }

        Log("Copying new files...", $"Path: {_installedOmniXFolder.FullName}");

        await Task.Run(() => { CopyFilesRecursively(_extractedOmniXFolder, _installedOmniXFolder); });

        Log("OMniX updated successfully");
    }

    // https://stackoverflow.com/questions/58744/copy-the-entire-contents-of-a-directory-in-c-sharp
    private static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target)
    {
        foreach (var dir in source.GetDirectories())
            CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name));
        foreach (var file in source.GetFiles())
            file.CopyTo(Path.Combine(target.FullName, file.Name));
    }

    private void CleanUp()
    {
        Log("Cleaning up work dir...", WorkDir);
        if (Directory.Exists(WorkDir))
        {
            Directory.Delete(WorkDir, true);
        }

        Log("Clean up finished");
    }

    // Copied from OMniX.WinUI/Services/UpdateChecker.cs
    private const string ReleasesApiUrl = "https://api.github.com/repos/aiertana/OMnix/releases?per_page=2";

    private async Task<ApiGitHubRelease?> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        Serilog.Log.Information("Checking for latest version...");

        using var httpClient = CreateHttpClient();

        var result = await httpClient.GetAsync(ReleasesApiUrl, cancellationToken);
        if (!result.IsSuccessStatusCode)
        {
            return null;
        }

        var text = await result.Content.ReadAsStringAsync(cancellationToken);

        var gitHubReleases =
            (JsonConvert.DeserializeObject<ApiGitHubRelease[]>(text)) ?? Array.Empty<ApiGitHubRelease>();

        var latestReleases = gitHubReleases.Where(r => !r.prerelease);
        var latestVersion = latestReleases.OrderByDescending(r => new Version(r.tag_name?.Trim('v') ?? ""));

        return latestVersion.FirstOrDefault();
    }

    private HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 2
        });
        httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        httpClient.DefaultRequestHeaders.Add("User-Agent", "OMniX-Just_Another_Skin_Manager-Update-Checker");
        return httpClient;
    }

    private class ApiGitHubRelease
    {
        public string? html_url;
        public string? target_commitish;
        public string? browser_download_url;
        public string? tag_name;
        public bool prerelease;
        public DateTime published_at = DateTime.MinValue;

        public ApiAssets[]? assets;
    }

    private class GitHubRelease
    {
        public Version Version;
        public bool PreRelease;
        public DateTime PublishedAt = DateTime.MinValue;

        public Uri BrowserUrl = null!;
        public Uri DownloadUrl = null!;
        public string FileName = null!;
    }

    private void Log(string logMessage, string? footer = null)
    {
        Serilog.Log.Information("Install Step {StepIndex} | Msg: {Message} | footer: {Footer}", (ProgressLog.Count + 1),
            logMessage, footer);

        var logEntry = new LogEntry
        {
            Message = logMessage,
            Footer = footer,
            TimeStamp = DateTime.Now
        };
        ProgressLog.Insert(0, logEntry);
    }

    [RelayCommand]
    private async Task StartOmniX()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _newOmniXExePath,
            UseShellExecute = true
        });
        await Task.Delay(500);
        Application.Current.Exit();
    }


    private async Task<ContentDialogResult> ShowDeleteWarning(ICollection<string> warningFiles)
    {
        var content = new ContentDialog
        {
            Title = "Warning",
            PrimaryButtonText = "Continue",
            DefaultButton = ContentDialogButton.Primary,
            SecondaryButtonText = "Cancel",
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var stackPanel = new StackPanel();

        stackPanel.Children.Add(new TextBlock
        {
            Text =
                "All files/folders in the installed OMniX folder will be deleted permanently!\n" +
                "This excludes the update folder itself. This action cannot be undone.\n" +
                $"OMniX Directory: {_installedOmniXFolder.FullName}",
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(0, 0, 0, 10)
        });

        if (warningFiles.Any())
            stackPanel.Children.Add(new TextBlock
            {
                Text = "These files/folders do not belong to OMniX and will be deleted as well:\n" +
                       string.Join("\n", warningFiles),
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.WrapWholeWords,
                Margin = new Thickness(0, 0, 0, 10)
            });

        stackPanel.Children.Add(new Button()
        {
            Content = "Open installed OMniX folder...",
            Margin = new Thickness(0, 8, 0, 8),
            Command = new AsyncRelayCommand(async () =>
            {
                await Launcher.LaunchFolderAsync(
                    await StorageFolder.GetFolderFromPathAsync(_installedOmniXFolder.FullName));
            })
        });

        content.Content = stackPanel;

        return await content.ShowAsync();
    }
}

internal class ApiAssets
{
    public string? name;
    public string? browser_download_url;
}

public class LogEntry
{
    public string Message { get; set; } = string.Empty;
    public string? Footer { get; set; }
    public DateTime TimeStamp { get; set; } = DateTime.Now;
}

public partial class UpdateProgress : ObservableObject
{
    [ObservableProperty] private bool _checkingForLatestUpdate = false;

    [ObservableProperty] private bool _downloadingLatestUpdate = false;

    [ObservableProperty] private bool _extractingLatestUpdate = false;

    [ObservableProperty] private bool _installingLatestUpdate = false;

    public void Reset()
    {
        CheckingForLatestUpdate = false;
        DownloadingLatestUpdate = false;
        ExtractingLatestUpdate = false;
        InstallingLatestUpdate = false;
    }

    public void NextStage()
    {
        if (!CheckingForLatestUpdate)
        {
            CheckingForLatestUpdate = true;
        }
        else if (!DownloadingLatestUpdate)
        {
            DownloadingLatestUpdate = true;
        }
        else if (!ExtractingLatestUpdate)
        {
            ExtractingLatestUpdate = true;
        }
        else if (!InstallingLatestUpdate)
        {
            InstallingLatestUpdate = true;
        }
    }
}