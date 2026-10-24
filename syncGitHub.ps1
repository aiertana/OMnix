param(
    [Parameter(Mandatory = $true, HelpMessage = "提交说明")]
    [string]$Message,

    # patch = 小修小补（1.0.0 -> 1.0.1）
    # minor = 大更新（1.0.0 -> 1.1.0）
    # major = 换代      （1.0.0 -> 2.0.0）
    # 给了就先升版本号，并把新版本刷到 csproj / README / 使用说明各处
    [ValidateSet('', 'patch', 'minor', 'major')]
    [string]$Bump = '',

    # 顺手出一次包：本地跑 dotnet publish + 压缩，一两分钟
    [switch]$MakeRelease,

    # 带上才会把 Releases\OMniX<版本>.zip 传到 GitHub Release
    [switch]$UploadRelease
)

# git 会把 "From https://..." 这类正常信息写到 stderr，
# PowerShell 在 Stop 模式下把它当致命错误，脚本跑到 pull 那步就断。
# 成败改由每一步的 $LASTEXITCODE 判断。
$ErrorActionPreference = "Continue"

$repo        = 'D:\mod\OMniX'
$repoSlug    = 'aiertana/OMnix'
$releasesDir = Join-Path $repo 'Releases'
$verFile     = Join-Path $repo 'version.json'

Set-Location $repo

# 本机 git 没配全局 user.name 的话，commit 会报 "Please tell me who you are"，
# 而 -q 会把这句话吞掉，看着像提交成功了。沿用仓库上一次提交的作者，
# 脚本就不依赖本机 git 配置。
$lastAuthor = git log -1 --format='%an'
$lastEmail = git log -1 --format='%ae'
if ($lastAuthor) {
    $env:GIT_AUTHOR_NAME = $lastAuthor
    $env:GIT_AUTHOR_EMAIL = $lastEmail
    $env:GIT_COMMITTER_NAME = $lastAuthor
    $env:GIT_COMMITTER_EMAIL = $lastEmail
}

# ---- 0. 升版本号（可选） ----
if ($Bump) {
    Write-Output "=== 0. 升版本号 (-Bump $Bump) ==="
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'bumpVersion.ps1') -Kind $Bump
    if ($LASTEXITCODE -ne 0) {
        Write-Output "升版本号失败"
        exit 1
    }
    Write-Output ""
}

# 版本号只有一个来源：version.json。
# 包名、Release 的 tag 和标题全部跟着它走，不会再出现
# 「程序里显示 1.0.1、包名还是 OMniX1.0.zip」这种错位。
$version = (Get-Content $verFile -Raw -Encoding UTF8 | ConvertFrom-Json).version
$zipName = 'OMniX' + $version + '.zip'
$tag     = 'v' + $version
$relName = 'OMniX ' + $version

Write-Output "当前版本: $version    包: $zipName    Release: $tag"

# ---- 1. 改动记录同步到 GitHub\ ----
# GitHub\src 是软链接，不用同步，永远等于 D:\mod\OMniX\src。需要复制的只有改动记录。
Write-Output ""
Write-Output "=== 1. 改动记录同步到 GitHub\ ==="
if (Test-Path (Join-Path $repo '改动说明.md')) {
    Copy-Item (Join-Path $repo '改动说明.md') (Join-Path $repo 'GitHub\改动说明.md') -Force
    Write-Output "已复制: 改动说明.md -> GitHub\"
}

# ---- 2. 提交 ----
# 顺序不能反：git pull --rebase 遇到未提交的改动会直接拒绝，
# 报 "cannot pull with rebase: You have unstaged changes"。
Write-Output ""
Write-Output "=== 2. 提交 ==="
git add -A 2>&1 | Write-Output
$staged = (git diff --cached --name-only | Measure-Object).Count
if ($staged -eq 0) {
    Write-Output "没有要提交的改动"
}
else {
    git commit -q -m $Message
    if ($LASTEXITCODE -ne 0) {
        Write-Output "提交失败，看上面 git 的报错"
        exit 1
    }
    Write-Output ("提交 " + $staged + " 个文件: " + $Message)
}

# ---- 3. 拉取远端（变基到最新） ----
Write-Output ""
Write-Output "=== 3. 拉取远端 ==="
git pull --rebase origin main 2>&1 | Write-Output
if ($LASTEXITCODE -ne 0) {
    Write-Output "拉取失败，先把冲突处理掉"
    exit 1
}

# ---- 4. 推送 ----
Write-Output ""
Write-Output "=== 4. 推送 ==="
git push origin main 2>&1 | Write-Output
if ($LASTEXITCODE -ne 0) {
    Write-Output "推送失败"
    exit 1
}

# ---- 5. 出包（可选） ----
if ($MakeRelease) {
    Write-Output ""
    Write-Output "=== 5. 出包 ==="
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'mkrelease.ps1')
    if ($LASTEXITCODE -ne 0) {
        Write-Output "出包失败"
        exit 1
    }
}

# ---- 6. Release（可选） ----
if (-not $UploadRelease) {
    Write-Output ""
    Write-Output "=== 跳过 Release（加 -UploadRelease 才会传） ==="
    Write-Output "同步完成"
    exit 0
}

Write-Output ""
Write-Output "=== Release: $tag ==="

$zip = Join-Path $releasesDir $zipName
if (-not (Test-Path $zip)) {
    Write-Output ("找不到包: " + $zip)
    Write-Output "先跑 mkrelease.ps1，或者给这条命令加上 -MakeRelease"
    exit 1
}

$tok = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'getToken.ps1')
if (-not $tok) {
    Write-Output "拿不到 GitHub token"
    exit 1
}

$h = @{
    Authorization = 'token ' + $tok
    'User-Agent'  = 'OMniX-release'
    Accept        = 'application/vnd.github+json'
}

# 一个版本一个 Release：v1.0.0、v1.0.1 各自一份，
# 老版本的包随时能单独下回来，不用怕最新版出问题没法退。
$rel = $null
try {
    $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/$repoSlug/releases/tags/$tag" -Headers $h -TimeoutSec 60
}
catch {
    $rel = $null
}

if ($rel) {
    $releaseId = $rel.id
    Write-Output ("已有 Release: " + $tag + "  id " + $releaseId + "，附件会被替换")
}
else {
    $body = @{
        tag_name   = $tag
        name       = $relName
        body       = $Message
        draft      = $false
        prerelease = $false
    } | ConvertTo-Json

    $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/$repoSlug/releases" `
        -Method Post -Headers $h -Body $body `
        -ContentType 'application/json; charset=utf-8' -TimeoutSec 60

    $releaseId = $rel.id
    Write-Output ("新建 Release: " + $tag + "  「" + $relName + "」  id " + $releaseId)
}

$assets = Invoke-RestMethod -Uri "https://api.github.com/repos/$repoSlug/releases/$releaseId/assets" -Headers $h -TimeoutSec 60
foreach ($a in $assets) {
    Invoke-RestMethod -Uri "https://api.github.com/repos/$repoSlug/releases/assets/$($a.id)" -Method Delete -Headers $h -TimeoutSec 60 | Out-Null
    Write-Output ("删掉旧附件: " + $a.name)
}

# 两个附件：增量补丁（小，更新器优先下它）+ 整包（新装、或者跨了好几个版本时用）
$patch = Get-ChildItem $releasesDir -Filter ("OMniX" + $version + "-patch-*.zip") | Select-Object -First 1

$uploads = @($zip)
if ($patch) { $uploads += $patch.FullName }

foreach ($file in $uploads) {
    $assetName = Split-Path $file -Leaf

    $out = & curl.exe -sS --ssl-no-revoke --max-time 2400 -X POST `
        -H ("Authorization: token " + $tok) `
        -H "Content-Type: application/zip" `
        -H "User-Agent: OMniX-release" `
        --data-binary "@$file" `
        "https://uploads.github.com/repos/$repoSlug/releases/$releaseId/assets?name=$assetName"

    if ($out -match '"state"') {
        $r = $out | ConvertFrom-Json
        Write-Output ("上传完成: " + $r.name + "  " + [math]::Round($r.size / 1MB, 1) + " MB  " + $r.state)
    }
    else {
        Write-Output ("上传失败: " + $assetName)
        $out | Select-Object -Last 5 | Write-Output
        exit 1
    }
}

Write-Output ""
Write-Output ("全部同步完成  " + $relName)
