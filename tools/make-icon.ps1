# Creates app.ico (16..256 px, PNG frames): blue rounded square, white clock, green "play" mark.
param([string]$Out = (Join-Path $PSScriptRoot '..\app.ico'))
Add-Type -AssemblyName System.Drawing

function New-Frame([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $r = [single]($s * 0.22); $m = [single]($s * 0.03); $w = [single]($s - 2 * $m)
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, $r, $r, 180, 90)
    $path.AddArc($m + $w - $r, $m, $r, $r, 270, 90)
    $path.AddArc($m + $w - $r, $m + $w - $r, $r, $r, 0, 90)
    $path.AddArc($m, $m + $w - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 37, 99, 235))), $path)

    $c = [single]($s * 0.45); $cr = [single]($s * 0.30)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::White), ([single][Math]::Max(1.2, $s * 0.075))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawEllipse($pen, $c - $cr, $c - $cr, 2 * $cr, 2 * $cr)
    $g.DrawLine($pen, $c, $c, $c, [single]($c - $cr * 0.62))
    $g.DrawLine($pen, $c, $c, [single]($c + $cr * 0.5), $c)

    $t = [single]($s * 0.30); $x = [single]($s * 0.62); $y = [single]($s * 0.60)
    $pts = [Drawing.PointF[]]@((New-Object Drawing.PointF $x, $y), (New-Object Drawing.PointF $x, ($y + $t)), (New-Object Drawing.PointF ($x + $t * 0.87), ($y + $t / 2)))
    $g.FillPolygon((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 34, 197, 94))), $pts)
    $g.DrawPolygon((New-Object Drawing.Pen ([Drawing.Color]::White), ([single][Math]::Max(1, $s * 0.03))), $pts)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    if ($s -ge 256) {
        $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Classic DIB frame: BITMAPINFOHEADER + 32-bit BGRA rows (bottom-up) + AND mask.
        $bw = New-Object IO.BinaryWriter $ms
        $bw.Write([uint32]40); $bw.Write([int32]$s); $bw.Write([int32]($s * 2))
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
        $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
        $bw.Write([uint32]($s * $s * 4 + $maskStride * $s))
        $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
        for ($y = $s - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $s; $x++) {
                $px = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$px.B); $bw.Write([byte]$px.G); $bw.Write([byte]$px.R); $bw.Write([byte]$px.A)
            }
        }
        $bw.Write((New-Object byte[] ($maskStride * $s)))
        $bw.Flush()
    }
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = $sizes | ForEach-Object { ,(New-Frame $_) }
$fs = [IO.File]::Create($Out); $bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$frames[$i].Length); $bw.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Close()
Write-Host "Wrote $Out"
