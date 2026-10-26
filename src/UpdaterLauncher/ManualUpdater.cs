// OMniX 手动更新 —— 以前的「手动更新.bat」的可执行版本。
//
// 干什么用：主程序打不开、卡在启动界面、或者想强制检查更新时双击它。
// 它会先关掉正在运行的 OMniX，再启动包里的自动更新器（OMniX - Auto Updater），
// 更新器自己识别当前版本、下载补丁、装完把主程序拉起来。
//
// 为什么用 .NET Framework 而不是跟主项目一样用 .NET 9：
//   这个入口存在的意义就是「主程序已经起不来了」，所以依赖越少越好。
//   .NET Framework 4.x 是 Windows 自带的，不需要用户另装任何运行时；
//   .NET 9 的 launcher 得靠机器上装了对应运行时才能跑。
//
// 为什么不是脚本：双击 .bat 会闪一个黑框，而且被某些安全软件拦；
//   改成 exe 之后能带图标、安静启动，找不到更新器时用对话框提示而不是一闪而过。
//
// 构建方式见 build.ps1（用系统自带的 csc 编译，输出只有几 KB）。
// 语法保持在 C# 5，别用新版本的语法糖。

using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace OMniX.UpdaterLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // exe 就放在包的最外层，所以它的目录就是程序根目录
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string updaterPath = Path.Combine(root, "OMniX", "OMniX - Auto Updater", "OMniX - Auto Updater.exe");

            // 更新时要覆盖主程序的文件，占着就写不进去，所以先把主程序关掉
            KillRunningOMniX();

            if (!File.Exists(updaterPath))
            {
                MessageBox.Show(
                    "没有找到更新器：\r\n\r\n" + updaterPath + "\r\n\r\n" +
                    "请确认解压完整，或者重新下载一次完整包。",
                    "OMniX 手动更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = updaterPath,
                    WorkingDirectory = root,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "启动更新器失败：\r\n\r\n" + ex.Message + "\r\n\r\n" +
                    "也可以直接进入 OMniX\\OMniX - Auto Updater 目录，手动运行里面的 exe。",
                    "OMniX 手动更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void KillRunningOMniX()
        {
            try
            {
                Process[] running = Process.GetProcessesByName("OMniX");
                foreach (Process p in running)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                    catch
                    {
                        // 关不掉就算了，更新器自己还会再试一次
                    }
                }
            }
            catch
            {
                // 拿不到进程列表也不影响继续
            }
        }
    }
}
