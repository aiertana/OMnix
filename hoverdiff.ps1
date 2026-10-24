$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Drawing

$p = 'D:\mod\OMniX\shot\h_rest.png'
$q = 'D:\mod\OMniX\shot\h_hover.png'

$a = [System.Drawing.Bitmap]::new($p)
$b = [System.Drawing.Bitmap]::new($q)

Write-Output ("尺寸 " + $a.Width + "x" + $a.Height)

$diff = 0
$minY = 999
$maxY = -1

for ($y = 0; $y -lt $a.Height; $y++) {
    for ($x = 0; $x -lt $a.Width; $x++) {
        $pa = $a.GetPixel($x, $y)
        $pb = $b.GetPixel($x, $y)
        $d = [Math]::Abs($pa.R - $pb.R) + [Math]::Abs($pa.G - $pb.G) + [Math]::Abs($pa.B - $pb.B)
        if ($d -gt 20) {
            $diff++
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}

Write-Output ("两图差异像素: " + $diff + "   差异行范围: " + $minY + " ~ " + $maxY)

# 逐行输出上边缘附近每行有多少差异，看是不是整体位移
for ($y = 18; $y -lt 34; $y++) {
    $n = 0
    for ($x = 0; $x -lt $a.Width; $x++) {
        $pa = $a.GetPixel($x, $y)
        $pb = $b.GetPixel($x, $y)
        $d = [Math]::Abs($pa.R - $pb.R) + [Math]::Abs($pa.G - $pb.G) + [Math]::Abs($pa.B - $pb.B)
        if ($d -gt 20) { $n++ }
    }
    Write-Output ("  y=" + $y + " 差异 " + $n)
}

$a.Dispose(); $b.Dispose()
