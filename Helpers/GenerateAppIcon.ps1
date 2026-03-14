$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

function New-PointF {
    param(
        [double]$X,
        [double]$Y
    )

    return [System.Drawing.PointF]::new([float]$X, [float]$Y)
}

function Get-CirclePoint {
    param(
        [double]$CenterX,
        [double]$CenterY,
        [double]$Radius,
        [double]$AngleDegrees
    )

    $angleRadians = [Math]::PI * $AngleDegrees / 180.0
    $x = $CenterX + ($Radius * [Math]::Cos($angleRadians))
    $y = $CenterY + ($Radius * [Math]::Sin($angleRadians))
    return (New-PointF -X $x -Y $y)
}

function Get-TangentVector {
    param([double]$AngleDegrees)

    $angleRadians = [Math]::PI * $AngleDegrees / 180.0
    $x = -[Math]::Sin($angleRadians)
    $y = [Math]::Cos($angleRadians)

    $length = [Math]::Sqrt(($x * $x) + ($y * $y))
    return @{
        X = $x / $length
        Y = $y / $length
    }
}

function Add-ArrowHead {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Brush]$Brush,
        [System.Drawing.PointF]$Tip,
        [hashtable]$Direction,
        [double]$Length,
        [double]$HalfWidth
    )

    $baseX = $Tip.X - ($Direction.X * $Length)
    $baseY = $Tip.Y - ($Direction.Y * $Length)
    $perpX = -$Direction.Y
    $perpY = $Direction.X

    $points = [System.Drawing.PointF[]]@(
        $Tip,
        (New-PointF -X ($baseX + ($perpX * $HalfWidth)) -Y ($baseY + ($perpY * $HalfWidth))),
        (New-PointF -X ($baseX - ($perpX * $HalfWidth)) -Y ($baseY - ($perpY * $HalfWidth)))
    )

    $Graphics.FillPolygon($Brush, $points)
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $repoRoot 'Assets'

if (-not (Test-Path $assetsDir)) {
    New-Item -ItemType Directory -Path $assetsDir | Out-Null
}

$baseSize = 256
$center = 128.0
$strokeWidth = 22.0
$radius = 76.0
$bounds = [System.Drawing.RectangleF]::new(52, 52, 152, 152)
$shadowBounds = [System.Drawing.RectangleF]::new(55, 58, 152, 152)

$bitmap = [System.Drawing.Bitmap]::new($baseSize, $baseSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$graphics.Clear([System.Drawing.Color]::Transparent)

$darkLoopColor = [System.Drawing.Color]::FromArgb(255, 28, 25, 31)
$accentColor = [System.Drawing.Color]::FromArgb(255, 99, 28, 38)
$accentGlowColor = [System.Drawing.Color]::FromArgb(120, 120, 38, 49)
$shadowColor = [System.Drawing.Color]::FromArgb(28, 0, 0, 0)

$shadowPen = [System.Drawing.Pen]::new($shadowColor, [float]$strokeWidth)
$shadowPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$shadowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

$darkPen = [System.Drawing.Pen]::new($darkLoopColor, [float]$strokeWidth)
$darkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$darkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$darkPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

$accentGlowPen = [System.Drawing.Pen]::new($accentGlowColor, 10.0)
$accentGlowPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$accentGlowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

$accentPen = [System.Drawing.Pen]::new($accentColor, [float]$strokeWidth)
$accentPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$accentPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$accentPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

$darkBrush = [System.Drawing.SolidBrush]::new($darkLoopColor)
$accentBrush = [System.Drawing.SolidBrush]::new($accentColor)

$graphics.DrawArc($shadowPen, $shadowBounds, 222, 148)
$graphics.DrawArc($shadowPen, $shadowBounds, 42, 148)

$graphics.DrawArc($darkPen, $bounds, 222, 148)
$graphics.DrawArc($darkPen, $bounds, 42, 148)
$graphics.DrawArc($accentGlowPen, $bounds, 332, 52)
$graphics.DrawArc($accentPen, $bounds, 338, 44)

$rightArrowAngle = 16.0
$leftArrowAngle = 196.0
$arrowTipInset = 6.0

$rightTip = Get-CirclePoint -CenterX $center -CenterY $center -Radius ($radius + $arrowTipInset) -AngleDegrees $rightArrowAngle
$leftTip = Get-CirclePoint -CenterX $center -CenterY $center -Radius ($radius + $arrowTipInset) -AngleDegrees $leftArrowAngle

$rightDirection = Get-TangentVector -AngleDegrees $rightArrowAngle
$leftDirection = Get-TangentVector -AngleDegrees $leftArrowAngle

Add-ArrowHead -Graphics $graphics -Brush $accentBrush -Tip $rightTip -Direction $rightDirection -Length 26 -HalfWidth 14
Add-ArrowHead -Graphics $graphics -Brush $darkBrush -Tip $leftTip -Direction $leftDirection -Length 26 -HalfWidth 14

$pngPath = Join-Path $assetsDir 'AppIcon.png'
$icoPath = Join-Path $assetsDir 'AppIcon.ico'
$bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

$iconSizes = @(16, 24, 32, 48, 64, 256)
$pngFrames = @()

foreach ($iconSize in $iconSizes) {
    if ($iconSize -eq $baseSize) {
        $frameBitmap = [System.Drawing.Bitmap]$bitmap.Clone()
    }
    else {
        $frameBitmap = [System.Drawing.Bitmap]::new($iconSize, $iconSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $frameGraphics = [System.Drawing.Graphics]::FromImage($frameBitmap)
        $frameGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $frameGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $frameGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $frameGraphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $frameGraphics.Clear([System.Drawing.Color]::Transparent)
        $frameGraphics.DrawImage($bitmap, 0, 0, $iconSize, $iconSize)
        $frameGraphics.Dispose()
    }

    $memoryStream = [System.IO.MemoryStream]::new()
    $frameBitmap.Save($memoryStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngFrames += [PSCustomObject]@{
        Size = $iconSize
        Bytes = $memoryStream.ToArray()
    }

    $memoryStream.Dispose()
    $frameBitmap.Dispose()
}

$fileStream = [System.IO.File]::Create($icoPath)
$writer = [System.IO.BinaryWriter]::new($fileStream)

$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$pngFrames.Count)

$offset = 6 + (16 * $pngFrames.Count)
foreach ($frame in $pngFrames) {
    $dimensionByte = if ($frame.Size -ge 256) { [byte]0 } else { [byte]$frame.Size }

    $writer.Write($dimensionByte)
    $writer.Write($dimensionByte)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frame.Bytes.Length)
    $writer.Write([UInt32]$offset)

    $offset += $frame.Bytes.Length
}

foreach ($frame in $pngFrames) {
    $writer.Write($frame.Bytes)
}

$writer.Dispose()
$fileStream.Dispose()
$darkBrush.Dispose()
$accentBrush.Dispose()
$shadowPen.Dispose()
$darkPen.Dispose()
$accentGlowPen.Dispose()
$accentPen.Dispose()
$graphics.Dispose()
$bitmap.Dispose()
