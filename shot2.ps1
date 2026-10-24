$ErrorActionPreference = "Continue"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class WinT {
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
}

$proc = Get-Process -Name OMniX -ErrorAction SilentlyContinue | Select-Object -First 1
[WinT]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 4000

$win = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "启动游戏")

$btn = $null
for ($try = 0; $try -lt 6 -and -not $btn; $try++) {
    foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($t.Current.BoundingRectangle.Width -ge 60) { $btn = $t; break }
    }
    if (-not $btn) { Start-Sleep -Milliseconds 1500 }
}

if (-not $btn) { Write-Output "WAIT: 按钮还没出来"; exit }

[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(5, 5)
Start-Sleep -Milliseconds 800
$r = $btn.Current.BoundingRectangle
$bx = [int]$r.X; $by = [int]$r.Y; $bw = [int]$r.Width; $bh = [int]$r.Height

# 两帧，间隔 1 秒：3 秒转一圈，1 秒正好走 120 度
Save ($bx - 16) ($by - 16) ($bw + 32) ($bh + 32) (Join-Path $out 'flow_f1.png')
Start-Sleep -Milliseconds 1000
Save ($bx - 16) ($by - 16) ($bw + 32) ($bh + 32) (Join-Path $out 'flow_f2.png')
Start-Sleep -Milliseconds 1000
Save ($bx - 16) ($by - 16) ($bw + 32) ($bh + 32) (Join-Path $out 'flow_f3.png')

Write-Output "已存 flow_f1 / f2 / f3"

# 找设置入口
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)

$found = @()
foreach ($t in $all) {
    $n = $t.Current.Name
    if ($n -and ($n -like "*设置*" -or $n -like "*主题*" -or $n -like "*深色*")) {
        $found += ($t.Current.ControlType.ProgrammaticName + " : " + $n)
    }
}
Write-Output ("含设置/主题/深色的元素: " + ($found -join " | "))
