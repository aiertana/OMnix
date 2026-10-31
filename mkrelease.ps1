# 出「解压即用」的包：D:\mod\OMniX\Releases\OMniX1.0  和  D:\mod\OMniX\Releases\OMniX1.0.zip
#
# 包体统一放 Releases\（作者指定的同步目录），不再丢到项目根目录：
#   Releases\
#   └─ OMniX1.0\
#      ├─ 启动OMniX.exe      无窗口启动器（从参照目录复制，对它只读不写）
#      ├─ README.md
#      ├─ 使用说明.txt
#      ├─ Core\              发布出来的程序文件（自带运行时，不需要装 .NET）
#      └─ GamesData\          游戏数据，按游戏隔离（预设 / 插件 / 缓存…）
#
# 换版本号不要改这里 —— 版本号统一存在 D:\mod\OMniX\version.json，
# 升版本用 bumpVersion.ps1 -Kind patch|minor。强制指定也可以：
#   powershell -File mkrelease.ps1 -Version 1.2.3
param([string]$Version = '')

# 二开：原来所有路径都写死 D:\mod\OMniX。本地跑没问题，
# 一旦搬到 CI（工作区是 D:\a\OMnix\OMnix 之类）就全废。
# 改成从脚本自己的位置反推仓库根 —— 本地和 CI 同一套代码。
$repo = Split-Path -Parent $MyInvocation.MyCommand.Path

# 没指定就读版本文件，它是全项目唯一的版本源
if ([string]::IsNullOrWhiteSpace($Version)) {
    $verFile = Join-Path $repo 'version.json'
    if (Test-Path $verFile) {
        $Version = (Get-Content $verFile -Raw -Encoding UTF8 | ConvertFrom-Json).version
    }
    else {
        Write-Output "找不到 version.json，退回默认版本 1.0"
        $Version = '1.0'
    }
}

$ErrorActionPreference = "Continue"

$proj    = Join-Path $repo 'src\OMniX.WinUI\OMniX.WinUI.csproj'
$pub     = Join-Path $repo 'src\OMniX.WinUI\bin\Release\Publish'
$releases= Join-Path $repo 'Releases'
$outRoot = Join-Path $releases ('OMniX' + $Version)
# 二开：程序本体目录改名为 Core\，游戏数据放同级的 GamesData\（按游戏隔离）
$outApp  = Join-Path $outRoot 'Core'
$outData = Join-Path $outRoot 'GamesData'
# 启动器和说明文档的来源。原来指向 D:\mod\OMniX_1.0_release，
# 那个参照目录早就不在了，于是包顶层一直没有「启动OMniX.exe」和说明文档 ——
# 用户解压出来只能直接点 OMniX\OMniX.exe。现在统一从项目根目录取。
$ref     = $repo

Write-Output ("=== 版本号 " + $Version + " ===")
Write-Output ""

# ---- 1. 发布（Release / x64 / 自包含） ----
Write-Output "=== 1. 发布 ==="
& dotnet publish $proj -p:PublishProfile=FolderProfileSelfContained -c Release -p:Platform=x64 --nologo -v:m 2>&1 |
    Select-String -Pattern 'error|已成功|Publish\\' | Select-Object -Last 4

# ---- 1.5 重建启动器 ----
# 二开：启动OMniX.exe 以前是手工 publish 一次就丢在根目录，之后再没人动过 ——
# 于是它一直停在 1.1.7，1.3.3 的包里装的还是那个老版本，跟主程序差了六个版本。
# 版本号对不上，真出了毛病也查不到用户手里跑的到底是哪一版。
# csproj 的版本由 bumpVersion.ps1 跟着 version.json 一起升，
# 所以这里每次打包前自动重建一次即可，不用再记得手工发布。
Write-Output ""
Write-Output "=== 1.5 重建启动器 ==="
$launcherProj = Join-Path $repo 'src\OMniXLauncher\OMniXLauncher.csproj'
$launcherOut  = Join-Path $repo 'src\OMniXLauncher\publish'
& dotnet publish $launcherProj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $launcherOut --nologo -v:m 2>&1 |
    Select-String -Pattern 'error' | Select-Object -Last 3
$launcherExe = Join-Path $launcherOut '启动OMniX.exe'
if (Test-Path $launcherExe) {
    Copy-Item $launcherExe -Destination $ref -Force
    Write-Output ("启动OMniX.exe  " + (Get-Item $launcherExe).VersionInfo.FileVersion)
}
else {
    Write-Output "警告：启动器没编译出来，包里会用根目录那份旧的"
}

# ---- 2. 组装 ----
Write-Output ""
Write-Output ("=== 2. 组装 " + $outRoot + " ===")
New-Item -ItemType Directory -Path $outApp -Force | Out-Null

# 游戏数据目录：空目录不会进 zip，放一个说明文件把它固定下来。
# 玩家一看就懂这里放什么，要整体挪盘也能在设置页改路径。
# 二开：必须整个重置 —— 上一轮在包目录里测试时，启动器把四个游戏的资源包、
# 迁移数据全装进了这里，不清理就全打进 zip（376MB 的事故）。
# 资源包本来就会在玩家第一次启动时自动装，包里不该带。
if (Test-Path $outData) { Remove-Item $outData -Recurse -Force }
New-Item -ItemType Directory -Path $outData -Force | Out-Null
Set-Content -Path (Join-Path $outData '读我.txt') -Encoding UTF8 -Value @(
    '这个文件夹放的是 OMniX 的游戏数据，按游戏分开存放：',
    '',
    '    GamesData\',
    '    ├─ WuWa\      鸣潮（预设、插件、缓存…）',
    '    ├─ ZZZ\       绝区零',
    '    ├─ GI\        原神',
    '    ├─ HSR\       崩坏：星穹铁道',
    '    ├─ Endfield\  明日方舟终末地',
    '    └─ Neverness\ 异环',
    '',
    '以前这些数据放在 C:\Users\<用户名>\AppData\Local\OMniX，',
    '新版改到这里，随版本目录一起走。启动时会自动问你要不要搬过来，',
    '也可以在「设置 → 游戏与路径 → 数据目录」里手动迁移。',
    '',
    '想放到别的盘：设置页里改数据目录路径即可，Mods 文件夹的位置也能自己定。'
)

# 用 /MIR 把发布输出镜像过去：目标里多出来的旧文件会被清掉。
# 不先 Remove-Item 整个目录 —— 里面上千个文件，会被安全删除守卫拦下来，
# 拦下来又继续往下走，包就成了"新旧混在一起"。
& robocopy $pub $outApp /MIR /NFL /NDL /NJH /NJS /R:1 /W:1 | Out-Null

# 启动器 + 说明：把参照目录根下的文件整个拿过来（只读，不改它）。
# 这里不写死中文文件名 —— 脚本要是被当成 ANSI 读，中文会变乱码、Test-Path 直接失败，
# 结果就是静默漏掉「启动OMniX.exe」和「使用说明.txt」，包双击不起来。
# 包顶层：启动器 + 文档。明确列出来，别用 Get-ChildItem 整目录复制 ——
# 根目录还有 version.json、几个 ps1 脚本，那些是开发用的，不该进包。
# 二开：外置更新入口。主程序万一打不开（崩了、缺运行时、配置坏了），
# 软件里的「检查更新」就点不到了，用户只能自己去 GitHub 翻整包 ——
# 其实更新器是独立进程，完全可以直接跑。这个东西放在包顶层，
# 双击就能更新，不经过主程序。
#
# 二开：原来是 手动更新.bat，双击会闪一个黑框，还容易被安全软件拦；
# 现在换成 手动更新.exe（见 src\UpdaterLauncher，用系统自带的 .NET Framework
# 编译器编译，几 KB，不依赖 .NET 9 运行时 —— 这个入口存在的意义就是
# 「主程序已经起不来了」，依赖越少越好）。带图标，找不到更新器时弹对话框提示。
foreach ($name in @('启动OMniX.exe', '手动更新.exe', 'README.md', '使用说明.txt', '使用说明书.md', 'DISCLAIMER.md', 'LICENSE')) {
    $src = Join-Path $ref $name
    if (Test-Path $src) {
        Copy-Item $src -Destination $outRoot -Force
    }
    else {
        Write-Output ("缺少文件: " + $name)
    }
}

# 程序目录里的说明文档：同样从根目录取（原来复制 GitHub1.0 里的副本，那个目录也没了）。
# 开发日志（改动说明.md）是内部用的，里面有代码片段和本地路径，不进发布包。
foreach ($name in @('使用说明.txt', '使用说明书.md', 'DISCLAIMER.md', 'LICENSE')) {
    $src = Join-Path $ref $name
    if (Test-Path $src) {
        Copy-Item $src -Destination $outApp -Force
    }
}

# README 里引用的界面预览图：包顶层和程序目录各放一份，
# 不然本地打开包里的 md 是个破图。
foreach ($dir in @($outRoot, $outApp)) {
    $imgDir = Join-Path $dir 'Images'
    New-Item -ItemType Directory -Force -Path $imgDir | Out-Null
    Copy-Item (Join-Path $repo 'Images\screenshot-home.png') -Destination $imgDir -Force
}

# ---- 2.5 打包自动更新器 ----
# 程序里的「更新」按钮会启动 OMniX - Auto Updater\OMniX - Auto Updater.exe，
# 由它下载新版、解压、替换文件、再拉起主程序。
# 不打进包里的话，点更新只会报「找不到更新器」—— 之前一直是这样。
Write-Output ""
Write-Output "=== 2.5 打包自动更新器 ==="
$auProj = Join-Path $repo 'src\OMniX.AutoUpdater\OMniX.AutoUpdater.csproj'
$auOut  = Join-Path $outApp 'OMniX - Auto Updater'

# 二开：这个路径必须是目录，而且要**提前建好**。
# 踩过两次，现象一模一样：
#   1) 路径变成一个无扩展名的文件（696 KB），dotnet publish 报 MSB3191
#      「无法创建目录…同名文件或目录已存在」；
#   2) 这个错误不会中断脚本 —— 后面照常补运行时、照常压缩，
#      结果包里更新器只剩 1 个文件、整包小 56 MB，日志里仅有一行
#      "补了 N 个运行时文件，更新器共 1 个文件"，稍不留神就发出去了。
#
# 最坑的是「本地好、CI 坏」：本地第二轮打包时上一轮的目录还在，publish 能用；
# CI 是全新目录，必挂。所以这里不指望 publish 自己创建目录 ——
# 先删掉可能的同名文件，再显式建目录，把两条路径的行为统一掉。
if ((Test-Path $auOut) -and -not (Get-Item $auOut -Force).PSIsContainer) {
    Write-Output "更新器目标路径是个文件而不是目录，先删掉重建"
    [System.IO.File]::Delete($auOut)
}

New-Item -ItemType Directory -Force -Path $auOut | Out-Null

# 二开：这里千万别接 Select-Object -First N。
# 它拿到 N 条输出就关闭管道，PowerShell 随之终止上游的 dotnet 进程，
# publish 被打断、目录里只剩一两个文件。这个坑在 CI（pwsh 7）上必踩，
# 本地（PS 5.1）偶尔才踩 —— 于是出现「本地能打出 229 个文件、CI 只剩 1 个」。
# 改成把输出完整写进日志文件，只在失败时把尾部打出来。
$tempDir = Join-Path $repo 'temp'
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
$publishLog = Join-Path $tempDir 'publish-updater.log'

function Invoke-UpdaterPublish {
    & dotnet publish $auProj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o $auOut --nologo -v:m *>&1 |
        Tee-Object -FilePath $publishLog | Out-Null
}

Invoke-UpdaterPublish

$auCount = (Get-ChildItem $auOut -Recurse -File -ErrorAction SilentlyContinue).Count

# 二开：打不全就再试一次（CI 上偶发还原或写入失败，重试基本能过）
if ($auCount -lt 200) {
    Write-Output ("更新器只有 " + $auCount + " 个文件，重试一次")
    Invoke-UpdaterPublish
    $auCount = (Get-ChildItem $auOut -Recurse -File -ErrorAction SilentlyContinue).Count
}

# 二开：有 exe 也不等于打全了。publish 失败时目录里可能只剩 1 个文件，
# 脚本会一路跑到底、打出一个「能启动但更新器是空壳」的包。
# 自包含发布正常在 200 个文件以上，少于此数直接中止，别发出去。
if (-not (Test-Path (Join-Path $auOut 'OMniX - Auto Updater.exe')) -or $auCount -lt 200) {
    Write-Output ("自动更新器没打全（" + $auCount + " 个文件），中止出包。publish 输出尾部：")
    Get-Content $publishLog -Tail 25 -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ("  " + $_) }
    exit 1
}

Write-Output ("自动更新器已就位（" + $auCount + " 个文件）")

# ---- 2.6 补齐更新器缺的 WinUI 运行时 ----
# 二开：dotnet publish 对 UseWinUI 的项目并不会把 WinUI 运行时复制全 ——
# 实测只有 229 个文件，Microsoft.UI.Xaml.dll（14 MB）、Microsoft.UI.Input.dll、
# Microsoft.WindowsAppRuntime.dll、Microsoft.UI.Xaml.Controls.dll 等一个都不在。
# 主程序那边是齐的（它自己要跑），所以两边文件数差了一截。
#
# 更新器是独立进程，只从自己所在目录加载。缺了 Microsoft.UI.Xaml.dll，
# 它一启动就崩在 XAML 解析上：窗口白屏 + 弹「XAML parsing failed」，
# 日志里只留下一行 "AutoUpdater OnLaunched" 之后就什么都没有 ——
# 表现跟「更新器被增量更新覆盖坏了」几乎一样，非常难分辨，之前就误判过一次。
#
# 所以这里从旁边的主程序目录把缺的补上，并记个数。
$runtimeFiles = Get-ChildItem $outApp -File | Where-Object {
    $_.Name -match 'Xaml|WinUI|Microsoft\.Windows|InputState|Microsoft\.UI\.Input|Microsoft\.UI\.Composition|Interactive|Interactivity|WindowsAppSdk'
}

$copied = 0

foreach ($f in $runtimeFiles) {
    if (-not (Test-Path (Join-Path $auOut $f.Name))) {
        Copy-Item $f.FullName -Destination $auOut -Force
        $copied++
    }
}

Write-Output ("  补了 " + $copied + " 个 WinUI 运行时文件，更新器共 " +
    (Get-ChildItem $auOut -Recurse -File).Count + " 个文件")

Write-Output ""
Write-Output "=== 顶层 ==="
Get-ChildItem -Path $outRoot | ForEach-Object {
    if ($_.PSIsContainer) { Write-Output ('[目录] ' + $_.Name) } else { Write-Output ('       ' + $_.Name) }
}

# ---- 3. 压缩 ----
Write-Output ""
Write-Output "=== 3. 压缩 ==="

# 二开：调试符号（.pdb）对用户没用，只让包变大（约 1.4 MB），打 zip 之前删掉
$pdbFiles = @(Get-ChildItem $outRoot -Recurse -Filter '*.pdb' -File -ErrorAction SilentlyContinue)
if ($pdbFiles.Count -gt 0) {
    $pdbFiles | Remove-Item -Force
    Write-Output ("清理调试符号: " + $pdbFiles.Count + " 个")
}

$zip = $outRoot + '.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $outRoot '*') -DestinationPath $zip -Force

Write-Output ""
Write-Output ("已打包: " + $zip + "  " + [math]::Round((Get-Item $zip).Length / 1MB, 1) + " MB")

# ---- 3.5 生成增量补丁 ----
# 整包一百多 MB，里面大半是自带的那份 .NET + WindowsAppSDK，每次都全量下载太慢，
# 还容易下到一半断掉（断一次就得从头再来）。
# 这里把相对上一版真正变过的文件单独压成一个小包：
#   Releases\OMniX1.0.3-patch-1.0.2.zip
# 更新器优先下载它，解压后直接盖到程序目录上 —— 只覆盖，不删任何东西。
Write-Output ""
Write-Output "=== 3.5 增量补丁 ==="

# 二开：基准原来是「比当前版本小的最大那个」，也就是紧挨着的上一版。
# 那样每个补丁只对上一版有效：1.1.7 想升到 1.1.9 必须先装 1.1.8，
# 中间漏掉一版就装不了，只能下整包（一百多 MB）。
#
# 改成钉死一个基准版：$PatchBaseVersion。补丁里放的是「从基准到当前版本」
# 的全部变化，于是基准以及它之后的任何版本都能直接装同一个补丁 ——
# 1.1.7 → 1.1.9 一步到位，不用再一版一版爬。
# 装在比基准更新的版本上也没问题：补丁里那些「基准→中间版本」的文件
# 对方已经有了，覆盖一遍内容一样，无害。
$PatchBaseVersion = '1.1.7'

$patchBaseDir = Join-Path $releases ('OMniX' + $PatchBaseVersion)

# 二开：程序本体目录从 OMniX\ 改名为 Core\（数据挪到 GamesData\）。
# 找上一版的时候两种名字都要认，不然 1.3.3 那种老包就匹配不上了。
function Get-AppDir($root) {
    $core = Join-Path $root 'Core'
    if (Test-Path $core) { return $core }

    $old = Join-Path $root 'OMniX'
    if (Test-Path $old) { return $old }

    return $null
}

if ([version]$Version -gt [version]$PatchBaseVersion -and (Get-AppDir $patchBaseDir)) {
    $prev = [pscustomobject]@{
        Dir = Get-Item $patchBaseDir
        Ver = [version]$PatchBaseVersion
    }

    Write-Output ("固定基准: " + $PatchBaseVersion + "（基准及之后的版本都能直接装这个补丁）")
}
else {
    # 基准目录不在本地（CI 上是全新环境，只有刚下的上一版），退回原来的「上一版」逻辑
    $prev = Get-ChildItem $releases -Directory |
        Where-Object { $_.Name -match '^OMniX\d+(\.\d+)*$' } |
        ForEach-Object {
            try { [pscustomobject]@{ Dir = $_; Ver = [version]$_.Name.Substring(5) } } catch { $null }
        } |
        Where-Object { $null -ne $_ -and $_.Ver -lt [version]$Version -and (Get-AppDir $_.Dir.FullName) } |
        Sort-Object -Property Ver -Descending |
        Select-Object -First 1
}

if (-not $prev) {
    Write-Output "没有上一版的解压目录，跳过（第一次出包，或者旧目录被清了）"
}
else {
    $prevApp    = Get-AppDir $prev.Dir.FullName
    $prevVer    = $prev.Ver.ToString(3)
    $patchName  = 'OMniX' + $Version + '-patch-' + $prevVer + '.zip'
    $patchZip   = Join-Path $releases $patchName
    $patchStage = Join-Path $releases ('_patch_' + $Version)

    if (Test-Path $patchStage) { Remove-Item $patchStage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Join-Path $patchStage 'Core') | Out-Null

    # MD5 比 Get-FileHash 快得多 —— 这里要算上千个文件
    $md5 = [System.Security.Cryptography.MD5]::Create()
    $changed = 0
    $total = 0

    # 更新器只要有任何一个文件变了，就把整份新更新器记到账上，
    # 最后由下面那段统一搬到 _New 里（理由见那边的注释）。
    $auChanged = $false

    foreach ($f in (Get-ChildItem $outApp -Recurse -File)) {
        $rel     = $f.FullName.Substring($outApp.Length + 1)
        $oldFile = Join-Path $prevApp $rel

        if (Test-Path $oldFile) {
            $s1 = [System.IO.File]::OpenRead($f.FullName)
            $h1 = [Convert]::ToBase64String($md5.ComputeHash($s1))
            $s1.Close()

            $s2 = [System.IO.File]::OpenRead($oldFile)
            $h2 = [Convert]::ToBase64String($md5.ComputeHash($s2))
            $s2.Close()

            if ($h1 -eq $h2) { continue }
        }

        # 更新器自己更新不了自己（正在运行），所以补丁里不能直接盖正式目录，
        # 要放到 OMniX - Auto Updater_New，交给主程序下次启动时处理。
        # 它走的是下面那条「整份复制」的路子，这里只打标记。
        if ($rel -like 'OMniX - Auto Updater\*') {
            $auChanged = $true
            continue
        }

        $dest = Join-Path (Join-Path $patchStage 'Core') $rel
        New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
        Copy-Item $f.FullName -Destination $dest -Force
        $changed++
        $total += $f.Length
    }

    # ---- 完整的更新器副本：这才是「更新以后更新器白屏闪退」的根治手段 ----
    #
    # 以前这里是把「变过的那几个文件」逐个塞进 _New —— 实测历史上只有 3 个
    # （exe / dll / pdb）。而老版本主程序的 UpdateAutoUpdater() 是这么干的：
    #     1. 删掉旧的更新器目录
    #     2. 把 _New 整个移过来
    # 它默认 _New 是一份完整的更新器。移过去之后目录里就只剩那 3 个文件，
    # coreclr.dll / hostfxr.dll / resources.pri 全丢 —— 窗口弹得出来（WinUI 空壳），
    # 但 XAML 资源和 .NET 运行时都找不到，于是白屏、然后闪退，
    # 连日志都来不及写（写日志那行在 App 构造函数里，那时已经起不来了）。
    #
    # 只要有变化就放一整套，两边都对：
    #   新版主程序 → 覆盖式 CopyFilesRecursively，没带的保持原样，没问题
    #   旧版主程序 → 整个目录移过来，拿到的是完整的一套，也没问题
    # 代价是补丁会变大（多带一整个更新器），但比让用户更新完用不了强得多。
    if ($auChanged) {
        $auSrc = Join-Path $outApp 'OMniX - Auto Updater'
        $auDst = Join-Path (Join-Path $patchStage 'Core') 'OMniX - Auto Updater_New'

        New-Item -ItemType Directory -Force -Path $auDst | Out-Null

        foreach ($f in (Get-ChildItem $auSrc -Recurse -File)) {
            $rel  = $f.FullName.Substring($auSrc.Length + 1)
            $dest = Join-Path $auDst $rel
            New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
            Copy-Item $f.FullName -Destination $dest -Force
            $changed++
            $total += $f.Length
        }

        $count = (Get-ChildItem $auSrc -Recurse -File).Count
        Write-Output ("  更新器有变化，补丁里放进了一整套完整副本（" + $count + " 个文件）")
    }

    if (Test-Path $patchZip) { Remove-Item $patchZip -Force }
    Compress-Archive -Path (Join-Path $patchStage '*') -DestinationPath $patchZip -Force

    Write-Output ("基准版本: " + $prevVer)
    Write-Output ("变化文件: " + $changed + " 个，原始共 " + [math]::Round($total / 1MB, 1) + " MB")
    Write-Output ("补丁包:   " + $patchZip)
    Write-Output ("          " + [math]::Round((Get-Item $patchZip).Length / 1MB, 1) + " MB  （整包 " + [math]::Round((Get-Item $zip).Length / 1MB, 1) + " MB）")

    Remove-Item $patchStage -Recurse -Force
}

# ---- 4. 校验：包里程序的版本号必须就是这次要发的版本 ----
# 二开：1.1.4 和 1.1.5 的包里，OMniX.exe 的文件版本是 1.1.3.0 ——
# bumpVersion.ps1 的 patch 分支在 old 匹配不到时会静默跳过 csproj，
# 编译出来的版本号没跟上，包却照发不误，用户装完看到的还是旧版本号。
# 这里兜最后一道：把 zip 里的 exe 抽出来读它的文件版本，对不上就失败退出，
# 别让 syncGitHub.ps1 把一个版本号不对的包传上去。
Write-Output ""
Write-Output "=== 4. 版本校验 ==="

Add-Type -AssemblyName System.IO.Compression.FileSystem

$tmpExe = Join-Path ([System.IO.Path]::GetTempPath()) ('_mkrelease_verify_' + [guid]::NewGuid().ToString('N') + '.exe')
$verifyError = ''

try {
    $zipEntry = $null

    $za = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try {
        # 二开：zip 条目的分隔符不统一 ——
        #   Windows PowerShell 5.1 的 Compress-Archive 写反斜杠 Core\OMniX.exe
        #   PowerShell 7（CI 上用的）写正斜杠 Core/OMniX.exe
        # 原来硬编码反斜杠，本地能过、CI 上永远匹配不到，于是报「包里找不到
        # Core\OMniX.exe」—— 但包其实是好的，白卡一轮。这里先归一化再比。
        $zipEntry = $za.Entries | Where-Object {
            ($_.FullName -replace '/', '\') -eq 'Core\OMniX.exe'
        } | Select-Object -First 1

        if ($zipEntry) {
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($zipEntry, $tmpExe, $true)
        }
        else {
            # 真找不到时把实际条目打出来，省得下次还要靠猜
            Write-Output "zip 里前 10 个条目："

            $za.Entries | Select-Object -First 10 |
                ForEach-Object { Write-Output ("  " + $_.FullName) }
        }
    }
    finally {
        $za.Dispose()
    }

    if (-not $zipEntry) {
        $verifyError = "包里找不到 Core\OMniX.exe，这个包不完整"
    }
    else {
        $actual = (Get-Item $tmpExe).VersionInfo.FileVersion
        $want   = [version]$Version
        $got    = [version]$actual

        if ($got.Major -ne $want.Major -or $got.Minor -ne $want.Minor -or $got.Build -ne $want.Build) {
            $verifyError = "包里 OMniX.exe 的版本是 $actual，但这次要发的是 $Version"
        }
        else {
            Write-Output ("包内 exe 版本 " + $actual + "  == 目标 " + $Version + "  ✓")
        }
    }
}
finally {
    if (Test-Path $tmpExe) {
        Remove-Item $tmpExe -Force -ErrorAction SilentlyContinue
    }
}

if ($verifyError) {
    Write-Output ""
    Write-Output ("校验失败： " + $verifyError)
    Write-Output ""
    Write-Output "多半是 csproj 里的 <VersionPrefix> 没跟上。跑一次："
    Write-Output "   powershell -File bumpVersion.ps1 -Kind none"
    Write-Output "把各处版本号强制刷成最新，然后重新出包。"
    Write-Output ""
    Write-Output "包还没上传，可以安全地重来。"
    exit 1
}

Write-Output ""
Write-Output "=== 5. 下一步 ==="
Write-Output ("包已经躺在 " + $zip)
Write-Output "要同步到 GitHub Release 的话，跑根目录下的 syncGitHub.ps1"
