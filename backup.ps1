$ErrorActionPreference = "Continue"

# ============================================================
# 改动前必跑：把当前界面代码整份存一份带时间戳的快照
#   D:\mod\OMniX\snapshots\<日期_时间>\
# 排除 bin / obj，避免把编译产物也塞进去
# ============================================================

$src = 'D:\mod\OMniX\src\OMniX.WinUI'
$stamp = Get-Date -Format 'yyyy-MM-dd_HHmmss'
$dest = Join-Path 'D:\mod\OMniX\snapshots' $stamp

New-Item -ItemType Directory -Path $dest -Force | Out-Null

robocopy $src $dest /MIR /XD bin obj /NFL /NDL /NJH /NJS | Out-Null
$rc = $LASTEXITCODE

$count = (Get-ChildItem -Path $dest -Recurse -File).Count

Write-Output ("快照: " + $dest)
Write-Output ("文件数: " + $count + "   robocopy 退出码: " + $rc)

# 同时刷新不带时间戳的那份，方便随时看"现在是什么样"
& cmd /c 'D:\mod\OMniX\mkbackup.cmd' | Out-Null
Write-Output "UI-Backup 已同步到当前状态"

# 只保留最近 10 份，避免快照越堆越多
$all = Get-ChildItem -Path 'D:\mod\OMniX\snapshots' -Directory | Sort-Object Name -Descending
if ($all.Count -gt 10) {
    $all | Select-Object -Skip 10 | ForEach-Object { Remove-Item $_.FullName -Recurse -Force }
    Write-Output ("已清理旧快照，保留最近 10 份")
}
