Add-Type -AssemblyName System.Drawing

# Шевроны главного действия (visual/kern-ui index.html symbol #i-arrows, systems.css .btn-arrows):
# три «>» штрихом 1.5 в viewBox 24x24, цвет — on-signal поверх сигнала (тинт rgb(92,31,11) из Button.uss).
# Рендер 40x40 (масштаб 40/24), на кнопке отображается 20x20.
$size = 40
$scale = $size / 24.0
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(92, 31, 11), [single](1.5 * $scale))

function P([single]$x, [single]$y) {
    return (New-Object System.Drawing.PointF([single]($x * $scale), [single]($y * $scale)))
}

# m2 7.5 6 4.5 -6 4.5 — три шеврона со сдвигом 7 по x
foreach ($offset in @(0, 7, 14)) {
    $pts = @((P (2 + $offset) 7.5), (P (8 + $offset) 12), (P (2 + $offset) 16.5))
    $g.DrawLines($pen, $pts)
}

$g.Dispose()
$out = "C:\Users\Melon\Desktop\NewMinesServer\MinesServer\assets\UI\brand\arrows.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "saved $out"
