<#
.SYNOPSIS
Builds docs/readme-flow.gif from the frames in docs/demo/frames.

.DESCRIPTION
Each frame gets a 1 px border and, where the next click goes, a click ring. ffmpeg then cross-fades the frames into one GIF that loops forever. To change the animation, replace a frame with a new capture of the same size, or edit the table below. Needs ffmpeg (winget install Gyan.FFmpeg) and PowerShell on Windows.

.EXAMPLE
pwsh RecDiffy.ExcelAddIn/docs/demo/make-gif.ps1
#>
param(
    [string]$FFmpeg = 'ffmpeg',
    [string]$Output = (Join-Path $PSScriptRoot '..\readme-flow.gif'),
    [double]$Fade = 0.3,
    [int]$Fps = 10
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$frames = @(
    @{ File = '01-ribbon.png';         Hold = 1.6; Click = @(49, 52) }
    @{ File = '02-sources.png';        Hold = 1.4; Click = @(686, 353) }
    @{ File = '03-pick.png';           Hold = 1.8; Click = @(239, 496) }
    @{ File = '04-sources-chosen.png'; Hold = 1.8; Click = @(1205, 762) }
    @{ File = '05-rules.png';          Hold = 2.2; Click = @(1342, 763) }
    @{ File = '06-results.png';        Hold = 2.2; Click = @(570, 380) }
    @{ File = '07-explain.png';        Hold = 2.4; Click = @(1335, 204) }
    @{ File = '08-worksheet.png';      Hold = 3.4; Click = $null }
)

$invariant = [Globalization.CultureInfo]::InvariantCulture
$border = [Drawing.Color]::FromArgb(0xD0, 0xD7, 0xDE)
$ring = [Drawing.Color]::FromArgb(0xE8, 0x6A, 0x10)

function Write-Frame([hashtable]$Frame, [string]$Path) {
    $source = [Drawing.Bitmap]::new((Join-Path $PSScriptRoot "frames\$($Frame.File)"))
    $canvas = [Drawing.Bitmap]::new($source.Width + 2, $source.Height + 2, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [Drawing.Graphics]::FromImage($canvas)
    try {
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear($border)
        $g.DrawImage($source, 1, 1, $source.Width, $source.Height)
        if ($Frame.Click) {
            $x = $Frame.Click[0] + 1
            $y = $Frame.Click[1] + 1
            $fill = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(55, $ring))
            $pen = [Drawing.Pen]::new($ring, 3)
            $g.FillEllipse($fill, $x - 15, $y - 15, 30, 30)
            $g.DrawEllipse($pen, $x - 21, $y - 21, 42, 42)
            $fill.Dispose(); $pen.Dispose()
        }
        $canvas.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $g.Dispose(); $canvas.Dispose(); $source.Dispose()
    }
}

$work = Join-Path ([IO.Path]::GetTempPath()) "recdiffy-gif-$PID"
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $last = $frames.Count - 1
    $inputs = 0..$last | ForEach-Object {
        $path = Join-Path $work ('{0:D2}.png' -f $_)
        Write-Frame $frames[$_] $path
        $length = if ($_ -eq $last) { $frames[$_].Hold } else { $frames[$_].Hold + $Fade }
        '-loop', '1', '-framerate', "$Fps", '-t', $length.ToString($invariant), '-i', $path
    }

    $prepare = 0..$last | ForEach-Object { "[${_}:v]format=gbrp,fps=$Fps,settb=AVTB[v$_]" }
    $fadeText = $Fade.ToString($invariant)
    $fades = 1..$last | ForEach-Object {
        $from = if ($_ -eq 1) { '[v0]' } else { "[x$($_ - 1)]" }
        $offset = ($frames[0..($_ - 1)] | Measure-Object -Property Hold -Sum).Sum
        "$from[v$_]xfade=transition=fade:duration=${fadeText}:offset=$($offset.ToString($invariant))[x$_]"
    }
    $palette = "[x$last]split[a][b];[a]palettegen=max_colors=256:stats_mode=full[p];[b][p]paletteuse=dither=none:diff_mode=rectangle"
    $filter = (@($prepare) + @($fades) + $palette) -join ';'

    & $FFmpeg -hide_banner -loglevel error -y @inputs -filter_complex $filter -loop 0 $Output
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed with exit code $LASTEXITCODE." }
    Get-Item $Output | Select-Object Name, Length
}
finally {
    Remove-Item -Recurse -Force $work
}
