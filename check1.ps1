$ErrorActionPreference = "Continue"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class Win8 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@

$proc = Get-Process -Name OMniX -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output "程序没在跑"; exit }

[Win8]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 1500

$win = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "启动游戏")

$btn = $null
foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
    $r = $t.Current.BoundingRectangle
    if ($r.Width -ge 60 -and $r.Height -ge 20) { $btn = $t; break }
}
if (-not $btn) { Write-Output "没找到启动按钮"; exit }

[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(5, 5)
Start-Sleep -Milliseconds 900
$r0 = $btn.Current.BoundingRectangle
Start-Sleep -Milliseconds 300
$r0 = $btn.Current.BoundingRectangle

$bx = [int]$r0.X; $by = [int]$r0.Y
$bw = [int]$r0.Width; $bh = [int]$r0.Height

$pad = 14
$ax = $bx - $pad; if ($ax -lt 0) { $ax = 0 }
$ay = $by - $pad; if ($ay -lt 0) { $ay = 0 }
$aw = $bw + $pad * 2
$ah = $bh + $pad * 2

Write-Output ("按钮 X=" + $bx + " Y=" + $by + " 宽=" + $bw + " 高=" + $bh + "   截图区 " + $aw + "x" + $ah)

function Snap
{
    $bmp = [System.Drawing.Bitmap]::new($script:aw, $script:ah)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([System.Drawing.Point]::new($script:ax, $script:ay),
        [System.Drawing.Point]::new(0, 0), $bmp.Size)
    $g.Dispose()
    return $bmp
}

function SatCount($bmp, $y)
{
    $n = 0
    for ($x = 0; $x -lt $bmp.Width; $x++) {
        $p = $bmp.GetPixel($x, $y)
        $mx = [Math]::Max($p.R, [Math]::Max($p.G, $p.B))
        $mn = [Math]::Min($p.R, [Math]::Min($p.G, $p.B))
        if ($mx - $mn -gt 35) { $n++ }
    }
    return $n
}

function TopEdge($bmp)
{
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        if ((SatCount $bmp $y) -gt 5) { return $y }
    }
    return -1
}

$b1 = Snap
$top1 = TopEdge $b1
Write-Output ("静止上沿 y=" + $top1)

Write-Output "--- 按钮外面那几行（流光框应该在这） ---"
for ($dy = 4; $dy -ge 1; $dy--) {
    $y = $top1 - $dy
    if ($y -lt 0) { continue }
    $n = SatCount $b1 $y
    $p = $b1.GetPixel([int]($aw / 2), $y)
    Write-Output ("  外" + $dy + "px: 彩色点=" + $n + "  中点=" + $p.R + "," + $p.G + "," + $p.B)
}

Start-Sleep -Milliseconds 1000
$b2 = Snap
$top2 = TopEdge $b2
Write-Output ("1秒后上沿 y=" + $top2)

$diff = 0
$maxd = 0
for ($x = 0; $x -lt $b2.Width; $x++) {
    for ($y = [Math]::Max(0, $top1 - 4); $y -lt $top1; $y++) {
        $p1 = $b1.GetPixel($x, $y)
        $p2 = $b2.GetPixel($x, $y)
        $d = [Math]::Abs($p1.R - $p2.R) + [Math]::Abs($p1.G - $p2.G) + [Math]::Abs($p1.B - $p2.B)
        if ($d -gt 30) { $diff++ }
        if ($d -gt $maxd) { $maxd = $d }
    }
}
Write-Output ("外圈两帧差异点=" + $diff + " 最大差=" + $maxd + "（>0 就是外圈在动）")

[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(
    ($bx + [int]($bw / 2)), ($by + [int]($bh / 2)))
Start-Sleep -Milliseconds 800
$b3 = Snap
$top3 = TopEdge $b3
Write-Output ("hover 上沿 y=" + $top3 + "   位移=" + ($top3 - $top1) + "px（期望 -2）")

$midY = $top1 + [int]($bh / 2)
$row = @()
for ($x = 20; $x -lt $aw - 20; $x += 30) {
    $p = $b2.GetPixel($x, $midY)
    $row += ([string]$p.R + "," + $p.G + "," + $p.B)
}
Write-Output ("按钮横向颜色: " + ($row -join "  "))

$b1.Dispose(); $b2.Dispose(); $b3.Dispose()
[System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(5, 5)
