using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OMniX.Launcher;

/// <summary>
///     OMniX 的无窗口启动器。
///
///     用法（都是可选参数，可以任意组合）：
///       启动OMniX.exe           直接启动已编译好的程序
///       启动OMniX.exe build     先重新编译(Debug|x64)再启动
///       启动OMniX.exe fresh     清空 Debug 本地配置后启动（会重新看到首次启动页）
/// </summary>
internal static class Program
{
    /// <summary>
    ///     相对路径候选：按顺序找，找到第一个存在就用它。
    ///     全部相对 <see cref="AppContext.BaseDirectory" />，整个目录搬到别的盘也能跑。
    /// </summary>
    private static readonly string[] CandidateExePaths =
    [
        Path.Combine("src", "OMniX.WinUI", "bin", "x64", "Debug", "net9.0-windows10.0.22621.0", "OMniX.exe"),
        Path.Combine("src", "OMniX.WinUI", "bin", "Release", "Publish", "OMniX.exe"),
        Path.Combine("OMniX", "OMniX.exe")
    ];

    private static string BaseDir => AppContext.BaseDirectory;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    private static int Main(string[] args)
    {
        var doBuild = HasArg(args, "build", "rebuild");
        var doFresh = HasArg(args, "fresh");

        if (doFresh)
            CleanDebugSettings();

        if (doBuild && !Build())
            return 1;

        var exe = FindExe();

        if (exe is null)
        {
            MessageBoxW(0,
                "没有找到 OMniX.exe。\n\n先检查下面几个位置是不是空的：\n  " +
                string.Join("\n  ", CandidateExePaths.Select(p => Path.Combine(BaseDir, p))) +
                "\n\n也可以带 build 参数运行本程序，让它先编译一次。",
                "OMniX 启动器", 0x10);
            return 2;
        }

        KillRunningInstances();

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBoxW(0, "启动失败：" + ex.Message, "OMniX 启动器", 0x10);
            return 3;
        }

        return 0;
    }

    private static string? FindExe()
    {
        foreach (var relative in CandidateExePaths)
        {
            var full = Path.Combine(BaseDir, relative);
            if (File.Exists(full))
                return full;
        }

        return null;
    }

    private static void KillRunningInstances()
    {
        foreach (var process in Process.GetProcessesByName("OMniX"))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
            catch
            {
                // 退不掉就算了，OMniX 自己也有单例处理
            }
        }
    }

    private static bool Build()
    {
        var project = Path.Combine(BaseDir, "src", "OMniX.WinUI", "OMniX.WinUI.csproj");

        if (!File.Exists(project))
            return true;

        try
        {
            using var dotnet = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{project}\" -c Debug -p:Platform=x64 --nologo",
                WorkingDirectory = BaseDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            })!;

            dotnet.WaitForExit();
            return dotnet.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void CleanDebugSettings()
    {
        var omnixData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMniX");

        if (!Directory.Exists(omnixData))
            return;

        foreach (var dir in Directory.GetDirectories(omnixData, "ApplicationData_*_Debug"))
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // 删不掉不影响启动
            }
        }
    }

    private static bool HasArg(string[] args, params string[] names) =>
        args.Any(a => names.Contains(a, StringComparer.OrdinalIgnoreCase));
}
