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

    /// <summary>
    ///     统一按三位写版本号。Version 从 "1.0" 造出来时 Build 是 -1，
    ///     直接 ToString() 会是 "1.0"，跟文件名里的 "1.0.0" 对不上。
    /// </summary>
    private static string VersionText(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

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

        // 二开：附件有两种 ——
        //   增量补丁  OMniX1.0.3-patch-1.0.2.zip   只含相对当前版本变过的文件，通常几 MB
        //   整包      OMniX1.0.3.zip               一百多 MB，供新装或跨多版本时用
        // 优先挑补丁：下载快、也不容易下到一半断掉。
        // （附件名是 OMniX<版本>.zip，中间没有下划线 —— 原来是按 "OMniX_" 找的，
        //   永远匹配不上，于是每次都报「找不到 OMniX 压缩包」。）
        var assets = newestVersionFound?.assets ?? [];

        var patchName = $"OMniX{VersionText(release.Version)}-patch-{VersionText(InstalledVersion)}.zip";

        var asset = assets.FirstOrDefault(a =>
                        string.Equals(a.name, patchName, StringComparison.OrdinalIgnoreCase)) ??
                    assets.FirstOrDefault(a =>
                        a.name is not null &&
                        a.name.StartsWith("OMniX", StringComparison.OrdinalIgnoreCase) &&
                        !a.name.Contains("-patch-", StringComparison.OrdinalIgnoreCase));

        if (asset?.browser_download_url is null)
        {
            Stop(
                "Could not find OMniX archive in the newest release on GitHub. This may be due to the developer having to manually upload the zip which can take a few minutes. " +
                "If the problem persists, then you may have to update OMniX manually");
            return null;
        }

        var isPatch = string.Equals(asset.name, patchName, StringComparison.OrdinalIgnoreCase);

        Log(isPatch
            ? $"Found incremental patch {VersionText(InstalledVersion)} -> {VersionText(release.Version)}"
            : "No patch for this version, downloading the full package");

        release.DownloadUrl = new Uri(asset.browser_download_url);
        release.BrowserUrl = new Uri(newestVersionFound?.html_url ?? "https://github.com/aiertana/OMnix/releases");
        release.FileName = asset.name ?? "OMniX.zip";

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
        var systemFileFound = string.Empty;

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
                systemFileFound = fileSystemInfo.Name;
            }
        }

        // 目录里得有 OMniX.exe，而且不能长得像 Windows 系统目录 ——
        // 万一上面那句 Parent 算错了，往系统目录里盖文件就完了
        if (!containsOmniXExe)
        {
            Stop(
                $"Failed to find '{omnixExe}' in installed OMniX folder. Path: {_installedOmniXFolder}");
            return;
        }

        if (!string.IsNullOrEmpty(systemFileFound))
        {
            Stop(
                $"OMniX folder seems to contain windows system files, this should never happen. File Found: '{systemFileFound}' at " +
                $"Path: {_installedOmniXFolder}");
            return;
        }

        // 二开：改成打补丁式覆盖。
        // 原来是先删光整个 OMniX 文件夹再铺一遍新文件 —— 那样连用户自己在里面放的
        // 东西（Mods、3DMigoto 之类）一起没了，而且删到一半出意外，
        // 就留下一个半新半旧、直接跑不起来的程序。
        // 现在只把新版本带的文件盖上去，目录里原有的东西一律不动。
        var autoUpdaterFolderName = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory).Name;

        Log("Applying update...", $"Path: {_installedOmniXFolder.FullName}");

        await Task.Run(() => { ApplyPatch(_extractedOmniXFolder, _installedOmniXFolder, autoUpdaterFolderName); });

        Log("OMniX updated successfully");
    }

    /// <summary>
    ///     把新版本的文件逐个盖到安装目录上：有就覆盖，没有就新建，<b>不删任何东西</b>。
    ///     上一版留下的多余文件保持原样，用户自己放在 OMniX 目录里的文件也不会被动。
    /// </summary>
    private static void ApplyPatch(DirectoryInfo source, DirectoryInfo target, string skipFolderName)
    {
        foreach (var dir in source.EnumerateDirectories())
        {
            // 新包里也带着更新器自己，但它此刻正在运行、exe 被锁着，覆盖会抛 IOException。
            // 跳过它 —— 换更新器靠 OMniX - Auto Updater_New，主程序启动时会自动换过去。
            // （这里必须用 Equals：用 StartsWith 会把 _New 也一起跳过，更新器就永远换不了了）
            if (dir.Name.Equals(skipFolderName, StringComparison.CurrentCultureIgnoreCase))
                continue;

            ApplyPatch(dir, target.CreateSubdirectory(dir.Name), skipFolderName);
        }

        foreach (var file in source.EnumerateFiles())
        {
            var destination = Path.Combine(target.FullName, file.Name);

            try
            {
                file.CopyTo(destination, true);
            }
            catch (IOException e)
            {
                // 单个文件被占用不该让整个更新失败，记一笔接着装别的。
                // 真被卡住的通常是跟 OMniX 一起跑的子进程，重启一次就好。
                Serilog.Log.Logger.Warning(e, "Skipped file that is still in use: {File}", destination);
            }
        }
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