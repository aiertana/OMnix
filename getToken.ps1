# 从 Windows 凭据管理器里读 GitHub token。
# 不把 token 写死在任何脚本里 —— 同步脚本调用它拿临时凭证。
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class CredReader {
  [DllImport("advapi32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
  public static extern bool CredRead(string target, int type, int flags, out IntPtr p);
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  public struct CRED {
    public int Flags; public int Type; public string TargetName; public string Comment;
    public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
    public int BlobSize; public IntPtr Blob; public int Persist;
    public int AttrCount; public IntPtr Attrs; public string Alias; public string UserName;
  }
  public static string Get(string t) {
    IntPtr p;
    if (!CredRead(t, 1, 0, out p)) return null;
    var c = (CRED)Marshal.PtrToStructure(p, typeof(CRED));
    return Marshal.PtrToStringUni(c.Blob, c.BlobSize / 2);
  }
}
"@

[CredReader]::Get('git:https://github.com')
