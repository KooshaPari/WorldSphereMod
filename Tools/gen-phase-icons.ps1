param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot "..\WorldSphereMod\GameResources\PhaseIcons")
)

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

$null = New-Item -ItemType Directory -Force -Path $OutputRoot

function New-Canvas {
    param([int]$Size = 64)

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $gfx.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $gfx.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gfx.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    return @($bmp, $gfx)
}

function Fill-GradientBackground {
    param(
        [System.Drawing.Graphics]$Gfx,
        [System.Drawing.Color]$Top,
        [System.Drawing.Color]$Bottom
    )

    $rect = New-Object System.Drawing.Rectangle 0, 0, 64, 64
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $Top, $Bottom, 90
    $Gfx.FillRectangle($brush, $rect)
    $brush.Dispose()

    for ($i = 0; $i -lt 4; $i++) {
        $alpha = 18 - ($i * 3)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb($alpha, 255, 255, 255)), 1
        $Gfx.DrawEllipse($pen, 2 + $i, 2 + $i, 60 - ($i * 2), 60 - ($i * 2))
        $pen.Dispose()
    }
}

function Save-Icon {
    param(
        [string]$Name,
        [scriptblock]$Draw
    )

    $bmp, $gfx = New-Canvas
    try {
        & $Draw $gfx
        $path = Join-Path $OutputRoot "$Name.png"
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host $path
    } finally {
        $gfx.Dispose()
        $bmp.Dispose()
    }
}

$icons = @(
    @{
        Name = 'VoxelEntities'
        Draw = {
            param($g)
            Fill-GradientBackground $g ([System.Drawing.Color]::FromArgb(53, 92, 112)) ([System.Drawing.Color]::FromArgb(20, 36, 48))
            $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 235, 240))
            $g.FillRectangle($brush, 16, 16, 12, 12)
            $g.FillRectangle($brush, 28, 16, 12, 12)
            $g.FillRectangle($brush, 22, 28, 12, 12)
            $brush.Dispose()
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 136, 84)), 3
            $g.DrawRectangle($pen, 14, 14, 34, 34)
            $pen.Dispose()
        }
    },
    @{
        Name = 'ProceduralBuildings'
        Draw = {
            param($g)
            Fill-GradientBackground $g ([System.Drawing.Color]::FromArgb(99, 73, 45)) ([System.Drawing.Color]::FromArgb(43, 28, 18))
            $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(224, 190, 120))
            $g.FillRectangle($brush, 15, 26, 14, 22)
            $g.FillRectangle($brush, 30, 18, 19, 30)
            $g.FillRectangle($brush, 48, 30, 10, 18)
            $brush.Dispose()
            $roof = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(230, 120, 70)), 4
            $g.DrawLine($roof, 14, 26, 22, 18)
            $g.DrawLine($roof, 29, 18, 39, 10)
            $g.DrawLine($roof, 47, 30, 53, 24)
            $roof.Dispose()
        }
    },
    @{
        Name = 'CrossedQuadFoliage'
        Draw = {
            param($g)
            Fill-GradientBackground $g ([System.Drawing.Color]::FromArgb(42, 102, 56)) ([System.Drawing.Color]::FromArgb(16, 40, 20))
            $leaf = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(102, 196, 92))
            $g.FillEllipse($leaf, 14, 16, 22, 30)
            $g.FillEllipse($leaf, 28, 14, 22, 30)
            $leaf.Dispose()
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(74, 56, 28)), 4
            $g.DrawLine($pen, 20, 52, 32, 12)
            $g.DrawLine($pen, 44, 52, 32, 12)
            $pen.Dispose()
        }
    },
    @{
        Name = 'MeshWater'
        Draw = {
            param($g)
            Fill-GradientBackground $g ([System.Drawing.Color]::FromArgb(31, 121, 175)) ([System.Drawing.Color]::FromArgb(10, 44, 90))
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(176, 240, 255)), 4
            for ($y = 18; $y -le 42; $y += 10) {
                $g.DrawArc($pen, 8, $y, 48, 12, 180, 180)
            }
            $pen.Dispose()
        }
    },
    @{
        Name = 'HighShadows'
        Draw = {
            param($g)
            Fill-GradientBackground $g ([System.Drawing.Color]::FromArgb(96, 96, 114)) ([System.Drawing.Color]::FromArgb(22, 22, 28))
            $shadow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(180, 20, 20, 20))
            $g.FillEllipse($shadow, 20, 20, 24, 24)
            $shadow.Dispose()
            $sun = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 240, 160))
            $g.FillEllipse($sun, 10, 10, 14, 14)
            $sun.Dispose()
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 240, 160)), 2
            $g.DrawLine($pen, 17, 17, 30, 30)
            $pen.Dispose()
        }
    },
    @{
        Name = 'DayNightCycle'
        Draw = {
            param($g)
            Fill-GradientBackground $g ([System.Drawing.Color]::FromArgb(70, 77, 143)) ([System.Drawing.Color]::FromArgb(17, 20, 43))
            $sun = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 208, 96))
            $g.FillEllipse($sun, 10, 12, 16, 16)
            $sun.Dispose()
            $moon = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(216, 226, 255))
            $g.FillEllipse($moon, 38, 18, 14, 14)
            $moon.Dispose()
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 255)), 3
            $g.DrawArc($pen, 14, 12, 34, 34, 220, 220)
            $pen.Dispose()
        }
    },
    @{
        Name = 'SkeletalAnimation'
        Draw = {
            param($g)
            Fill-GradientBackground $g ([System.Drawing.Color]::FromArgb(118, 92, 121)) ([System.Drawing.Color]::FromArgb(41, 23, 40))
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(248, 240, 230)), 4
            $g.DrawEllipse($pen, 24, 10, 16, 16)
            $g.DrawLine($pen, 32, 26, 32, 44)
            $g.DrawLine($pen, 32, 30, 20, 38)
            $g.DrawLine($pen, 32, 30, 44, 38)
            $g.DrawLine($pen, 32, 44, 24, 56)
            $g.DrawLine($pen, 32, 44, 40, 56)
            $pen.Dispose()
        }
    },
    @{
        Name = 'WorldspaceUI'
        Draw = {
            param($g)
            Fill-GradientBackground $g ([System.Drawing.Color]::FromArgb(60, 92, 126)) ([System.Drawing.Color]::FromArgb(18, 33, 51))
            $panel = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(225, 240, 255))
            $g.FillRectangle($panel, 12, 14, 40, 28)
            $panel.Dispose()
            $accent = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 160, 255)), 4
            $g.DrawLine($accent, 18, 24, 46, 24)
            $g.DrawLine($accent, 18, 34, 38, 34)
            $accent.Dispose()
        }
    },
    @{
        Name = 'HdrSkybox'
        Draw = {
            param($g)
            Fill-GradientBackground $g ([System.Drawing.Color]::FromArgb(92, 129, 208)) ([System.Drawing.Color]::FromArgb(18, 33, 82))
            $glow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 255, 190, 80))
            $g.FillEllipse($glow, 20, 16, 24, 24)
            $glow.Dispose()
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 255)), 2
            $g.DrawRectangle($pen, 14, 14, 36, 36)
            $pen.Dispose()
        }
    },
    @{
        Name = 'SSGIEnabled'
        Draw = {
            param($g)
            Fill-GradientBackground $g ([System.Drawing.Color]::FromArgb(87, 67, 143)) ([System.Drawing.Color]::FromArgb(30, 18, 68))
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(190, 255, 122)), 4
            $g.DrawEllipse($pen, 16, 16, 32, 32)
            $g.DrawLine($pen, 24, 32, 30, 40)
            $g.DrawLine($pen, 30, 40, 42, 22)
            $pen.Dispose()
        }
    }
)

foreach ($icon in $icons) {
    Save-Icon -Name $icon.Name -Draw $icon.Draw
}
