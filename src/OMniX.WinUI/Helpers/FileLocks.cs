#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OMniX.WinUI.Helpers;

/// <summary>
///     文件夹被占用时的真凶探针。用 Windows 自带的 Restart Manager API 查
///     「哪些进程正握着这个 Mod 的文件句柄」，失败提示就能点名进程，不再是裸的
///     Access denied。不用装 Sysinternals 的 handle.exe，系统自带。
/// </summary>
/// <remarks>
///     两个踩过的坑，别再踩回去：
///     1. 会话 key 不能用 GUID —— 必须以字母开头且别带连字符，否则 RmGetList 直接返回
///        ERROR_ACCESS_DENIED(5)，看起来像「没人占用」，其实是整个查询失败了。
///     2. <b>只支持文件路径，传目录会被拒</b>。所以传入文件夹时要先展开成文件。
/// </remarks>
public static class FileLocks
{
    private const int ErrorMoreData = 234;
    private const int MaxFilesToProbe = 100;

    private static readonly string SessionKey = "OMniXFileLockProbe";

    /// <summary>
    ///     把「文件夹为什么改不了名」翻译成人话，三级排查：
    ///     ① 有进程握着文件句柄（Restart Manager 查）→ ② explorer 窗口停在目录里
    ///     （目录句柄，RM 查不到）→ ③ 都查不到就给通用提示。
    ///     主页和悬浮窗的失败提示共用，保证口径一致。
    /// </summary>
    public static string DescribeFileLock(string? folderPath)
    {
        var lockers = GetLockingProcessNames(folderPath);
        if (lockers.Count > 0)
        {
            return $"「{string.Join("、", lockers)}」正占着这个 Mod 的文件夹（已重试 8 次）";
        }

        var windows = GetExplorerWindowsInside(folderPath);
        if (windows.Count > 0)
        {
            return "资源管理器窗口正开着这个 Mod 的文件夹，把它关掉再试（已重试 8 次）";
        }

        return "Mod 文件夹被某个程序占着（已重试 8 次），稍后再试";
    }

    /// <summary>
    ///     查谁占着这个 Mod 文件夹。返回进程名列表（去重），查不到返回空集合 ——
    ///     失败时还得照常给用户提示，所以这里一律吞掉异常。
    /// </summary>
    public static IReadOnlyList<string> GetLockingProcessNames(string? path)
    {
        var names = new List<string>();

        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return names;
            }

            var targets = ExpandToFiles(path).ToArray();
            if (targets.Length == 0)
            {
                return names;
            }

            if (RmStartSession(out var session, 0, SessionKey) != 0)
            {
                return names;
            }
            try
            {
                if (RmRegisterResources(session, (uint)targets.Length, targets,
                        0, null, 0, null) != 0)
                {
                    return names;
                }

                uint needed = 0, rebootReasons = 0;
                var count = 0u;

                // 第一次调用只为拿条目数，返回 ERROR_MORE_DATA 是正常流程
                if (RmGetList(session, out needed, ref count, null, ref rebootReasons) != ErrorMoreData)
                {
                    return names;
                }

                if (needed <= 0)
                {
                    return names;
                }

                var buffer = new RmProcessInfo[needed];
                count = needed;

                if (RmGetList(session, out needed, ref count, buffer, ref rebootReasons) != 0)
                {
                    return names;
                }

                for (var i = 0; i < (int)Math.Min(count, needed); i++)
                {
                    var info = buffer[i];
                    var name = ResolveName(info.strAppName, (int)info.Process.dwProcessId);
                    if (!string.IsNullOrEmpty(name) && !names.Contains(name))
                    {
                        names.Add(name);
                    }
                }
            }
            finally
            {
                RmEndSession(session);
            }
        }
        catch (Exception e)
        {
            Serilog.Log.Verbose(e, "查文件占用者失败（不影响主流程）：{Path}", path);
        }

        return names;
    }

    /// <summary>
    ///     Restart Manager 查不到「目录句柄」类的占用，而 explorer 窗口停在
    ///     Mod 文件夹里就是这种 —— 重命名必被拒，且 RM 报不出占用者。
    ///     这里用 Shell COM 枚举当前打开的资源管理器窗口，看有没有落在目标目录树内。
    /// </summary>
    public static IReadOnlyList<string> GetExplorerWindowsInside(string? folderPath)
    {
        var result = new List<string>();

        try
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return result;
            }

            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return result;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            foreach (var window in shell.Windows())
            {
                string url;
                try
                {
                    url = window.LocationURL;
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(url) || !url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string windowPath;
                try
                {
                    windowPath = Uri.UnescapeDataString(new Uri(url).LocalPath);
                }
                catch
                {
                    continue;
                }

                // 窗口停在目标目录内（或正是目标目录）→ 目标目录改名会被 explorer 卡住
                if (windowPath.StartsWith(folderPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(windowPath, folderPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(windowPath);
                }
            }
        }
        catch (Exception e)
        {
            Serilog.Log.Verbose(e, "枚举资源管理器窗口失败（不影响主流程）");
        }

        return result;
    }

    /// <summary>
    ///     关掉停在目标目录树内的资源管理器窗口，返回关掉的数量。
    ///     改名文件夹前先调它 —— explorer 握着目录句柄时改名必失败，
    ///     而这类占用 Restart Manager 查不出来，只能直接把窗口请走。
    ///     只关落在目标目录里的窗口，其它窗口一概不碰。
    /// </summary>
    public static int CloseExplorerWindowsInside(string? folderPath)
    {
        var closed = 0;

        try
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return 0;
            }

            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return 0;
            }

            var root = folderPath.TrimEnd(Path.DirectorySeparatorChar);
            dynamic shell = Activator.CreateInstance(shellType)!;

            foreach (var window in shell.Windows())
            {
                string windowPath;
                try
                {
                    var url = (string)window.LocationURL;
                    if (string.IsNullOrEmpty(url) ||
                        !url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    windowPath = Uri.UnescapeDataString(new Uri(url).LocalPath).TrimEnd(Path.DirectorySeparatorChar);
                }
                catch
                {
                    continue;
                }

                // 窗口停在目标目录内（或正是目标目录）→ 挡着改名，关掉它
                if (!windowPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(windowPath, root, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    window.Quit();
                    closed++;
                }
                catch (Exception e)
                {
                    Serilog.Log.Verbose(e, "关不掉资源管理器窗口：{Path}", windowPath);
                }
            }
        }
        catch (Exception e)
        {
            Serilog.Log.Verbose(e, "关闭资源管理器窗口时出错（不影响主流程）");
        }

        if (closed > 0)
        {
            Serilog.Log.Information("已自动关闭 {Count} 个占用 Mod 文件夹的资源管理器窗口：{Path}", closed, folderPath);
        }

        return closed;
    }

    /// <summary>
    ///     Restart Manager 只认文件，所以文件夹要先展开成文件列表。
    ///     ini 优先（3DMigoto 主要读它），总数封顶避免巨号 Mod 查询太慢。
    /// </summary>
    private static List<string> ExpandToFiles(string path)
    {
        var result = new List<string>();

        if (File.Exists(path))
        {
            result.Add(path);
            return result;
        }

        if (!Directory.Exists(path))
        {
            return result;
        }

        try
        {
            var all = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);
            result.AddRange(all.Where(f => Path.GetExtension(f)
                .Equals(".ini", StringComparison.OrdinalIgnoreCase)).Take(MaxFilesToProbe));

            if (result.Count < MaxFilesToProbe)
            {
                result.AddRange(all.Where(f => !Path.GetExtension(f)
                        .Equals(".ini", StringComparison.OrdinalIgnoreCase))
                    .Take(MaxFilesToProbe - result.Count));
            }
        }
        catch (Exception e)
        {
            Serilog.Log.Verbose(e, "展开 Mod 文件夹里的文件失败：{Path}", path);
        }

        return Path.GetFullPath(path).Length > 0
            ? result.Where(f => File.Exists(f)).Distinct().ToList()
            : result;
    }

    /// <summary>
    ///     从 Restart Manager 返回的信息里取一个能看懂的进程名（优先本机进程名）。
    /// </summary>
    private static string ResolveName(string appName, int processId)
    {
        if (processId > 0)
        {
            try
            {
                return Process.GetProcessById(processId).ProcessName;
            }
            catch (Exception e)
            {
                Serilog.Log.Verbose(e, "进程 {Pid} 查不到名字", processId);
            }
        }

        return string.IsNullOrWhiteSpace(appName) ? string.Empty : appName;
    }

    #region Restart Manager API

    // Pack=4：第二个字段是 FILETIME（两个 DWORD，对齐到偏移 4），
    // 默认的 8 字节对齐会把长整型挤到偏移 8，RmGetList 返回的结构就全错位了
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct RmUniqueProcess
    {
        public uint dwProcessId;
        public long ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags,
        string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles,
        string[] rgsFilenames, uint nApplications, RmUniqueProcess[]? rgApplications,
        uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded,
        ref uint pnProcInfo, [In, Out] RmProcessInfo[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    #endregion
}
