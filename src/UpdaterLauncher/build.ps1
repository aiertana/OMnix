# 用系统自带的 .NET Framework 编译器把「手动更新」编译成 exe。
#
# 产物输出到项目根目录的 手动更新.exe —— mkrelease.ps1 打包时会把它放进包的最外层。
# 需要图标时先跑 makeico.py（它会从 icon-source.png 重新生成 App.ico）。

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent (Split-Path -Parent $here)

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw '找不到系统自带的 csc.exe，这台机器上没有 .NET Framework 编译器。'
}

$icon  = Join-Path $here 'App.ico'
$src   = Join-Path $here 'ManualUpdater.cs'
$out   = Join-Path $root '手动更新.exe'

if (-not (Test-Path $icon)) { throw "缺图标: $icon（先跑 makeico.py）" }
if (-not (Test-Path $src))  { throw "缺源码: $src" }

Write-Output '=== 编译 手动更新.exe ==='
Write-Output ('  编译器: ' + $csc)

& $csc /nologo /target:winexe /optimize+ /win32icon:$icon `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.dll `
    /out:$out $src

if ($LASTEXITCODE -ne 0) {
    throw ('csc 编译失败，退出码 ' + $LASTEXITCODE)
}

if (-not (Test-Path $out)) { throw '编译没产出文件' }

$size = [math]::Round((Get-Item $out).Length / 1KB, 1)
Write-Output ('  完成: ' + $out + '  ' + $size + ' KB')
