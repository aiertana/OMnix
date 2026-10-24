$ErrorActionPreference = "Continue"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class WinU {
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
[WinU]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 2000

$win = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)

# 点"设置"
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "设置")

$nav = $null
foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
    if ($t.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) { $nav = $t; break }
}

if (-not $nav) { Write-Output "没找到设置项"; exit }

$ok = $false
try {
    $p = $nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $p.Select()
    $ok = $true
} catch {
    try {
        $p = $nav.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $p.Invoke()
        $ok = $true
    } catch { }
}

Write-Output ("点击设置: " + $ok)
Start-Sleep -Milliseconds 1500

$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Save 0 0 $b.Width $b.Height (Join-Path $out 'screen_settings.png')
Write-Output "已存 screen_settings.png"

# 列出设置页里的可交互元素，找主题/深色
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)

$list = @()
foreach ($t in $all) {
    $n = $t.Current.Name
    $ct = $t.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
    if ($n -and $ct -in @('Button', 'ComboBox', 'RadioButton', 'ToggleSwitch', 'CheckBox', 'ListItem', 'Text')) {
        $list += ($ct + "=" + $n)
    }
}
Write-Output ("元素(" + $list.Count + "): " + (($list | Select-Object -First 60) -join " | "))
