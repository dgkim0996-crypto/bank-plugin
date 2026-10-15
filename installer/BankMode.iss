; 은행 모드 설치 프로그램 (Inno Setup 6)
; 빌드: ISCC.exe installer\BankMode.iss  (먼저 build.ps1로 bin\BankMode.exe가 있어야 함)
; 결과: dist\BankMode-Setup.exe
;
; 관리자 권한 없이 설치되는 '사용자 설치'입니다.
;  - 설치: UAC 없이 내 계정에만 설치 (크롬 Native Messaging·설정이 모두 사용자 단위라 이 방식이 맞음)
;  - 제거: BankMode.exe --uninstall 을 먼저 실행해, 은행 모드가 바꿔 둔 자동 시작 설정을 원래대로 되돌림
;          (되돌릴 서비스가 있으면 이때 관리자 확인 창이 한 번 뜸)

#define AppName "은행 모드"
#define AppVersion "1.0.0"
#define AppPublisher "BankMode"
#define ExeName "BankMode.exe"

[Setup]
AppId={{B9A6F3C2-8E4D-4A17-9C5B-7D2E1F0A3B64}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\BankMode
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#ExeName}
; 관리자 권한 없이 내 계정에만 설치
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; 설치 중 은행 모드가 켜져 있으면 닫도록 안내 (앱의 단일 실행 뮤텍스와 같은 이름)
AppMutex=Local\BankMode.Main
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=6.1sp1
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir=..\dist
OutputBaseFilename=BankMode-Setup
SetupIconFile=..\src\app.ico

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 '은행 모드 켜기' 바로가기 만들기"; GroupDescription: "바로가기:"

[Files]
Source: "..\bin\{#ExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "..\chrome-extension\*"; DestDir: "{app}\chrome-extension"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; 시작 메뉴: 창 열기
Name: "{group}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{group}\{#AppName} 제거"; Filename: "{uninstallexe}"
; 바탕화면: 더블클릭하면 바로 켜지고 은행 사이트가 열림
Name: "{autodesktop}\은행 모드 켜기"; Filename: "{app}\{#ExeName}"; Parameters: "--on"; Tasks: desktopicon

[Run]
Description: "{#AppName} 지금 실행"; Filename: "{app}\{#ExeName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; 파일을 지우기 전에 먼저 실행 → 바꿔 둔 자동 시작 설정을 원래대로 되돌리고 크롬 연결 해제
Filename: "{app}\{#ExeName}"; Parameters: "--uninstall"; Flags: waituntilterminated runhidden; RunOnceId: "RestoreBankSecurity"

[Code]
function InitializeUninstall(): Boolean;
begin
  MsgBox('은행 모드를 제거합니다.' + #13#10#13#10 +
         '보안 프로그램이 다음 부팅부터 예전처럼 저절로 켜지도록, 은행 모드가 바꿔 둔 자동 시작 설정을 먼저 원래대로 되돌립니다.' + #13#10 +
         '이때 관리자 확인 창이 한 번 뜰 수 있어요 ('#39'예'#39' 선택).',
         mbInformation, MB_OK);
  Result := True;
end;
