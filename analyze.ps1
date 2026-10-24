$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Drawing

$files = @('flow_f1.png', 'flow_f2.png', 'flow_f3.png')
$dir = 'D:\mod\OMniX\shot'

foreach ($f in $files) {
    $path = Join-Path $dir $f
    $bmp = [System.Drawing.Bitmap]::new($path)
    $w = $bmp.Width
    $h = $bmp.Height

    # 按钮在截图里：外扩 16px，所以按钮本体从 (16,16) 开始，宽 w-32，高 h-32
    $left = 16; $top = 16
    $right = $w - 17; $bottom = $h - 17
    $midX = [int]($w / 2); $midY = [int]($h / 2)

    # 边框在按钮外侧 0~2px：取顶边中点上方 1px、右边中点右侧 1px 等
    $samples = @{
        '上' = @($midX, ($top - 1))
        '下' = @($midX, ($bottom + 1))
        '左' = @(($left - 1), $midY)
        '右' = @(($right + 1), $midY)
        '左上角' = @(($left - 1), ($top - 1))
        '右下角' = @(($right + 1), ($bottom + 1))
    }

    $line = $f + " : "
    foreach ($k in @('上', '下', '左', '右', '左上角', '右下角')) {
        $pt = $samples[$k]
        $x = [int]$pt[0]; $y = [int]$pt[1]
        if ($x -lt 0 -or $y -lt 0 -or $x -ge $w -or $y -ge $h) { $line += ($k + "=越界  "); continue }
        $p = $bmp.GetPixel($x, $y)
        $line += ($k + "=" + $p.R + "," + $p.G + "," + $p.B + "  ")
    }
    Write-Output $line
    $bmp.Dispose()
}

Write-Output ""
Write-Output "（如果三行的同一位置颜色不一样，说明边框渐变在转）"
