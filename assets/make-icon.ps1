# Renders the REX phone mark at several sizes and packs them into assets/rex.ico.
param([string]$Output = (Join-Path $PSScriptRoot "rex.ico"))
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.Clear([System.Drawing.Color]::Transparent)
  $ink = [System.Drawing.Color]::FromArgb(255, 8, 10, 9)
  $signal = [System.Drawing.Color]::FromArgb(255, 215, 255, 63)
  $r = [Math]::Max(2, $s * 0.22)
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0, 0, $r*2, $r*2, 180, 90); $path.AddArc($s-$r*2-1, 0, $r*2, $r*2, 270, 90)
  $path.AddArc($s-$r*2-1, $s-$r*2-1, $r*2, $r*2, 0, 90); $path.AddArc(0, $s-$r*2-1, $r*2, $r*2, 90, 90); $path.CloseFigure()
  $g.FillPath((New-Object System.Drawing.SolidBrush $ink), $path)
  $pw = [Math]::Max(1.2, $s * 0.075)
  $pen = New-Object System.Drawing.Pen $signal, $pw
  $pen.LineJoin = 'Round'; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
  $pl = $s*0.33; $pt = $s*0.17; $pwid = $s*0.34; $ph = $s*0.66; $pr = [Math]::Max(1, $s*0.09)
  $phone = New-Object System.Drawing.Drawing2D.GraphicsPath
  $phone.AddArc($pl, $pt, $pr*2, $pr*2, 180, 90); $phone.AddArc($pl+$pwid-$pr*2, $pt, $pr*2, $pr*2, 270, 90)
  $phone.AddArc($pl+$pwid-$pr*2, $pt+$ph-$pr*2, $pr*2, $pr*2, 0, 90); $phone.AddArc($pl, $pt+$ph-$pr*2, $pr*2, $pr*2, 90, 90); $phone.CloseFigure()
  $g.DrawPath($pen, $phone)
  $g.DrawLine($pen, $pl+$pwid*0.3, $pt+$ph*0.14, $pl+$pwid*0.7, $pt+$ph*0.14)
  $g.DrawLine($pen, $pl+$pwid*0.35, $pt+$ph*0.86, $pl+$pwid*0.65, $pt+$ph*0.86)
  $g.DrawLine($pen, $s*0.19, $s*0.40, $s*0.19, $s*0.60)
  $g.DrawLine($pen, $s*0.81, $s*0.40, $s*0.81, $s*0.60)
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  [pscustomobject]@{ Size = $s; Bytes = $ms.ToArray() }
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
  $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
  $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$f.Bytes.Length); $w.Write([uint32]$offset)
  $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $w.Write($f.Bytes) }
$w.Flush()
[System.IO.File]::WriteAllBytes($Output, $out.ToArray())
Write-Host "wrote $Output ($($out.Length) bytes)"
