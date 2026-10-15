# 은행 모드 빌드 스크립트 — 윈도우에 기본으로 들어 있는 .NET Framework 4 컴파일러(csc.exe)만 사용
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$bin = Join-Path $root 'bin'
$ico = Join-Path $src 'app.ico'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# 컴파일러를 찾을 수 없어요: $csc" }

# 아이콘 그림 (파란 둥근 사각형 + ₩) — 앱 아이콘과 크롬 확장 아이콘에 같이 쓴다
Add-Type -AssemblyName System.Drawing
function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)
    $d = [Math]::Max(2, [int]($size * 0.44))
    $w = $size - 1
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90)
    $path.AddArc(0, $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(59, 130, 246)), ([System.Drawing.Color]::FromArgb(29, 78, 216)), 90
    $g.FillPath($brush, $path)
    $font = New-Object System.Drawing.Font 'Segoe UI', ([single]($size * 0.62)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString([string][char]0x20A9, $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, ([single]($size * 0.02)), $size, $size), $sf)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

# 아이콘이 없으면 그려서 만든다
if (-not (Test-Path $ico)) {
    $sizes = 16, 24, 32, 48, 64, 256
    $pngs = foreach ($s in $sizes) { , (New-IconPng $s) }
    $out = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $out
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i] % 256
        $bw.Write([byte]$s); $bw.Write([byte]$s); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write([byte[]]$p) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($ico, $out.ToArray())
    Write-Host "아이콘 생성: $ico"
}

# 크롬 확장 아이콘 (없을 때만)
$extIcons = Join-Path $root 'chrome-extension\icons'
foreach ($s in 16, 32, 48, 128) {
    $png = Join-Path $extIcons "icon$s.png"
    if (-not (Test-Path $png)) {
        New-Item -ItemType Directory -Force $extIcons | Out-Null
        [System.IO.File]::WriteAllBytes($png, (New-IconPng $s))
        Write-Host "확장 아이콘 생성: $png"
    }
}

New-Item -ItemType Directory -Force $bin | Out-Null
$exe = Join-Path $bin 'BankMode.exe'
& $csc /nologo /target:winexe /codepage:65001 /optimize+ /platform:anycpu `
    "/out:$exe" "/win32icon:$ico" "/win32manifest:$(Join-Path $src 'app.manifest')" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.ServiceProcess.dll /r:System.Web.Extensions.dll `
    "$src\*.cs"
if ($LASTEXITCODE -ne 0) { throw "빌드 실패 (위 오류 참고)" }
Write-Host "빌드 완료: $exe"
