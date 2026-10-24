# 升版本号 —— 顺带把新版本同步到所有写着版本号的地方。
#
# 版本号原来散在六七个文件里，改一次要手改一遍，总会漏。
# 现在唯一来源是 D:\mod\OMniX\version.json，这个脚本负责读完新式之后刷回去：
#   src\OMniX.WinUI\OMniX.WinUI.csproj    程序里「关于」「设置」显示的 v1.0.1 从这里来
#   src\OMniXLauncher\OMniXLauncher.csproj 启动器自己那份独立的版本号
#   README.md / README_EN.md              首页当前版本 + 下载链接里的包名
#   使用说明.txt                          第一行标题
#   使用说明书.md                         版本那一行
#
# 口径：
#   patch  小修小补   1.0.0 -> 1.0.1
#   minor  大更新     1.0.0 -> 1.1.0
#   major  换代       1.0.0 -> 2.0.0
#
# 用法：
#   powershell -File bumpVersion.ps1 -Kind patch          # 默认 patch
#   powershell -File bumpVersion.ps1 -Kind minor
#   powershell -File bumpVersion.ps1 -Set 1.2.3           # 直接指定版本
#   powershell -File bumpVersion.ps1 -Kind none           # 不动版本，只把各处刷成最新
param(
    [ValidateSet('patch', 'minor', 'major', 'none')]
    [string]$Kind = 'patch',

    [string]$Set = ''
)

# git 会把正常信息写到 stderr，Stop 模式下 PowerShell 当它是致命错误，
# 脚本跑到一半就断。这里延续 syncGitHub.ps1 的做法，用 Continue。
$ErrorActionPreference = "Continue"

# 二开：原来写死 D:\mod\OMniX。本地跑没事，
# CI 上工作区是 D:\a\OMnix\OMnix 之类，路径全废 ——
# 第一次跑 Actions 就挂在「把版本号刷到 csproj / README」这一步。
# 改成从脚本自己的位置反推仓库根，本地和 CI 同一套。
$repo    = Split-Path -Parent $MyInvocation.MyCommand.Path
$verFile = Join-Path $repo 'version.json'

if (-not (Test-Path $verFile)) {
    Write-Output "找不到 version.json"
    exit 1
}

$json = Get-Content $verFile -Raw -Encoding UTF8 | ConvertFrom-Json
$old  = $json.version

# version.json 里存的是三位，但保不齐被人手改成两位，这里补成三位好计算
$oldParts = ($old -split '\.')
while ($oldParts.Count -lt 3) { $oldParts += '0' }
$v = [version]($oldParts -join '.')

if ($Set) {
    $new = ([version]$Set).ToString(3)
}
elseif ($Kind -eq 'none') {
    $new = $v.ToString(3)
}
else {
    $major = $v.Major
    $minor = $v.Minor
    $patch = if ($v.Build -ge 0) { $v.Build } else { 0 }

    switch ($Kind) {
        'patch' { $patch += 1 }
        'minor' { $minor += 1; $patch = 0 }
        'major' { $major += 1; $minor = 0; $patch = 0 }
    }

    $new = "$major.$minor.$patch"
}

Write-Output "版本: $old  ->  $new   ($Kind)"

if ($new -ne $old) {
    $json.version = $new
    $json.updated = (Get-Date).ToString('yyyy-MM-dd')
    ($json | ConvertTo-Json) | Set-Content $verFile -Encoding UTF8
    Write-Output "已写入 version.json"
}

# ---- 把新版本号刷到各个文件 ----
$targets = @(
    'src\OMniX.WinUI\OMniX.WinUI.csproj',
    'src\OMniXLauncher\OMniXLauncher.csproj',
    'README.md',
    'README_EN.md',
    '使用说明.txt',
    '使用说明书.md'
)

Write-Output ""
Write-Output "=== 同步到各处 ==="

foreach ($rel in $targets) {
    $f = Join-Path $repo $rel

    if (-not (Test-Path $f)) {
        Write-Output ("跳过（不存在）: " + $rel)
        continue
    }

    $text = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8)
    $before = $text

    # 包名 / 目录名：OMniX1.0.zip、OMniX1.0.1.zip 都要一次性换成新版本。
    # 用「OMniX 后面整串数字」来匹配，不然 1.0.1 之后再把 1.0 替换掉会变成 1.0.2.1。
    $text = [regex]::Replace($text, 'OMniX\d+(?:\.\d+)*', 'OMniX' + $new)

    # 剩下的裸版本号 —— 逐行来，跳过包引用。
    # 原来是无脑全局替换，结果把 <PackageReference ... Version="1.1.1" /> 也刷成了 1.1.2，
    # 编译直接报 NU1102：Polly.Contrib.WaitAndRetry 根本没有 1.1.2 这个版本。
    $text = ($text -split "`r?`n" | ForEach-Object {
            if ($_ -match 'PackageReference|PackageVersion|<TargetFramework|TargetFrameworks') { $_ }
            else { $_ -replace [regex]::Escape($old), $new }
        }) -join "`r`n"

    # 再按上下文补几处。只靠上面那句「旧版本 → 新版本」是不够的：
    # 万一某个文件之前漏同步过、停在更老的版本（csproj 就出过这事，
    # 停在 1.0.2 而 $old 已经是 1.0.3），$old 根本匹配不上，就再也刷不动了。
    # 下面这些只认标签/标题上下文，不会误伤 PackageReference 的版本号。
    $text = [regex]::Replace($text, '(?<=>)\d+\.\d+(?:\.\d+)?(?=</Version>)', $new)
    $text = [regex]::Replace($text, '(?<=>)\d+\.\d+(?:\.\d+)?(?=</VersionPrefix>)', $new)
    # 中英文 README 都照顾到：中文写「当前版本：**x**」，英文 README 写「Version: **x**」
    $text = [regex]::Replace($text, '(?i)((?:当前版本|Version)[：:]\s*\*\*)[^*]+(\*\*)', ('${1}' + $new + '${2}'))
    $text = [regex]::Replace($text, '(版本[：:]\s*)\d+\.\d+(?:\.\d+)?', ('${1}' + $new))
    $text = [regex]::Replace($text, '(OMniX\s+v)\d+\.\d+(?:\.\d+)?', ('${1}' + $new))

    if ($text -eq $before) {
        Write-Output ("没变化:     " + $rel)
        continue
    }

    # 原来有没有 BOM 就保持什么样，不然 git 上会多出一行无意义的改动
    $bytes = [System.IO.File]::ReadAllBytes($f)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $encoding = New-Object System.Text.UTF8Encoding($hasBom)

    [System.IO.File]::WriteAllText($f, $text, $encoding)
    Write-Output ("已同步:     " + $rel)
}

Write-Output ""
Write-Output "下一次出包会用新版本号： Releases\\OMniX$new.zip"
Write-Output "Release 会是： tag v$new，标题「OMniX $new」"
