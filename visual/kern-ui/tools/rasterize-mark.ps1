Add-Type -AssemblyName System.Drawing

# Растр знака Kern из visual/kern-ui/img/brand/mark.svg (viewBox 0 0 64 64).
# Рендер 224x224 (масштаб 3.5) с полями 2px: ImagePacket запрашивает 56x56,
# серверный ассет может быть больше — клиент масштабирует фоном.
$size = 224
$scale = $size / 64.0
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

function P($points) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pts = @()
    foreach ($p in $points) { $pts += ,@([single]($p[0] * $scale), [single]($p[1] * $scale)) }
    $path.AddLines(@($pts | ForEach-Object { New-Object System.Drawing.PointF($_[0], $_[1]) }))
    $path.CloseFigure()
    return $path
}

# Внешний шестигранник: M32 3.5 56.7 17.75 56.7 46.25 32 60.5 7.3 46.25 7.3 17.75 z
$outer = P @(@(32,3.5),@(56.7,17.75),@(56.7,46.25),@(32,60.5),@(7.3,46.25),@(7.3,17.75))
$bonePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(216,213,238), [single](3 * $scale))
$bonePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Miter
$g.DrawPath($bonePen, $outer)

# Внутренний шестигранник: заливка графит + шов 1.25 op .7
$inner = P @(@(32,13),@(48.45,22.5),@(48.45,41.5),@(32,51),@(15.55,41.5),@(15.55,22.5))
$innerFill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(17,16,24))
$g.FillPath($innerFill, $inner)
$innerPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(178,216,213,238), [single](1.25 * $scale))
$innerPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Miter
$g.DrawPath($innerPen, $inner)

# Ядро-ромб
$core = P @(@(32,22),@(42,32),@(32,42),@(22,32))
$coreFill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,90,31))
$g.FillPath($coreFill, $core)

# Засечки у вершин: шесть коротких штрихов 1.25 op .7
$tickPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(178,216,213,238), [single](1.25 * $scale))
$ticks = @(
    @(@(32,9),@(32,7)), @(@(52,21),@(53.7,20)), @(@(52,43),@(53.7,44)),
    @(@(32,55),@(32,57)), @(@(12,43),@(10.3,44)), @(@(12,21),@(10.3,20))
)
foreach ($t in $ticks) {
    $x1 = [single]($t[0][0] * $scale); $y1 = [single]($t[0][1] * $scale)
    $x2 = [single]($t[1][0] * $scale); $y2 = [single]($t[1][1] * $scale)
    $g.DrawLine($tickPen, $x1, $y1, $x2, $y2)
}

$g.Dispose()
$out = "C:\Users\Melon\Desktop\NewMinesServer\MinesServer\assets\UI\brand\mark.png"
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "saved $out"
