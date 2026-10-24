$ErrorActionPreference = "Continue"

# ============================================================
# 改动前必跑：把当前界面代码整份存一份带时间戳的快照
#   D:\mod\OMniX\.backup\源码目录\<日期_时间>\
# 排除 bin / obj，免得把编译产物也塞进去
# ============================================================

$src = "D:\mod\OMniX\src\OMniX.WinUI"
$stamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
$dest = Join-Path "D:\mod\OMniX\.backup\源码目录" $stamp

New-Item -ItemType Directory -Path $dest -Force | Out-Null

robocopy $src $dest /MIR /XD bin obj /NFL /NDL /NJH /NJS | Out-Null
$rc = $LASTEXITCODE

$count = (Get-ChildItem -Path $dest -Recurse -File).Count

Write-Output ("快照: " + $dest)
Write-Output ("文件数: " + $count + "   robocopy 退出码: " + $rc)

# 只留最近 10 份，旧的自动清掉
$all = Get-ChildItem "D:\mod\OMniX\.backup\源码目录" -Directory |
    Where-Object { $_.Name -match "^\d{4}-\d{2}-\d{2}_\d{6}$" } |
    Sort-Object Name
if ($all.Count -gt 10) {
    $all | Select-Object -First ($all.Count - 10) | ForEach-Object {
        Remove-Item $_.FullName -Recurse -Force
    }
    Write-Output "已清理旧快照，保留最近 10 份"
}

# 同时刷新不带时间戳的那份，方便随时看"现在是什么样"
& cmd /c "D:\mod\OMniX\mkbackup.cmd" | Out-Null
Write-Output "UI-Backup 已同步到当前状态"
