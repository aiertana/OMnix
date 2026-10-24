$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Drawing

$dir = 'D:\mod\OMniX\shot'
$a = [System.Drawing.Bitmap]::new((Join-Path $dir 'flow_f1.png'))
$b = [System.Drawing.Bitmap]::new((Join-Path $dir 'flow_f2.png'))
$c = [System.Drawing.Bitmap]::new((Join-Path $dir 'flow_f3.png'))

Write-Output ("尺寸: " + $a.Width + "x" + $a.Height)
Write-Output ""

function DiffInfo($p, $q, $label)
{
    $rows = @{}
    $cols = @{}
    $total = 0

    for ($y = 0; $y -lt $p.Height; $y++) {
        for ($x = 0; $x -lt $p.Width; $x++) {
            $pa = $p.GetPixel($x, $y)
            $pb = $q.GetPixel($x, $y)
            $d = [Math]::Abs($pa.R - $pb.R) + [Math]::Abs($pa.G - $pb.G) + [Math]::Abs($pa.B - $pb.B)
            if ($d -gt 25) {
                $total++
                if (-not $rows.ContainsKey($y)) { $rows[$y] = 0 }
                $rows[$y]++
                if (-not $cols.ContainsKey($x)) { $cols[$x] = 0 }
                $cols[$x]++
            }
        }
    }

    Write-Output ($label + " 差异像素: " + $total + " / " + ($p.Width * $p.Height))

    if ($total -gt 0) {
        $rowKeys = $rows.Keys | Sort-Object
        $colKeys = $cols.Keys | Sort-Object
        Write-Output ("   有差异的行: " + (($rowKeys | ForEach-Object { "$_(" + $rows[$_] + ")" }) -join " "))
        Write-Output ("   有差异的列: " + (($colKeys | ForEach-Object { "$_(" + $cols[$_] + ")" }) -join " "))
    }
    Write-Output ""
}

DiffInfo $a $b "帧1 vs 帧2"
DiffInfo $b $c "帧2 vs 帧3"

$a.Dispose(); $b.Dispose(); $c.Dispose()
