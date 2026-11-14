Add-Type -AssemblyName System.Drawing

# Растры чекбокса макета (visual/kern-ui: screens.css .check, micro.css input[type=checkbox]):
# 18x18, поле well, шов seam-strong; отмеченный — галочка костью. Формат 1:1 с
# дефолтными ключами SelectablePacket протокола (gui/check/generic_on|off.png).

function New-Checkbox([bool]$checked, [string]$outPath) {
    $size = 18
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    # Поле well + шов seam-strong
    $well = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(10, 9, 15))
    $g.FillRectangle($well, 0, 0, $size, $size)
    $seam = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(74, 70, 104), 1)
    $g.DrawRectangle($seam, 0.5, 0.5, $size - 1, $size - 1)

    if ($checked) {
        # Отмеченный: тёмный квадрат с белым (кость) квадратом внутри —
        # как чекбокс макета, без глифа галочки.
        $inner = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(216, 213, 238))
        $g.FillRectangle($inner, 5, 5, $size - 10, $size - 10)
    }

    $g.Dispose()
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "saved $outPath"
}

$dir = "C:\Users\Melon\Desktop\NewMinesServer\MinesServer\assets\gui\check"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
New-Checkbox $false "$dir\generic_off.png"
New-Checkbox $true "$dir\generic_on.png"
