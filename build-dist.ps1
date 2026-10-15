# 은행 모드 배포 묶음 만들기
#  1) 앱 빌드 (build.ps1)
#  2) 크롬 웹스토어 업로드용 확장 zip (개발용 key 제거)
#  3) Inno Setup이 있으면 설치 프로그램(dist\BankMode-Setup.exe)까지 컴파일
# 결과물은 모두 dist\ 에 모입니다.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

# manifest.json에서 버전 읽기
$ext = Join-Path $root 'chrome-extension'
$manifest = Get-Content (Join-Path $ext 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$ver = $manifest.version

Write-Host "== 1/3  앱 빌드 =="
& (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw "앱 빌드 실패" }

Write-Host "== 2/3  크롬 웹스토어용 확장 zip =="
# 임시 폴더에 확장을 복사하고, 업로드본에서는 개발용 key를 뺀다
# (웹스토어가 ID를 새로 배정하므로. 배정된 ID는 앱의 extension-ids.txt에 넣으면 됨)
$tmp = Join-Path $env:TEMP ("bankmode-ext-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    Copy-Item (Join-Path $ext '*') $tmp -Recurse -Force
    $mPath = Join-Path $tmp 'manifest.json'
    $m = Get-Content $mPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($m.PSObject.Properties.Name -contains 'key') {
        $m.PSObject.Properties.Remove('key')
        Write-Host "  업로드본에서 개발용 key 제거"
    }
    # Chrome은 \uXXXX 이스케이프된 JSON도 받는다. manifest.json은 BOM 없이 저장(BOM 있으면 크롬이 거부할 수 있음)
    [IO.File]::WriteAllText($mPath, ($m | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
    $zip = Join-Path $dist ("bankmode-extension-store-v$ver.zip")
    if (Test-Path $zip) { Remove-Item $zip -Force }
    # zip 항목 경로는 반드시 정방향 슬래시('icons/icon16.png')여야 함.
    # Compress-Archive는 역슬래시를 넣으므로 ZipArchive로 직접 만든다.
    Add-Type -AssemblyName System.IO.Compression | Out-Null
    $fs = [IO.File]::Open($zip, [IO.FileMode]::Create)
    $arch = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $tmp -Recurse -File) {
            $rel = $file.FullName.Substring($tmp.Length).TrimStart('\', '/').Replace('\', '/')
            $entry = $arch.CreateEntry($rel, [IO.Compression.CompressionLevel]::Optimal)
            $es = $entry.Open()
            $bytes = [IO.File]::ReadAllBytes($file.FullName)
            $es.Write($bytes, 0, $bytes.Length)
            $es.Dispose()
        }
    }
    finally { $arch.Dispose(); $fs.Dispose() }
    Write-Host "  생성: $zip"
}
finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host "== 3/3  설치 프로그램 =="
$iscc = $null
foreach ($p in (Join-Path $root 'tools\InnoSetup\ISCC.exe'), "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") {
    if (Test-Path $p) { $iscc = $p; break }
}
if ($iscc) {
    & $iscc (Join-Path $root 'installer\BankMode.iss')
    if ($LASTEXITCODE -ne 0) { throw "설치 프로그램 컴파일 실패" }
    Write-Host "  생성: $(Join-Path $dist 'BankMode-Setup.exe')"
} else {
    Write-Host "  Inno Setup(ISCC.exe)이 없어 설치 프로그램은 건너뜀."
    Write-Host "  설치: winget install JRSoftware.InnoSetup  또는  https://jrsoftware.org/isdl.php"
    Write-Host "  그 뒤 다시 실행하면 dist\BankMode-Setup.exe 가 만들어집니다."
}

Write-Host ""
Write-Host "== dist 폴더 =="
Get-ChildItem $dist | Select-Object Name, @{n='KB';e={[math]::Round($_.Length/1KB)}} | Format-Table -AutoSize
