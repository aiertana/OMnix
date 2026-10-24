$ErrorActionPreference = "Continue"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class WinS {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@

$out = 'D:\mod\OMniX\shot'
if (-not (Test-Path $out)) { New-Item -ItemType Directory -Path $out -Force | Out-Null }

function Save([int]$x, [int]$y, [int]$w, [int]$h, [string]$file)
{
    $bmp = [System.Drawing.Bitmap]::new($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([System.Drawing.Point]::new($x, $y), [System.Drawing.Point]::new(0, 0), $bmp.Size)
    $g.Dispose()
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ("已存: " + $file + "  " + $w + "x" + $h)
}

$proc = Get-Process -Name OMniX -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output "程序没在跑"; exit }

[WinS]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 1500

$win = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "启动游戏")

$btn = $null
foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
    if ($t.Current.BoundingRectangle.Width -ge 60) { $btn = $t; break }
}
if (-not $btn) { Write-Output "没找到启动按钮"; exit }

[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(5, 5)
Start-Sleep -Milliseconds 900
$r = $btn.Current.BoundingRectangle
Start-Sleep -Milliseconds 300
$r = $btn.Current.BoundingRectangle

$bx = [int]$r.X; $by = [int]$r.Y
$bw = [int]$r.Width; $bh = [int]$r.Height
Write-Output ("按钮: X=" + $bx + " Y=" + $by + " " + $bw + "x" + $bh)

# 静止
Save ($bx - 20) ($by - 20) ($bw + 40) ($bh + 40) (Join-Path $out 'btn_rest.png')

# hover
[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(
    ($bx + [int]($bw / 2)), ($by + [int]($bh / 2)))
Start-Sleep -Milliseconds 800
Save ($bx - 20) ($by - 20) ($bw + 40) ($bh + 40) (Join-Path $out 'btn_hover.png')

[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(5, 5)
Start-Sleep -Milliseconds 400

# 整屏，用来看整体和找卡片
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Save 0 0 $b.Width $b.Height (Join-Path $out 'screen_light.png')

# 把所有按钮名字列出来，找主题切换那个
$btnCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)

$names = @()
foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)) {
    $n = $t.Current.Name
    if ($n -and $names -notcontains $n) { $names += $n }
}
Write-Output ("按钮名字(" + $names.Count + "): " + ($names -join " | "))
