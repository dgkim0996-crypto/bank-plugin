# 은행 모드 (BankMode)

은행·증권·카드·공공 사이트용 보안 프로그램(AhnLab Safe Transaction, nProtect, TouchEn nxKey, CrossEX,
Veraport, Delfino, AnySign, MagicLine, SignKorea, IPinside, MarkAny 등)을
**평소엔 꺼 두고, 은행 업무할 때만 버튼 한 번으로 전부 켜는** 윈도우 앱입니다.

- 실행 파일 하나(약 90 KB), 추가 설치 없음 — 윈도우에 기본 포함된 .NET Framework 4.8로 동작
- 창을 닫으면 앱도 완전히 종료돼서 평소 자원을 쓰지 않음
- 크롬 툴바 버튼(확장 프로그램)으로도 상태 확인·켜기·끄기 가능 — 은행 사이트에 들어가면 꺼져 있다고 알려 줌

## 다운로드 · 설치

1. **[BankMode-Setup.exe 다운로드](https://github.com/dgkim0996-crypto/bank-plugin/releases/latest/download/BankMode-Setup.exe)** → 실행
   - "Windows의 PC 보호" 창이 뜨면 **추가 정보 → 실행**을 누르세요 (코드 서명 인증서가 아직 없어서 뜨는 안내예요)
   - 관리자 권한 없이 내 계정에만 설치되고, **크롬 확장 연결까지 자동으로** 끝나요
2. 크롬 웹스토어에서 **[은행 모드 확장 프로그램](https://chromewebstore.google.com/detail/finlhbijjakfdccdbibdccfkljkgbjck)** 설치
3. 툴바의 퍼즐 조각 아이콘에서 '은행 모드' 옆 핀을 눌러 버튼을 고정

> 지난 버전은 [Releases](https://github.com/dgkim0996-crypto/bank-plugin/releases)에 있어요.

## 사용법

1. 시작 메뉴의 **은행 모드** 실행 (직접 빌드했다면 `bin\BankMode.exe`)
2. **처음 한 번 `끄기`** → 부팅할 때 보안 프로그램이 저절로 켜지지 않게 막아 둡니다 (관리자 확인 창에서 '예')
3. 은행 갈 때 **`은행 모드 켜기`** → 보안 프로그램이 전부 켜지고, 고른 은행 사이트가 열립니다
4. 은행 업무가 끝나면 창 닫기 → "끄고 닫을까요?" → **예**

> `바탕화면 바로가기 만들기`를 누르면 더블클릭 한 번으로 3번까지 진행되는 바로가기가 생깁니다.

## "플러그인 설치 → 재부팅 → 재로그인" 반복을 끝내려면

보안 프로그램은 **설치만 돼 있으면** 은행 모드로 켜고 끌 수 있습니다. 문제는 로그인한 다음에야
빠진 프로그램을 알려 주는 사이트들이라서, 처음 한 번만 아래 순서를 따르세요.

1. 자주 쓰는 은행 사이트에서 **로그인하기 전에** '보안프로그램 설치'(통합설치) 페이지로 가서 빠진 것을 모두 설치
2. 재부팅은 **딱 한 번**만
3. 그 뒤부터는 `은행 모드 켜기` → 로그인 한 번이면 끝

새로 설치된 보안 프로그램은 다음에 앱을 열 때 자동으로 목록에 잡힙니다.

## 무엇을 바꾸나요

| 동작 | 켜기 | 끄기 |
|---|---|---|
| 서비스 (예: CrossEX Live Checker) | '사용 안 함'을 풀고 시작 | 중지 + 시작 유형 '사용 안 함'으로 |
| 시작프로그램 (예: veraport, delfino) | 실행 | 작업 관리자 '시작 앱 → 사용 안 함'과 같은 방식으로 끔 |
| 실행 중인 프로세스 | — | 종료 |

- 서비스를 **'사용 안 함'** 으로 바꾸므로 부팅 때는 물론 **은행 사이트가 불러도 켜지지 않습니다**.
  켜기를 누르면 다시 '수동'으로 풀고 시작하므로 은행 업무엔 지장이 없어요
- **커널 드라이버는 건드리지 않습니다** (원래 대부분 '수동'이라 필요할 때만 올라옴)
- 관리자 권한이 필요한 작업은 변경할 항목을 먼저 보여 줍니다. 동의해야 UAC 확인 창이 이어서 뜨고, 취소하면 작업을 시작하지 않습니다
- 일부 모듈이 자체 보호로 꺼지지 않으면 `남은 항목 다시 끄기`로 아직 실행 중인 항목만 다시 시도할 수 있습니다
- 처음 상태와 BankMode가 마지막으로 적용한 값은 `%APPDATA%\BankMode\state.json`에 원자적으로 기록됩니다. 변경 직전 저널도 저장하므로 작업 중 앱이 종료돼도 다음 작업에서 상태를 확인해 이어서 복구합니다. **`원래대로 되돌리기`** 는 그 뒤 사용자가 값을 바꾸지 않은 항목만 복원하고, 달라진 항목은 현재 설정을 보존하며 기록 창에 알려 줍니다
- 서비스 제어가 필요할 때만 관리자 권한을 요청합니다. 이미 다 켜져 있으면 확인 창도 뜨지 않아요

### 안 꺼질 수 있는 것

AhnLab Safe Transaction, nProtect 같은 키보드 보안 프로그램은 **자체 보호 기능**으로 강제 종료를 막는 경우가 있습니다.
그러면 기록 창에 `!` 표시로 알려 줍니다. 서비스 자동 시작 설정이나 시작프로그램 끄기까지 막거나 곧바로 되돌리는
프로그램도 있어서, 끈 다음 실제로 꺼졌는지 다시 확인해 안 꺼졌으면 실패로 알려 줍니다.
이렇게 끝내 안 꺼지는 모듈은 목록에서 체크를 해제하세요. 은행 모드가 건드리지 않고, 켜짐/꺼짐 표시에서도 빠집니다.

## 지원하는 보안 프로그램

여러 은행·증권사·카드사·공공기관이 요구하는 주요 모듈을 기본으로 인식합니다. **설치돼 있는 것만** 목록에
나타나므로, 해당 모듈이 없는 PC에서는 그냥 보이지 않습니다.

- **키보드 보안·안티해킹**: AhnLab Safe Transaction(ASTx)·구버전 AOS, nProtect Online Security·KeyCrypt·Netizen(잉카),
  TouchEn nxKey·nxWeb(라온), Kings Online Security(킹스), SoftCamp 보안
- **공동인증서·전자서명**: INISAFE CrossWeb EX(이니텍), Veraport·Delfino(위즈베라), AnySign·XecureWeb(한컴위드),
  MagicLine(드림시큐리티), SignKorea(코스콤), KSign(케이사인), UniSign/CrossCert(한국전자인증),
  SecuKit(한국정보인증), KeySharp CertRelay(라온), VestCert(예티소프트)
- **단말정보·문서 보안**: IPinside LWS(인터리젠), MarkAny ePageSAFER/WebDRM

> **안전장치 — 백신·게임은 건드리지 않습니다.** 은행 보안 프로그램 중에는 백신(안랩 V3, 잉카 TACHYON)이나
> 게임 보안(게임가드)과 실행 파일 이름이 똑같은 것들이 있습니다. 그래서 이 앱은 파일 이름만 보지 않고
> **은행 전용 설치 폴더 경로**(예: `AhnLab\Safe Transaction`)까지 맞을 때만 그 모듈로 인식합니다.
> 백신·게임 보안·일반 프로그램은 꺼지지 않습니다.

## 설정 (`설정 파일 열기`)

`%APPDATA%\BankMode\settings.json`

앱의 **`사이트 관리·분류 미리보기`** 화면에서 사이트를 추가·수정·삭제하고, 현재 서비스·시작프로그램·프로세스 경로가 어떤 분류 규칙과 일치하는지 미리 볼 수 있습니다.

- `Sites` — 켠 다음 열 사이트 목록 (증권사 등 추가 가능)
- `Vendors` — 보안 프로그램 분류 규칙. `Match`에 적힌 문자열이 실행 파일 경로에 있으면 그 모듈로 인식
  (예: `"AhnLab/Safe Transaction"` → `C:\Program Files\AhnLab\Safe Transaction\...`).
  백신 등과 겹치지 않도록 되도록 **폴더 경로**로 적으세요. 와일드카드·상위 폴더 이동·너무 짧은 단일 이름은 무시되며, 무시된 패턴은 앱 기록에 표시됩니다. 관리자 확인 창에는 변경 대상의 실행 경로도 나옵니다
- `Excluded` — 체크 해제한(관리하지 않을) 모듈

고친 뒤 저장하고 `새로고침`을 누르면 반영됩니다.

**`진단 정보 저장`** 은 동의를 받은 뒤 현재 탐지 상태와 최근 로그를 텍스트로 내보냅니다. 사용자 프로필 경로와 URL·이메일은 가리지만, 공유 전 파일 내용을 직접 확인하세요.

앱은 새 버전 안내를 위해 GitHub 공개 Releases API를 하루 최대 한 번 확인합니다. 이 요청에는 PC의 설정이나 은행 보안 프로그램 상태를 보내지 않습니다.

## 크롬 툴바 버튼 (확장 프로그램)

크롬 확장은 보안상 윈도우 서비스나 다른 프로그램을 직접 켜고 끌 수 없어서,
확장 버튼이 이 앱을 불러 일을 시키는 방식(크롬 Native Messaging)입니다.

### 설치

**일반 사용자**: 위 '다운로드 · 설치'대로 설치 프로그램 + 웹스토어 확장을 깔면 끝이에요. 연결은 설치 프로그램이 자동으로 해요.

**개발자용 (압축해제 로드)**:

1. 크롬 주소창에 `chrome://extensions` 입력
2. 오른쪽 위 **개발자 모드** 켜기
3. **압축해제된 확장 프로그램 로드** → `BankMode\chrome-extension` 폴더 선택
4. 툴바의 퍼즐 조각 아이콘에서 '은행 모드' 옆 핀을 눌러 버튼을 고정
5. 은행 모드 앱을 열고 아래쪽 **`크롬 확장 연결`** 누르기 (관리자 권한 필요 없음)

> 5번은 크롬이 앱을 찾을 수 있게 `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.bankmode.host`에
> 앱 위치를 적는 단계입니다(설정 파일은 `%APPDATA%\BankMode\com.bankmode.host.json`). 앱 폴더를 옮겼다면 다시 누르세요.
> 이미 연결된 상태에서 누르면 연결을 끊을지 묻습니다.
> 확장 ID는 `manifest.json`의 고정 키 덕분에 어느 PC에서 불러와도 `pmjnagnjdiidgplgfeckafeamdiflkbe`로 같습니다.

### 쓰는 법

- 버튼을 누르면 팝업에 **켜짐/꺼짐, 실행 중인 개수, 메모리**와 `은행 모드 켜기` / `끄기` 버튼이 나옵니다
- 은행 사이트에서 열면 "은행 모드가 꺼져 있어요 — 켤까요?"가 함께 나옵니다
- 관리자 확인 창이 뜨면 팝업이 닫히지만 작업은 계속됩니다. 끝나면 버튼 배지가 바뀝니다
- 배지 뜻: **ON**(초록) 모두 켜짐 · **~**(주황) 일부만 켜짐 · **!**(주황) 은행 사이트인데 꺼져 있음 · **?**(회색) 앱과 연결 안 됨 · **…**(파랑) 켜고 끄는 중
- 은행 사이트는 설정 파일의 `Sites` 목록을 따릅니다. `www.kbstar.com`을 등록하면 `obank.kbstar.com` 같은 하위 주소도 포함됩니다

확장은 평소에 앱을 주기적으로 부르지 않습니다. **팝업을 열 때와 은행 사이트에 들어갈 때만** 잠깐 앱을 실행해
상태를 확인합니다. 탭 주소는 은행 사이트인지 비교하는 데만 쓰고, 앱이나 다른 곳으로 보내지 않습니다.

### 권한 (좁게 설계)

"방문 기록 읽기" 경고가 뜨는 `tabs` 권한은 **쓰지 않습니다**. 대신 꼭 필요한 것만 요청해요.

- `nativeMessaging` — 은행 모드 앱과 통신
- `storage` — 상태·사이트 목록 보관
- `activeTab` — 팝업을 열 때 **지금 보고 있는 탭**만 확인 (은행 사이트인지 판단·새로고침)
- 기본 사이트 권한은 `chrome-extension\sites.json`에서 만들어집니다. 추가 사이트는 팝업의 **사이트별 자동 배지 권한** 목록에서 도메인별로 허용하거나 해제할 수 있습니다.
  `tabs` 권한은 쓰지 않고, 허용된 등록 사이트에서만 자동 배지를 띄웁니다
  사이트 목록에서 삭제한 추가 권한은 팝업에 남아 해제할 수 있습니다. 기본 권한은 확장 업데이트 전까지 유지된다고 표시됩니다.

> 목록에 없는 은행/증권 사이트는 `%APPDATA%\BankMode\settings.json`의 `Sites`에 추가한 뒤, 확장 팝업의 사이트별 권한 목록에서 그 도메인만 허용하세요.
> 허용하면 해당 사이트에서 자동 배지를 볼 수 있습니다. 팝업 버튼으로 켜고 끄는 기능은 사이트 권한 없이도 어디서나 됩니다.

## 빌드

`build.bat` 더블클릭 (또는 `build.ps1`). 결과물은 `bin\BankMode.exe`.

```
src\Engine.cs        탐색(서비스·시작프로그램·프로세스) + 켜기/끄기/되돌리기 (앱 창·크롬 확장 공용)
src\MainForm.cs      화면
src\SettingsAssistantDialog.cs 사이트 관리 및 분류 미리보기
src\DiagnosticsReport.cs 동의 기반 진단 정보 내보내기
src\UpdateChecker.cs  하루 한 번 새 버전 확인
src\NativeHost.cs    크롬 확장용 Native Messaging 호스트(--native-host) + 크롬에 호스트 등록
src\Program.cs       진입점, 관리자 권한 단계(--elevated), 제거 복원(--uninstall), 진단(--scan)
chrome-extension\    크롬 확장 (Manifest V3). 아이콘은 없으면 build.ps1이 만들어 줌
chrome-extension\sites.json  앱과 확장이 함께 읽는 기본 사이트 목록
installer\           Inno Setup 설치 프로그램 스크립트(BankMode.iss)
build-dist.ps1       배포 묶음(설치 프로그램 + 웹스토어용 확장 zip) 만들기
test-host.ps1        크롬처럼 '4바이트 길이 + JSON'을 보내 호스트 응답 확인
```

`BankMode.exe --scan 결과.txt` 는 아무것도 바꾸지 않고 인식된 모듈 목록만 저장합니다 (문제 확인용).

`test-host.ps1` 은 크롬 없이 호스트에 status를 보내 응답 JSON을 확인합니다 (아무것도 바꾸지 않음).
`-ViaCmd`를 붙이면 크롬처럼 `cmd.exe`를 거쳐 `chrome-extension://…/` 인자로 실행합니다.

```
powershell -ExecutionPolicy Bypass -File test-host.ps1
```

## 배포하기

`build-dist.ps1` 하나로 배포 묶음을 만듭니다. 결과물은 `dist\` 에 모입니다.

```
powershell -ExecutionPolicy Bypass -File build-dist.ps1
```

1. 앱 빌드 (`bin\BankMode.exe`)
2. 크롬 웹스토어 업로드용 확장 zip (`dist\bankmode-extension-store-v1.0.2.zip`) — 개발용 key를 뺀 버전, 사이트 권한 목록은 `sites.json`에서 생성
3. **Inno Setup**이 깔려 있으면 확장 버전과 맞춘 설치 프로그램 `dist\BankMode-Setup.exe` 까지 컴파일
   - 없으면: `winget install JRSoftware.InnoSetup` 또는 <https://jrsoftware.org/isdl.php> 로 설치 후 다시 실행

### 설치 프로그램 (`BankMode-Setup.exe`)

- **관리자 권한 없이** 내 계정에만 설치됩니다 (크롬 연결·설정이 사용자 단위라 이 방식이 맞음)
- 설치하면서 **크롬 확장 연결을 자동 등록**합니다 (`--register-host`). 개발용·웹스토어 확장 ID를 둘 다 허용해요
- 제거하면 **은행 모드가 바꿔 둔 자동 시작 설정을 자동으로 원래대로 되돌립니다** (`--uninstall`).
  되돌릴 서비스가 있으면 이때 관리자 확인 창이 한 번 떠요. 그래서 제거 뒤에도 은행 사이트가 정상 동작합니다
- 설정·상태 파일(`%APPDATA%\BankMode`)은 제거해도 남겨 둡니다 (직접 지워도 됨)

> **서명 안내:** 지금 exe는 코드 서명이 안 돼 있어, 인터넷에서 받아 처음 실행하면
> "Windows의 PC 보호(SmartScreen)" 창이 떠요. **추가 정보 → 실행**을 눌러야 설치됩니다.
> 일반 공개하려면 코드 서명 인증서가 필요합니다(아래 '남은 일').

## 크롬 웹스토어 등록

일반 사용자는 웹스토어 밖의 확장을 설치하기 어렵기 때문에, 널리 쓰려면 웹스토어에 올려야 합니다.

1. [크롬 개발자 대시보드](https://chrome.google.com/webstore/devconsole)에 가입 (최초 등록비 $5)
2. `dist\bankmode-extension-store-v1.0.2.zip` 업로드 — 공개 범위는 전체 공개 또는 **'미등록(Unlisted)'**(링크 아는 사람만) 선택 가능
3. 심사 통과 후, 대시보드에서 **배정된 확장 ID**를 복사
4. 그 ID를 앱이 허용하도록 등록. 둘 중 하나:
   - 다시 빌드하지 않기: `%APPDATA%\BankMode\extension-ids.txt` 에 ID를 한 줄 적고, 앱에서 `크롬 확장 연결`을 다시 누름
   - 빌드에 넣기: `src\NativeHost.cs` 의 `PublishedExtensionId` 에 ID를 넣고 다시 빌드
5. 개발 중(압축 해제 로드)에도 같은 ID를 쓰고 싶으면, 대시보드의 **공개 키**를 `chrome-extension\manifest.json` 의 `key` 에 붙여넣기

### 개인정보처리방침

웹스토어는 **개인정보처리방침 URL**을 요구합니다. 초안이 [docs/privacy-policy.html](docs/privacy-policy.html)에 있어요
(이 확장은 데이터를 수집·전송하지 않는다는 내용). 올리기 전에:

1. 현재 파일의 연락처와 정책 내용을 검토
2. 어디든 공개 웹에 호스팅 — 예: GitHub Pages(저장소의 `docs/` 폴더를 Pages로 켜면 `https://<아이디>.github.io/<저장소>/privacy-policy.html`), Netlify, 혹은 가진 웹사이트
3. 공개 URL을 개발자 대시보드의 '개인정보 보호' 항목에 입력

> 앱은 **여러 확장 ID를 동시에 허용**합니다(개발용 + 공개용). 그래서 공개 전후로 확장이 계속 동작해요.
> `nativeMessaging` 권한 때문에 심사가 길어질 수 있으니, 대시보드의 권한 사유 칸에 "같은 PC의 은행 모드 앱과 통신" 이라고 적어 두세요.

## 남은 일 (일반 공개 전)

- **코드 서명 인증서**: SmartScreen·백신 경고를 줄이려면 필요(연 비용, 하드웨어 토큰 보관)
- **백신 오탐 신고**: '보안 프로그램을 끄고 프로세스를 종료'하는 동작이 악성코드와 비슷해 오탐될 수 있음 → V3·알약 등에 오탐 신고
- ~~**권한 축소**~~ (완료): `tabs` 권한을 쓰지 않고 기본 사이트만 설치 권한으로 선언. 추가 사이트는 팝업에서 해당 도메인 접근을 따로 허용
- ~~**개인정보처리방침** 페이지~~ (초안 완료: [docs/privacy-policy.html](docs/privacy-policy.html)) — 연락처 이메일 채우고 공개 URL로 호스팅만 하면 됨

### Windows 릴리스 자동화와 코드 서명

`v*` 태그를 올리면 GitHub Actions가 manifest 버전을 확인하고 Windows에서 설치 파일과 웹스토어 ZIP을 빌드해 GitHub Release에 첨부합니다. 코드 서명 인증서를 설정하지 않으면 설치 파일은 서명되지 않은 상태로 표시됩니다. 서명용 GitHub Actions secret 설정은 [릴리스·서명 안내](docs/release-and-signing.md)를 참고하세요. 이 자동화는 Chrome 웹스토어에 확장을 게시하지 않습니다.
