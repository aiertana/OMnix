$ErrorActionPreference = "Continue"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class WinV {
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
    Write-Output ("已存 " + $file)
}

$proc = Get-Process -Name OMniX -ErrorAction SilentlyContinue | Select-Object -First 1
[WinV]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 1500

$win = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)

# 回首页
$homeCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "首页")
foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $homeCond)) {
    if ($t.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
        try {
            $p = $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            $p.Select()
        } catch { }
        break
    }
}
Start-Sleep -Milliseconds 1200

# 找所有按钮，取首页右上角那个（Y 大于标题栏、X 最大）
$btnCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)

$best = $null
foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)) {
    $r = $t.Current.BoundingRectangle
    if ($r.Y -lt 60) { continue }          # 跳过标题栏那三个
    if ($r.Width -gt 300) { continue }
    if (-not $best -or $r.X -gt $best.Current.BoundingRectangle.X) { $best = $t }
}

if (-not $best) { Write-Output "没找到主题按钮"; exit }

$rb = $best.Current.BoundingRectangle
Write-Output ("主题按钮: X=" + [int]$rb.X + " Y=" + [int]$rb.Y + " " + [int]$rb.Width + "x" + [int]$rb.Height)

try {
    $ip = $best.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $ip.Invoke()
} catch {
    Write-Output "点击失败"
    exit
}

Start-Sleep -Milliseconds 1500

$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Save 0 0 $b.Width $b.Height (Join-Path $out 'screen_dark.png')

# 找卡片（"Mod 管理" 标题）截下来
$cardCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "Mod 管理")
$card = $null
foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cardCond)) {
    if ($t.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) { $card = $t; break }
}

if ($card) {
    $rc = $card.Current.BoundingRectangle
    $cx = [int]$rc.X - 30; $cy = [int]$rc.Y - 30
    $cw = [int]$rc.Width + 400; $ch = [int]$rc.Height + 140
    if ($cx -lt 0) { $cx = 0 }
    if ($cy -lt 0) { $cy = 0 }
    if ($cx + $cw -gt $b.Width) { $cw = $b.Width - $cx }
    if ($cy + $ch -gt $b.Height) { $ch = $b.Height - $cy }
    Save $cx $cy $cw $ch (Join-Path $out 'card_dark.png')
} else {
    Write-Output "没找到 Mod 管理 标题"
}
