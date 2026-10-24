$ErrorActionPreference = "Continue"
$log = "D:\mod\OMniX\v1.log"

Get-Process -Name OMniX -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill(); $_.WaitForExit(3000) } catch { }
}
Start-Sleep -Milliseconds 800

# 清掉 XBF 缓存，省得拿到上一轮的旧资源
$obj = "D:\mod\OMniX\src\OMniX.WinUI\obj\x64\Debug\net9.0-windows10.0.22621.0"
Get-ChildItem -Path $obj -Filter "*.xbf" -Recurse -ErrorAction SilentlyContinue |
    ForEach-Object { try { $_.Delete() } catch { } }

Set-Location "D:\mod\OMniX\src\OMniX.WinUI"
& dotnet build OMniX.WinUI.csproj -c Debug -p:Platform=x64 --nologo -v:m 2>&1 |
    Out-File -FilePath $log -Encoding utf8

Select-String -Path $log -Pattern "error|个错误" | Select-Object -First 8

$bin = "D:\mod\OMniX\src\OMniX.WinUI\bin\x64\Debug\net9.0-windows10.0.22621.0"
$exe = Join-Path $bin "OMniX.exe"

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $exe
$psi.WorkingDirectory = $bin
$psi.UseShellExecute = $true
$p = [System.Diagnostics.Process]::Start($psi)
Start-Sleep -Milliseconds 8000
Write-Output ("exited=" + $p.HasExited)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$file = "D:\mod\OMniX\shot\restored.png"
$bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
Write-Output $file
