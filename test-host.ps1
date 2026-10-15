# 크롬 확장용 Native Messaging 호스트 점검 — 크롬이 하듯 stdin에 '4바이트 길이 + JSON'을 써서 보내고 답을 읽는다
#   .\test-host.ps1                 status 확인 (아무것도 바꾸지 않음)
#   .\test-host.ps1 -ViaCmd         크롬처럼 cmd.exe를 거쳐 chrome-extension://…/ 인자로 실행
#   .\test-host.ps1 -Command on     실제로 켜기 (관리자 확인 창이 뜰 수 있음)
param(
    [ValidateSet('status', 'on', 'off')][string]$Command = 'status',
    [string]$Exe = (Join-Path $PSScriptRoot 'bin\BankMode.exe'),
    [switch]$ViaCmd
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Exe)) { throw "실행 파일이 없어요: $Exe (먼저 build.ps1 실행)" }
$Exe = (Resolve-Path $Exe).Path

$psi = New-Object System.Diagnostics.ProcessStartInfo
if ($ViaCmd) {
    # 크롬(윈도우)은 cmd.exe /d /s /c "호스트 경로 확장출처 --parent-window=0" 형태로 실행한다
    $psi.FileName = Join-Path $env:WINDIR 'System32\cmd.exe'
    $psi.Arguments = '/d /s /c ""' + $Exe + '" chrome-extension://pmjnagnjdiidgplgfeckafeamdiflkbe/ --parent-window=0"'
} else {
    $psi.FileName = $Exe
    $psi.Arguments = '--native-host'
}
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.CreateNoWindow = $true
$p = [System.Diagnostics.Process]::Start($psi)
$stdin = $p.StandardInput.BaseStream
$stdout = $p.StandardOutput.BaseStream

function Read-Exact([int]$n) {
    $buf = New-Object byte[] $n
    $got = 0
    while ($got -lt $n) {
        $r = $stdout.Read($buf, $got, $n - $got)
        if ($r -le 0) { throw "호스트가 답하기 전에 연결이 끊겼어요 ($got/$n 바이트)" }
        $got += $r
    }
    return , $buf
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$body = [System.Text.Encoding]::UTF8.GetBytes((@{ cmd = $Command; id = 1 } | ConvertTo-Json -Compress))
$stdin.Write([BitConverter]::GetBytes([int]$body.Length), 0, 4)
$stdin.Write($body, 0, $body.Length)
$stdin.Flush()

$result = $null
while (-not $result) {
    $len = [BitConverter]::ToInt32((Read-Exact 4), 0)
    if ($len -le 0 -or $len -gt 1MB) { throw "길이 값이 이상해요: $len" }
    $json = [System.Text.Encoding]::UTF8.GetString((Read-Exact $len))
    $msg = $json | ConvertFrom-Json   # 올바른 JSON이 아니면 여기서 오류
    if ($msg.type -eq 'progress') { Write-Host "  진행: $($msg.line)" }
    else { $result = $msg; $raw = $json }
}
$elapsed = $sw.ElapsedMilliseconds

# stdin을 닫으면(크롬이 연결을 끊는 것과 같음) 호스트가 스스로 끝나야 한다
$stdin.Close()
if (-not $p.WaitForExit(5000)) { $p.Kill(); throw '연결을 닫았는데 호스트가 끝나지 않았어요' }

Write-Host "응답 ($elapsed ms, $($raw.Length)자):"
Write-Host $raw
Write-Host ''

$problems = @()
if ($result.type -ne 'result') { $problems += "type이 result가 아님: $($result.type)" }
if ($result.id -ne 1) { $problems += "id가 그대로 돌아오지 않음: $($result.id)" }
if ($result.cmd -ne $Command) { $problems += "cmd가 다름: $($result.cmd)" }
if ($null -eq $result.ok) { $problems += 'ok 필드 없음' }
if ($null -eq $result.status) { $problems += 'status 필드 없음' }
else {
    foreach ($f in 'on', 'running', 'managed', 'found', 'memoryMB', 'modules') {
        if ($null -eq $result.status.$f) { $problems += "status.$f 필드 없음" }
    }
}
if ($null -eq $result.sites) { $problems += 'sites 필드 없음' }
if ($p.ExitCode -ne 0) { $problems += "호스트 종료 코드 $($p.ExitCode)" }

if ($problems.Count) {
    $problems | ForEach-Object { Write-Host "  실패: $_" -ForegroundColor Red }
    exit 1
}
$s = $result.status
Write-Host ("통과: 은행 모드 {0} · 관리 {1}개 중 {2}개 실행 · 메모리 {3} MB · 사이트 {4}개" -f `
    $(if ($s.on) { '켜짐' } else { '꺼짐' }), $s.managed, $s.running, $s.memoryMB, @($result.sites).Count) -ForegroundColor Green
