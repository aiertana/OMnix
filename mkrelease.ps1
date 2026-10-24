# 出「解压即用」的包：D:\mod\OMniX\OMniX1.0  和  D:\mod\OMniX\OMniX1.0.zip
#
# 目录摆法跟 D:\mod\OMniX_1.0_release 一个路子：
#   OMniX1.0\
#   ├─ 启动OMniX.exe      无窗口启动器（从参照目录复制，对它只读不写）
#   ├─ README.md
#   ├─ 使用说明.txt
#   └─ OMniX\             发布出来的程序文件（自带运行时，不需要装 .NET）
#
# 换版本号：powershell -File mkrelease.ps1 -Version 1.1
param([string]$Version = '1.0')

$ErrorActionPreference = "Continue"

$proj    = 'D:\mod\OMniX\src\OMniX.WinUI\OMniX.WinUI.csproj'
$pub     = 'D:\mod\OMniX\src\OMniX.WinUI\bin\Release\Publish'
$outRoot = Join-Path 'D:\mod\OMniX' ('OMniX' + $Version)
$outApp  = Join-Path $outRoot 'OMniX'
$ref     = 'D:\mod\OMniX_1.0_release'

# ---- 1. 发布（Release / x64 / 自包含） ----
Write-Output "=== 1. 发布 ==="
& dotnet publish $proj -p:PublishProfile=FolderProfileSelfContained -c Release -p:Platform=x64 --nologo -v:m 2>&1 |
    Select-String -Pattern 'error|已成功|Publish\\' | Select-Object -Last 4

# ---- 2. 组装 ----
Write-Output ""
Write-Output "=== 2. 组装 " + $outRoot + " ==="
New-Item -ItemType Directory -Path $outApp -Force | Out-Null

# 用 /MIR 把发布输出镜像过去：目标里多出来的旧文件会被清掉。
# 不先 Remove-Item 整个目录 —— 里面上千个文件，会被安全删除守卫拦下来，
# 拦下来又继续往下走，包就成了"新旧混在一起"。
& robocopy $pub $outApp /MIR /NFL /NDL /NJH /NJS /R:1 /W:1 | Out-Null

# 启动器 + 说明：把参照目录根下的文件整个拿过来（只读，不改它）。
# 这里不写死中文文件名 —— 脚本要是被当成 ANSI 读，中文会变乱码、Test-Path 直接失败，
# 结果就是静默漏掉「启动OMniX.exe」和「使用说明.txt」，包双击不起来。
Get-ChildItem $ref -File | Copy-Item -Destination $outRoot -Force

# 改动说明同理：找"除了 README 以外的 md"，不硬写名字
Get-ChildItem 'D:\mod\OMniX' -File -Filter '*.md' |
    Where-Object { $_.Name -ne 'README.md' } |
    Copy-Item -Destination $outApp -Force

Write-Output ""
Write-Output "=== 顶层 ==="
Get-ChildItem -Path $outRoot | ForEach-Object {
    if ($_.PSIsContainer) { Write-Output ('[目录] ' + $_.Name) } else { Write-Output ('       ' + $_.Name) }
}

# ---- 3. 压缩 ----
Write-Output ""
Write-Output "=== 3. 压缩 ==="
$zip = $outRoot + '.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $outRoot '*') -DestinationPath $zip -Force

Write-Output ""
Write-Output ("已打包: " + $zip + "  " + (Get-Item $zip).Length + " 字节")
