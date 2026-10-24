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

    /// <summary>二开：本地导入的更新包路径。空着 = 走 GitHub 在线更新。</summary>
    private readonly string _localZipPath;

    /// <summary>二开：这次是拿本地 zip 装，不用联网。</summary>
    public bool IsLocalUpdate => !string.IsNullOrWhiteSpace(_localZipPath) && File.Exists(_localZipPath);

    public MainPageVM(string installedOmniXVersion, string localZipPath = "")
    {
        InstalledVersion = Version.TryParse(installedOmniXVersion, out var version) ? version : new Version(0, 0, 0, 0);
        _localZipPath = localZipPath;
    }

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
            ? "无法确定当前 OMniX 版本…"
            : $"当前 OMniX 版本：{InstalledVersion}");


        try
        {
            if (IsLocalUpdate)
            {
                // 二开：本地导入的包 —— 已经躺在磁盘上了，
                // 不用查 GitHub、也不用下载，直接跳到解压那一步。
                _zipPath = _localZipPath;
                LatestVersion = GuessVersionFromFileName(_localZipPath) ?? "本地包";

                UpdateProgress.NextStage(); // 检查版本（跳过）
                UpdateProgress.NextStage(); // 下载（跳过）

                Log($"使用本地更新包：{_localZipPath}");
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
            Stop("已取消");
        }
        catch (OperationCanceledException e)
        {
            Stop("已取消");
        }
        catch (Exception e)
        {
            Log("出错了！", e.Message);
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

        Log($"服务器上的最新版本：{newestVersionFound?.tag_name}");

        // 二开：这里原来不判空，直接 new Version(null ?? "")。
        // GitHub 的匿名 API 一小时只给 60 次，点几次「检查更新」就能撞上 403，
        // 拿回来的 release 是 null，于是 new Version("") 抛
        // "Version string portion was too short or too long" ——
        // 界面上只剩这么一句，完全看不出是限流。
        var newestText = (newestVersionFound?.tag_name ?? "").Trim().TrimStart('v');

        if (!Version.TryParse(newestText, out var newestVersion))
        {
            Log($"取不到可用的版本号，原始值：[{newestVersionFound?.tag_name ?? "<空>"}]");

            Stop(
                "检查更新失败：没能从 GitHub 取到版本号。\n" +
                "最常见的原因是匿名访问 GitHub API 的限额用完了（每小时 60 次），\n" +
                "等一小时再试就行；也可能是网络不通。\n" +
                $"这次拿到的原始值：{newestVersionFound?.tag_name ?? "<空>"}");

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
            Stop("当前版本已经是最新，或者比服务器上的还新");
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

            Log("附件列表没拿到（API 限流），按命名规则拼链接试试");

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
                "在最新的 Release 里没找到 OMniX 压缩包。可能是作者还没传完附件（上传要几分钟），" +
                "过一会儿再试；一直这样也可以去 Release 页面手动下载，用设置页的「导入本地更新包」安装。");
            return null;
        }

        var isPatch = fileName.StartsWith(patchPrefix, StringComparison.OrdinalIgnoreCase);

        Log(isPatch
            ? $"找到增量补丁：{VersionText(InstalledVersion)} → {versionText}"
            : "没有适用于当前版本的补丁，改下完整包");

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

        Log($"正在下载：{gitHubRelease.FileName}");

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
                    Log("文件已完整");
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Stop($"下载失败。状态码：{response.StatusCode}，原因：{response.ReasonPhrase}");
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
                                Log($"已下载 {percent}%（{written / 1048576.0:F1} / {total / 1048576.0:F1} MB）");
                            }
                        }
                    }
                }

                if (total <= 0 || written >= total)
                {
                    Log($"下载完成，来自 {gitHubRelease.DownloadUrl}");
                    return;
                }

                Log($"连接提前断开（{written} / {total} 字节），接着下…");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                Log($"下载中断（第 {attempt} 次）：{ex.Message}，3 秒后接着下…");
                await Task.Delay(3000, cancellationToken);
            }
            catch (Exception ex)
            {
                Stop($"下载失败，自动重试 {maxAttempts} 次都没成功：{ex.Message}\n" +
                     "多半是网络到 GitHub 不通。点「重试」再试（已经下到的部分会保留），" +
                     "或者去 Release 页面手动下载，用设置页的「导入本地更新包」安装。");
                return;
            }
        }

        Stop("下载没有完成。点「重试」接着下（已下载的部分会保留），" +
             "或者去 Release 页面手动下载，用设置页的「导入本地更新包」安装。");
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
        Log("正在解压压缩包…");
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            Stop($"解压失败。退出码：{process.ExitCode}");
            return;
        }


        _extractedOmniXFolder = new DirectoryInfo(WorkDir).EnumerateDirectories().FirstOrDefault(folder =>
            folder.Name.StartsWith("OMniX", StringComparison.CurrentCultureIgnoreCase))!;

        if (_extractedOmniXFolder is null)
        {
            Stop("解压出来的东西里没有 OMniX 文件夹，这不是有效的更新包");
            return;
        }

        Log($"解压完成，程序目录：{_extractedOmniXFolder.FullName}");
    }

    private async Task InstallLatestVersion()
    {
        _installedOmniXFolder = new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory)).Parent!;
        if (_installedOmniXFolder is null)
        {
            Stop("找不到已安装的 OMniX 目录");
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
                $"在 OMniX 目录里没找到 {omnixExe}，确认一下是不是装歪了。路径：{_installedOmniXFolder}");
            return;
        }

        if (!string.IsNullOrEmpty(systemFileFound))
        {
            Stop(
                $"OMniX 目录里出现了 Windows 系统文件，这不该发生，已中止。找到的是：'{systemFileFound}'，" +
                $"路径：{_installedOmniXFolder}");
            return;
        }

        // 二开：改成打补丁式覆盖。
        // 原来是先删光整个 OMniX 文件夹再铺一遍新文件 —— 那样连用户自己在里面放的
        // 东西（Mods、3DMigoto 之类）一起没了，而且删到一半出意外，
        // 就留下一个半新半旧、直接跑不起来的程序。
        // 现在只把新版本带的文件盖上去，目录里原有的东西一律不动。
        var autoUpdaterFolderName = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory).Name;

        Log("正在打补丁…", $"目录：{_installedOmniXFolder.FullName}");

        await Task.Run(() => { ApplyPatch(_extractedOmniXFolder, _installedOmniXFolder, autoUpdaterFolderName); });

        Log("OMniX 更新完成");
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
        Log("正在清理临时文件…", WorkDir);

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

        Log("清理完成");
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
        Log("GitHub API 取不到版本（多半是匿名限额用完），改读 releases.atom");

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