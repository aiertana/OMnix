# 出「源码包」：D:\mod\OMniX\源码1.0
#
# 复制的是完整工程源码（solution + 全部子工程），不是补丁片段，
# 拿去用 Visual Studio 打开 OMniX.sln 就能编译。
# 编译产物（bin / obj / .vs）一律不带。
#
# 换版本：powershell -File mksrc.ps1 -Version 1.1
param([string]$Version = '1.0')

$ErrorActionPreference = "Continue"

$root = 'D:\mod\OMniX'
$dst  = Join-Path $root ('源码' + $Version)

New-Item -ItemType Directory -Path $dst -Force | Out-Null

# ---- 1. 源码 ----
# /PURGE 让目标跟工作区严格一致（工作区删掉的，包里也删掉）
& robocopy (Join-Path $root 'src') (Join-Path $dst 'src') /E /PURGE `
    /XD bin obj .vs .git `
    /XF *.user *.suo *.userprefs `
    /NFL /NDL /NJH /NJS /R:1 /W:1 | Out-Null

# ---- 2. 根目录的文件 ----
foreach ($f in @('LICENSE', 'README.md', '.gitignore', '改动说明.md')) {
    $p = Join-Path $root $f
    if (Test-Path $p) { Copy-Item $p $dst -Force }
}

# ---- 3. 结果 ----
Write-Output '=== 源码1.0 顶层 ==='
Get-ChildItem -Path $dst | ForEach-Object {
    if ($_.PSIsContainer) { Write-Output ('[目录] ' + $_.Name) } else { Write-Output ('       ' + $_.Name) }
}

Write-Output ''
Write-Output '=== src 下的子工程 ==='
Get-ChildItem -Path (Join-Path $dst 'src') -Directory | ForEach-Object { Write-Output ('  ' + $_.Name) }

Write-Output ''
$files = Get-ChildItem -Path $dst -Recurse -File
Write-Output ('一共 ' + $files.Count + ' 个文件, ' + [math]::Round((($files | Measure-Object Length -Sum).Sum / 1MB), 1) + ' MB')
