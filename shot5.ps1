$ErrorActionPreference = "Continue"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class WinW {
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

$proc = Get-Process -Name OMniX -ErrorAction SilentlyContinue | Select-Object -First 1
[WinW]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 4000

$win = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)

# 先回首页（上次可能停在设置页）
$homeCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "首页")
foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $homeCond)) {
    if ($t.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
        try { $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { }
        break
    }
}
Start-Sleep -Milliseconds 1500

$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "Mod 管理")

$card = $null
for ($i = 0; $i -lt 6 -and -not $card; $i++) {
    foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($t.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) { $card = $t; break }
    }
    if (-not $card) { Start-Sleep -Milliseconds 1200 }
}

if (-not $card) { Write-Output "没找到 Mod 管理"; exit }

$r = $card.Current.BoundingRectangle
Write-Output ("标题位置: X=" + [int]$r.X + " Y=" + [int]$r.Y)

# 鼠标移到标题上，触发卡片 hover（流光条 hover 才淡入）
[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(
    [int]($r.X + 20), [int]($r.Y + 10))
Start-Sleep -Milliseconds 900

# 卡片顶边大约在标题上方 40~50px，截一条 60px 高的带子，把顶部和流光条都框进来
$sx = [int]$r.X - 40
$sy = [int]$r.Y - 105
$sw = 460
$sh = 120
if ($sx -lt 0) { $sx = 0 }
if ($sy -lt 0) { $sy = 0 }

Save $sx $sy $sw $sh (Join-Path $out 'strip_f1.png')
Start-Sleep -Milliseconds 950
Save $sx $sy $sw $sh (Join-Path $out 'strip_f2.png')
Start-Sleep -Milliseconds 950
Save $sx $sy $sw $sh (Join-Path $out 'strip_f3.png')

Write-Output ("已存 strip_f1/f2/f3  区域 " + $sx + "," + $sy + " " + $sw + "x" + $sh)

[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(5, 5)
