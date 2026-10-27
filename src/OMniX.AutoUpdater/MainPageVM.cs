using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PickLocalPackageCommand))]
    private bool _updateProcessStarted = false;
    [ObservableProperty] private string _latestVersion = "-----";
    [ObservableProperty] private Uri _defaultBrowserUri = new("https://github.com/aiertana/OMnix/releases");

    public ObservableCollection<LogEntry> ProgressLog { get; } = new();

    public UpdateProgress UpdateProgress { get; } = new();

    public Version InstalledVersion { get; }

    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private bool _finishedSuccessfully = false;

    [ObservableProperty] private bool _stopped;
    [ObservableProperty] private string? _stopReason;

    /// <summary>
    ///     二开：本地导入的更新包路径。空着 = 走 GitHub 在线更新。
    ///     原来是 readonly，只能由主程序通过命令行传进来 —— 可下载慢恰恰是最常见的情况，
    ///     那会儿用户想装的正是这个更新器本身，根本进不了主程序设置页。改成可在更新器里选。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalUpdate))]
    [NotifyPropertyChangedFor(nameof(LocalPackageName))]
    [NotifyPropertyChangedFor(nameof(LocalPackageHint))]
    private string _localZipPath = string.Empty;

    /// <summary>二开：这次是拿本地 zip 装，不用联网。</summary>
    public bool IsLocalUpdate => !string.IsNullOrWhiteSpace(_localZipPath) && File.Exists(_localZipPath);

    /// <summary>二开：界面上显示选中的包名，没选就是空</summary>
    public string LocalPackageName => IsLocalUpdate ? Path.GetFileName(_localZipPath) : string.Empty;

    /// <summary>
    ///     二开：本地模式的提示语。
    ///     原来想在 XAML 里用 &lt;Run Text="{x:Bind …}" /&gt; 拼，但 Run.Text 不支持 x:Bind，
    ///     编译能过、运行时 LoadComponent 直接抛 XamlParseException，整个更新器白屏闪退。
    ///     拼好整句再绑一个 TextBlock 就不会碰到这个限制。
    /// </summary>
    public string LocalPackageHint =>
        IsLocalUpdate ? $"Local install mode — no download: {Path.GetFileName(_localZipPath)}" : string.Empty;

    public MainPageVM(string installedOmniXVersion, string localZipPath = "")
    {
        InstalledVersion = Version.TryParse(installedOmniXVersion, out var version) ? version : new Version(0, 0, 0, 0);
        _localZipPath = localZipPath;
    }

    /// <summary>
    ///     二开：让用户自己挑一个已经下载好的更新包（zip）。
    ///     下载慢、或者根本连不上 GitHub 时，这是唯一的离线安装办法。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPickLocalPackage))]
    private async Task PickLocalPackageAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();

            // WinUI 3 的 FileOpenPicker 必须先绑窗口句柄，否则一调用就抛
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
            picker.FileTypeFilter.Add(".zip");

            var file = await picker.PickSingleFileAsync();

            if (file is null)
                return;

            LocalZipPath = file.Path;
            Log($"Selected local update package: {file.Path}");
        }
        catch (Exception ex)
        {
            Log($"Failed to pick local update package: {ex.Message}");
        }
    }

    /// <summary>二开：已经开始装了就别让人换包，不然装的是哪个都说不清</summary>
    private bool CanPickLocalPackage() => !UpdateProcessStarted;

    /// <summary>
    ///     从文件名认版本：`OMniX1.0.3.zip` 和 `OMniX1.0.3-patch-1.0.2.zip` 都认得出来。
    ///     认不出来返回 null，界面上就显示「本地包」。
    /// </summary>
    private static string? GuessVersionFromFileName(string zipPath)
    {
        var match = Regex.Match(Path.GetFileName(zipPath), @"OMniX(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
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
            ? "Could not determine the current OMniX version…"
            : $"Current OMniX version: {InstalledVersion}");


        try
        {
            if (IsLocalUpdate)
            {
                // 二开：本地导入的包 —— 已经躺在磁盘上了，
                // 不用查 GitHub、也不用下载，直接跳到解压那一步。
                _zipPath = _localZipPath;
                LatestVersion = GuessVersionFromFileName(_localZipPath) ?? "Local package";

                UpdateProgress.NextStage(); // 检查版本（跳过）
                UpdateProgress.NextStage(); // 下载（跳过）

                Log($"Using local update package: {_localZipPath}");
            }
            else
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
            Stop("Cancelled");
        }
        catch (OperationCanceledException e)
        {
            Stop("Cancelled");
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

        Log($"Latest version on the server: {newestVersionFound?.tag_name}");

        // 二开：这里原来不判空，直接 new Version(null ?? "")。
        // GitHub 的匿名 API 一小时只给 60 次，点几次「检查更新」就能撞上 403，
        // 拿回来的 release 是 null，于是 new Version("") 抛
        // "Version string portion was too short or too long" ——
        // 界面上只剩这么一句，完全看不出是限流。
        var newestText = (newestVersionFound?.tag_name ?? "").Trim().TrimStart('v');

        if (!Version.TryParse(newestText, out var newestVersion))
        {
            Log($"Could not parse a usable version number, raw value: [{newestVersionFound?.tag_name ?? "<empty>"}]");

            Stop(
                "Update check failed: could not get a version number from GitHub.\n" +
                "The most common cause is that the anonymous GitHub API rate limit (60 requests per hour) is exhausted —\n" +
                "wait an hour and try again; it could also be a network problem.\n" +
                $"Raw value received this time: {newestVersionFound?.tag_name ?? "<empty>"}");

            return null;
        }

        var release = new GitHubRelease()
        {
            Version = newestVersion,
            PreRelease = newestVersionFound?.prerelease ?? false,
            PublishedAt = newestVersionFound?.published_at ?? DateTime.MinValue
        };

        if (release.Version <= InstalledVersion)
        {
            Stop("The current version is already up to date, or newer than what's on the server");
            return null;
        }

        // 二开：附件有两种 ——
        //   增量补丁  OMniX1.0.3-patch-1.0.2.zip   只含相对当前版本变过的文件，通常几 MB
        //   整包      OMniX1.0.3.zip               一百多 MB，供新装或跨多版本时用
        // 优先挑补丁：下载快、也不容易下到一半断掉。
        // （附件名是 OMniX<版本>.zip，中间没有下划线 —— 原来是按 "OMniX_" 找的，
        //   永远匹配不上，于是每次都报「找不到 OMniX 压缩包」。）
        var assets = newestVersionFound?.assets ?? [];

        var versionText = VersionText(release.Version);
        var patchName = $"OMniX{versionText}-patch-{VersionText(InstalledVersion)}.zip";
        var fullName = $"OMniX{versionText}.zip";
        var patchPrefix = $"OMniX{versionText}-patch-";

        string? downloadUrl = null;
        string? fileName = null;

        // 二开：补丁改成按固定基准打包之后（见 PatchBaseVersion），补丁名里的
        // 那个版本号是基准，未必是自己当前的版本。所以这里不强求精确匹配：
        // 先找对着自己版本的，找不到就退一步，只要是同一个目标版本的补丁都能用。
        // 补丁里放的是「基准 → 新版」的全部变化，装在比基准新的版本上，
        // 只是多覆盖几份内容已经一样的文件，无害。
        var asset = assets.FirstOrDefault(a =>
                        string.Equals(a.name, patchName, StringComparison.OrdinalIgnoreCase)) ??
                    assets.FirstOrDefault(a =>
                        a.name is not null &&
                        a.name.StartsWith(patchPrefix, StringComparison.OrdinalIgnoreCase) &&
                        a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ??
                    assets.FirstOrDefault(a =>
                        a.name is not null &&
                        a.name.StartsWith("OMniX", StringComparison.OrdinalIgnoreCase) &&
                        !a.name.Contains("-patch-", StringComparison.OrdinalIgnoreCase));

        if (asset?.browser_download_url is not null && asset.name is not null)
        {
            downloadUrl = asset.browser_download_url;
            fileName = asset.name;
        }

        // 二开：API 限流时 assets 是空的（走的是 atom 那条路），
        // 但附件本身挂在 github.com 的下载域名上，走 CDN，不受 API 限额影响。
        // 按命名规则自己拼 URL，再用 HEAD 验一下存不存在 —— 存在就照常下载，
        // 不必干等一小时限额恢复。
        if (downloadUrl is null)
        {
            var baseUrl = $"https://github.com/aiertana/OMnix/releases/download/v{versionText}/";

            Log("Could not fetch the asset list (API rate limit), trying to build the link from the naming rules");

            // 依次试：对着自己版本的补丁 → 对着基准版本的补丁 → 整包
            var candidates = new List<string> { patchName };

            var basePatch = $"OMniX{versionText}-patch-{PatchBaseVersion}.zip";

            if (!string.Equals(basePatch, patchName, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(basePatch);
            }

            candidates.Add(fullName);

            foreach (var candidate in candidates)
            {
                if (await UrlExistsAsync(baseUrl + candidate, cancellationToken))
                {
                    downloadUrl = baseUrl + candidate;
                    fileName = candidate;
                    break;
                }
            }
        }

        if (downloadUrl is null || fileName is null)
        {
            Stop(
                "Could not find the OMniX archive in the latest Release. The author may not have finished uploading the assets yet (it takes a few minutes) — " +
                "try again later; if it keeps failing, download it manually from the Releases page and use the \"Import local update package\" option in Settings.");
            return null;
        }

        var isPatch = fileName.StartsWith(patchPrefix, StringComparison.OrdinalIgnoreCase);

        Log(isPatch
            ? $"Found an incremental patch: {VersionText(InstalledVersion)} → {versionText}"
            : "No patch applies to the current version, downloading the full package");

        release.DownloadUrl = new Uri(downloadUrl);
        release.BrowserUrl = new Uri(newestVersionFound?.html_url ?? "https://github.com/aiertana/OMnix/releases");
        release.FileName = fileName;

        LatestVersion = release.Version.ToString();

        DefaultBrowserUri = release.BrowserUrl;

        return release;
    }

    public void Stop(string stopReason)
    {
        Stopped = true;
        StopReason = stopReason;
    }

    /// <summary>
    ///     二开：这一段原来只有一次 GetAsync + CopyToAsync，中间断一下整包就作废。
    ///     作者那边 5 秒就 "Received an unexpected EOF or 0 bytes from the transport stream"，
    ///     187 MB 的整包一次都没下成，更新器卡在这步永远过不去。
    ///     现在改成断点续传 + 自动重试：断了就带 Range 头接着要剩下的部分，
    ///     只要不是彻底连不上，几 MB 几 MB 也能慢慢磨完。
    /// </summary>
    private async Task DownloadLatestVersion(GitHubRelease gitHubRelease, CancellationToken cancellationToken)
    {
        // 注意：这里不再删整个 WorkDir —— 上次失败留下的半个包要留着续传。
        Directory.CreateDirectory(WorkDir);

        _zipPath = Path.Combine(WorkDir, gitHubRelease.FileName);

        using var httpClient = CreateHttpClient();
        httpClient.DefaultRequestHeaders.Add("Accept", "application/octet-stream");

        const int maxAttempts = 8;

        Log($"Downloading: {gitHubRelease.FileName}");

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (Stopped)
            {
                return;
            }

            var already = File.Exists(_zipPath) ? new FileInfo(_zipPath).Length : 0;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, gitHubRelease.DownloadUrl);

                if (already > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(already, null);
                }

                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    // 服务器说没有这个区间了 —— 说明本地这份其实已经完整
                    Log("File already complete");
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Stop($"Download failed. Status code: {response.StatusCode}, reason: {response.ReasonPhrase}");
                    return;
                }

                // 服务器不认 Range（回 200 而不是 206）就只能从头写
                var append = response.StatusCode == HttpStatusCode.PartialContent && already > 0;

                if (!append)
                {
                    already = 0;
                }

                var total = response.Content.Headers.ContentRange?.Length
                            ?? (response.Content.Headers.ContentLength ?? 0) + already;

                var written = already;
                var lastLogged = -1;

                await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var fileStream = new FileStream(_zipPath, append ? FileMode.Append : FileMode.Create,
                                 FileAccess.Write, FileShare.None, 1024 * 128, true))
                {
                    var buffer = new byte[1024 * 128];
                    int read;

                    while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        written += read;

                        if (total > 0)
                        {
                            var percent = (int)(written * 100 / total);

                            // 每爬 10% 记一次，免得日志刷屏
                            if (percent / 10 != lastLogged / 10)
                            {
                                lastLogged = percent;
                                Log($"Downloaded {percent}% ({written / 1048576.0:F1} / {total / 1048576.0:F1} MB)");
                            }
                        }
                    }
                }

                if (total <= 0 || written >= total)
                {
                    Log($"Download complete, from {gitHubRelease.DownloadUrl}");
                    return;
                }

                Log($"Connection dropped early ({written} / {total} bytes), resuming…");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                Log($"Download interrupted (attempt {attempt}): {ex.Message}, retrying in 3 seconds…");
                await Task.Delay(3000, cancellationToken);
            }
            catch (Exception ex)
            {
                Stop($"Download failed after {maxAttempts} automatic retries: {ex.Message}\n" +
                     "Most likely the network cannot reach GitHub. Press \"Retry\" to try again (the part already downloaded is kept), " +
                     "or download it manually from the Releases page and use the \"Import local update package\" option in Settings.");
                return;
            }
        }

        Stop("The download did not complete. Press \"Retry\" to continue (the part already downloaded is kept), " +
             "or download it manually from the Releases page and use the \"Import local update package\" option in Settings.");
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
        Log("Extracting the archive…");
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            Stop($"Extraction failed. Exit code: {process.ExitCode}");
            return;
        }


        // 二开：程序本体目录从 OMniX\ 改名为 Core\（游戏数据搬到同级的 GamesData\）。
        // 两种名字都要认，不然老包认不出、新包也认不出。
        var extractedFolders = new DirectoryInfo(WorkDir).EnumerateDirectories().ToList();

        _extractedOmniXFolder =
            extractedFolders.FirstOrDefault(folder =>
                string.Equals(folder.Name, "Core", StringComparison.OrdinalIgnoreCase))
            ?? extractedFolders.FirstOrDefault(folder =>
                folder.Name.StartsWith("OMniX", StringComparison.CurrentCultureIgnoreCase))!;

        if (_extractedOmniXFolder is null)
        {
            Stop("The extracted content does not contain a Core or OMniX folder — this is not a valid update package");
            return;
        }

        Log($"Extraction complete, program folder: {_extractedOmniXFolder.FullName}");
    }

    private async Task InstallLatestVersion()
    {
        _installedOmniXFolder = new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory)).Parent!;
        if (_installedOmniXFolder is null)
        {
            Stop("Could not find the installed OMniX folder");
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
                $"{omnixExe} was not found in the OMniX folder — check whether the installation is in the wrong place. Path: {_installedOmniXFolder}");
            return;
        }

        if (!string.IsNullOrEmpty(systemFileFound))
        {
            Stop(
                $"A Windows system file appeared inside the OMniX folder — this should never happen, aborted. Found: '{systemFileFound}', " +
                $"path: {_installedOmniXFolder}");
            return;
        }

        // 二开：改成打补丁式覆盖。
        // 原来是先删光整个 OMniX 文件夹再铺一遍新文件 —— 那样连用户自己在里面放的
        // 东西（Mods、3DMigoto 之类）一起没了，而且删到一半出意外，
        // 就留下一个半新半旧、直接跑不起来的程序。
        // 现在只把新版本带的文件盖上去，目录里原有的东西一律不动。
        var autoUpdaterFolderName = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory).Name;

        Log("Applying the patch…", $"Folder: {_installedOmniXFolder.FullName}");

        await Task.Run(() => { ApplyPatch(_extractedOmniXFolder, _installedOmniXFolder, autoUpdaterFolderName); });

        Log("OMniX update complete");
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
        Log("Cleaning up temporary files…", WorkDir);

        if (Directory.Exists(WorkDir))
        {
            // 二开：以前是整个目录删掉，结果下载失败后点「重试」得从 0 开始。
            // 现在只清解压出来的东西，把下了一半的安装包留着，重试时能带着
            // Range 头接着下 —— 187 MB 在烂网络下靠的就是这一点点攒。
            foreach (var entry in Directory.EnumerateFileSystemEntries(WorkDir))
            {
                if (string.Equals(entry, _zipPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (Directory.Exists(entry))
                    {
                        Directory.Delete(entry, true);
                    }
                    else
                    {
                        File.Delete(entry);
                    }
                }
                catch (IOException)
                {
                    // 被占用就留着，下次启动再说
                }
            }
        }

        Log("Cleanup complete");
    }

    // Copied from OMniX.WinUI/Services/UpdateChecker.cs
    private const string ReleasesApiUrl = "https://api.github.com/repos/aiertana/OMnix/releases?per_page=2";

    // 二开：API 限流时的退路。这是普通网页，不受每小时 60 次的匿名限额影响。
    private const string ReleasesAtomUrl = "https://github.com/aiertana/OMnix/releases.atom";

    // 二开：增量补丁的固定基准版本，要和 mkrelease.ps1 里的 $PatchBaseVersion 保持一致。
    // 补丁里放的是「基准 → 新版本」的全部变化，所以基准以及它之后的任何版本
    // 都能直接装同一个补丁（1.1.7 → 1.1.9 一步到位，不用先装 1.1.8）。
    private const string PatchBaseVersion = "1.1.7";

    private async Task<ApiGitHubRelease?> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        Serilog.Log.Information("Checking for latest version...");

        var fromApi = await TryGetLatestFromApiAsync(cancellationToken);
        if (fromApi is not null)
        {
            return fromApi;
        }

        // 二开：GitHub 匿名 API 一小时只给 60 次，点几次「检查更新」就 403 了。
        // 以前遇到这个就彻底没辙 —— 拿不到版本号，更新直接卡死，
        // 用户看到的只是「闪退」或者一句看不懂的版本解析错误。
        // releases.atom 是普通网页，不走 API 限额，用它兜底。
        Log("GitHub API returned no version (most likely the anonymous rate limit is exhausted), falling back to releases.atom");

        return await TryGetLatestFromAtomAsync(cancellationToken);
    }

    private async Task<ApiGitHubRelease?> TryGetLatestFromApiAsync(CancellationToken cancellationToken)
    {
        using var httpClient = CreateHttpClient();

        var result = await httpClient.GetAsync(ReleasesApiUrl, cancellationToken);
        if (!result.IsSuccessStatusCode)
        {
            // 二开：把状态码和响应体记下来。403 基本就是匿名 API 限额用完（60 次/小时）。
            // 以前这里静默 return null，上层崩在 new Version("")，完全看不出真实原因。
            var body = await result.Content.ReadAsStringAsync(cancellationToken);

            Serilog.Log.Warning(
                "GitHub API failed: {Status} {Body}",
                result.StatusCode,
                body.Length > 300 ? body.Substring(0, 300) : body);

            return null;
        }

        var text = await result.Content.ReadAsStringAsync(cancellationToken);

        var gitHubReleases =
            (JsonConvert.DeserializeObject<ApiGitHubRelease[]>(text)) ?? Array.Empty<ApiGitHubRelease>();

        // 二开：原来直接 new Version(tag_name)，只要有一个 release 的 tag 不规范
        // （或者字段没解析出来），整个排序就抛异常，检查更新直接失败。
        // 改成只保留能解析出版本号的。
        return gitHubReleases
            .Where(r => !r.prerelease)
            .Select(r => new
            {
                Release = r,
                Parsed = Version.TryParse((r.tag_name ?? "").TrimStart('v'), out var v) ? v : null
            })
            .Where(x => x.Parsed is not null)
            .OrderByDescending(x => x.Parsed)
            .Select(x => x.Release)
            .FirstOrDefault();
    }

    /// <summary>
    ///     从 releases.atom 读最新版本号。这是网页不是 API，不受每小时 60 次的限额影响。
    ///     代价是拿不到附件列表，所以下面 IsNewerVersionAvailable 会按命名规则自己拼下载链接。
    /// </summary>
    private async Task<ApiGitHubRelease?> TryGetLatestFromAtomAsync(CancellationToken cancellationToken)
    {
        using var httpClient = CreateHttpClient();

        var result = await httpClient.GetAsync(ReleasesAtomUrl, cancellationToken);
        if (!result.IsSuccessStatusCode)
        {
            return null;
        }

        var text = await result.Content.ReadAsStringAsync(cancellationToken);

        // atom 里第一个 <entry> 就是最新的那个 Release，
        // 形如 <id>tag:github.com,2008:Repository/aiertana/OMnix/v1.1.8</id>
        var match = Regex.Match(text, @"<id>tag:github\.com,2008:Repository/[^<]*?/([^/<]+)</id>");

        if (!match.Success)
        {
            return null;
        }

        var tag = match.Groups[1].Value.Trim();

        if (!Version.TryParse(tag.TrimStart('v'), out _))
        {
            return null;
        }

        return new ApiGitHubRelease
        {
            tag_name = tag,
            prerelease = false,
            html_url = $"https://github.com/aiertana/OMnix/releases/tag/{tag}"
        };
    }

    /// <summary>
    ///     附件到底存不存在。只发 HEAD，不下载正文。
    /// </summary>
    private async Task<bool> UrlExistsAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = CreateHttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await httpClient.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "检查附件是否存在失败: {Url}", url);
            return false;
        }
    }

    private HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            // 二开：GitHub 的附件会 302 到 objects.githubusercontent.com，中间还可能再过一跳。
            // 原来只给 2 次，网络差一点就跳不完直接失败。
            MaxAutomaticRedirections = 10
        })
        {
            // 二开：187 MB 的整包用默认的 100 秒会中途超时，
            // 超时也会被当成 "EOF or 0 bytes" 那一类错误报出来。
            Timeout = TimeSpan.FromMinutes(15)
        };
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