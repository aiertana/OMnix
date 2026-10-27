namespace OMniX.WinUI.Helpers;

/// <summary>
///     删东西的两种姿势。
///     玩家自己的 Mod 一律不碰；真要删，默认也是送回收站 —— 删错了还能捞回来。
/// </summary>
public static class RecycleBin
{
    /// <summary>送回收站。失败就抛，让调用方决定要不要提示。</summary>
    public static void SendDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }

    public static void SendFile(string path)
    {
        if (!File.Exists(path))
            return;

        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }

    /// <summary>玩家主动勾了「永久删除」才走这个</summary>
    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.DeletePermanently);
    }

    public static void DeleteFile(string path)
    {
        if (!File.Exists(path))
            return;

        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.DeletePermanently);
    }
}
