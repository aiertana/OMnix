$ErrorActionPreference = "Continue"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class WinX {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@

$out = 'D:\mod\OMniX\shot'

function Save([int]$x, [int]$y, [int]$w, [int]$h, [string]$file)
{
    $bmp = [System.Drawing.Bitmap]::new($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([System.Drawing.Point]::new($x, $y), [System.Drawing.Point]::new(0, 0), $bmp.Size)
    $g.Dispose()
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function TopEdge([string]$file)
{
    $bmp = [System.Drawing.Bitmap]::new($file)
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        $n = 0
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            $p = $bmp.GetPixel($x, $y)
            $mx = [Math]::Max($p.R, [Math]::Max($p.G, $p.B))
            $mn = [Math]::Min($p.R, [Math]::Min($p.G, $p.B))
            if ($mx - $mn -gt 35) { $n++ }
        }
        if ($n -gt 5) { $bmp.Dispose(); return $y }
    }
    $bmp.Dispose()
    return -1
}

$proc = Get-Process -Name OMniX -ErrorAction SilentlyContinue | Select-Object -First 1
[WinX]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 2500

$win = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "启动游戏")

$btn = $null
for ($i = 0; $i -lt 6 -and -not $btn; $i++) {
    foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($t.Current.BoundingRectangle.Width -ge 60) { $btn = $t; break }
    }
    if (-not $btn) { Start-Sleep -Milliseconds 1200 }
}
if (-not $btn) { Write-Output "没找到按钮"; exit }

[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(5, 5)
Start-Sleep -Milliseconds 900
$r = $btn.Current.BoundingRectangle
Start-Sleep -Milliseconds 300
$r = $btn.Current.BoundingRectangle

$bx = [int]$r.X; $by = [int]$r.Y; $bw = [int]$r.Width; $bh = [int]$r.Height
Write-Output ("按钮 X=" + $bx + " Y=" + $by + " " + $bw + "x" + $bh)

$sx = $bx - 14; if ($sx -lt 0) { $sx = 0 }
$sy = $by - 24; if ($sy -lt 0) { $sy = 0 }
$sw = $bw + 28
$sh = $bh + 40

Save $sx $sy $sw $sh (Join-Path $out 'h_rest.png')
$t1 = TopEdge (Join-Path $out 'h_rest.png')

[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(
    ($bx + [int]($bw / 2)), ($by + [int]($bh / 2)))
Start-Sleep -Milliseconds 800
Save $sx $sy $sw $sh (Join-Path $out 'h_hover.png')
$t2 = TopEdge (Join-Path $out 'h_hover.png')

[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(5, 5)
Start-Sleep -Milliseconds 800

Write-Output ("静止上沿 y=" + $t1 + "   hover 上沿 y=" + $t2 + "   位移=" + ($t2 - $t1) + "px（期望 -2）")
