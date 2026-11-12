; 틈(Teum) 배포용 Inno Setup 스크립트 — package.ps1과 같은 파일 묶음을 setup.exe로 만든다.
; 사용법: Inno Setup 6 설치 후  ISCC.exe Teum-Setup.iss  →  Output\Teum-Setup-v<버전>.exe 생성
; 정책:
;   - 사용자 폴더 설치(%LOCALAPPDATA%\Programs\틈)라 관리자 권리가 필요 없다.
;   - 앱 자체는 이미 HKCU Run 키(DeskHooker.Teum)로 로그인 자동실행을 관리하므로
;     인스톨러가 별도의 Run 항목을 만들지 않는다(충돌 방지).
;   - 사용자 설정(settings.json·user-themes.json·knox-token.txt·WebView2 프로필)은
;     %LOCALAPPDATA%\DeskHooker에 있으므로 언인스톨해도 지우지 않는다.
;   - 단, "Install하면 preset 적용"(312): 설치 시 settings.json은 지운다. 앱이 다음 실행에서
;     ReminderSettings.DefaultPreset()(기본 프리셋)으로 다시 시작한다. 사용자가 따로 만든
;     쉼 카드(user-themes.json)와 각 카드의 영상 목록 편집(WebView2 localStorage)은 남는다.
;   - 업그레이드 설치: 같은 "Teum.AppId" GUID로 기존 폴더 위에 덮어쓴다.
;   - 재설치 흐름(318): 앱이 트레이 전용이라 Restart Manager가 못 닫는다. [Code]
;     PrepareToInstall이 실행 중인 DeskHooker.exe를 강제 종료하고, 기존 버전의
;     uninstaller를 조용히 먼저 실행한 뒤 새 파일을 설치한다.

#define TeumName "틈"
; AppId는 [Code]의 기존 버전 제거 로직과 같은 값을 공유한다.
#define TeumAppId "7B3A5C0E-9D4F-4E2B-9C1A-TEUM0001"
; exe에 버전 리소스가 없으므로(GetVersionNumbersString→0.0.0.0) 버전은 배포 시 여기서 지정.
#define TeumVersion "1.3.0"
#define TeumPublisher "Teum"
#define TeumExeName "DeskHooker.exe"

[Setup]
AppId={{{#TeumAppId}}}
AppName={#TeumName}
AppVersion={#TeumVersion}
AppPublisher={#TeumPublisher}
DefaultDirName={localappdata}\Programs\{#TeumName}
DefaultGroupName={#TeumName}
DisableProgramGroupPage=yes
; 관리자 권한 불필요(사용자 폴더 설치)
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=Teum-Setup-v{#TeumVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 앱은 트레이 전용(최상위 창 없음)이라 Restart Manager(CloseApplications)가 종료하지
; 못하고 "자동으로 닫지 못했습니다" 대화상자가 뜬다. 대신 [Code] PrepareToInstall에서
; 프로세스를 직접 종료하므로 Restart Manager는 끈다.
CloseApplications=no
RestartApplications=no
; 한국어 위저드
ShowLanguageDialog=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[CustomMessages]
korean.SetupClosing=설치를 계속하려면 실행 중인 틈을 먼저 종료해 주세요. 작업 표시줄 트레이의 틈 아이콘에서 종료할 수 있습니다.
korean.LaunchProgram={#TeumName} 실행

[Files]
; package.ps1의 런타임 목록과 같은 구성. 소스 경로는 이 스크립트가 있는 폴더 기준.
Source: "DeskHooker.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Microsoft.Web.WebView2.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "Microsoft.Web.WebView2.WinForms.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "WebView2Loader.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "app.html"; DestDir: "{app}"; Flags: ignoreversion
Source: "app.js"; DestDir: "{app}"; Flags: ignoreversion
Source: "app.css"; DestDir: "{app}"; Flags: ignoreversion
Source: "app-core.js"; DestDir: "{app}"; Flags: ignoreversion
Source: "theme-variants.js"; DestDir: "{app}"; Flags: ignoreversion
Source: "games-content.js"; DestDir: "{app}"; Flags: ignoreversion
Source: "themes-data.js"; DestDir: "{app}"; Flags: ignoreversion
Source: "themes.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "manifest.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "desk-stretch-motion.gif"; DestDir: "{app}"; Flags: ignoreversion
Source: "desk-stretch-guide.png"; DestDir: "{app}"; Flags: ignoreversion
Source: "사용안내.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "themes\*"; DestDir: "{app}\themes"; Flags: ignoreversion recursesubdirs
Source: "assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs

[InstallDelete]
; "Install하면 preset 적용"(312): 설치(신규·업그레이드 모두) 시 기존 설정을 지워
; 앱이 다음 실행에서 기본 프리셋(09:00-18:00 평일 · 60분 · 점심 12-13시 · 카드 5개 무작위 ·
; 퇴근버스 17:00 · Knox 캘린더 5분 전)으로 시작하게 한다.
; 앱이 실행 중이면 CloseApplications=yes가 먼저 종료시키므로, 앱이 종료하며 설정을 다시 써도
; 이 삭제가 그 뒤에 실행돼 프리셋이 이긴다.
Type: files; Name: "{localappdata}\DeskHooker\settings.json"

[Icons]
Name: "{group}\{#TeumName}"; Filename: "{app}\{#TeumExeName}"
Name: "{group}\{#TeumName} 사용안내"; Filename: "{app}\사용안내.txt"
Name: "{autodesktop}\{#TeumName}"; Filename: "{app}\{#TeumExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 바로 가기 만들기"; GroupDescription: "추가 아이콘:"

[Run]
Filename: "{app}\{#TeumExeName}"; Description: "{cm:LaunchProgram}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 프로그램 파일만 제거. %LOCALAPPDATA%\DeskHooker의 사용자 설정은 남긴다.
Type: filesandordirs; Name: "{app}\EBWebView"

[Code]
// 트레이 상주 앱이라 Restart Manager가 닫을 최상위 창이 없어 "자동으로 닫지 못했습니다"
// 대화상자가 떴다(318). 대신 설치(Install 클릭 직후) 단계에서:
//   1) DeskHooker.exe를 프로세스 트리째 강제 종료한다(/T로 WebView2 자식까지).
//   2) 기존 버전의 uninstaller를 조용히(/VERYSILENT) 실행해 먼저 제거한다.
//   3) 그 위에 새 버전 파일을 깐다.
// 사용자 설정은 %LOCALAPPDATA%\DeskHooker에 있으므로 uninstaller가 지우지 않고,
// 프리셋 초기화(settings.json 삭제)는 [InstallDelete]가 계속 담당한다.
procedure KillDeskHooker();
var
  resultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM "DeskHooker.exe" /T /F', '', SW_HIDE, ewWaitUntilTerminated, resultCode);
end;

function PreviousUninstallCommand(): String;
var
  key: String;
  value: String;
begin
  // Inno가 AppId "{{...}}"를 파싱한 결과 키 이름은 "{...}}"_is1처럼 닫는 중괄호가 두 개다.
  // 실제 등록된 키(reg.exe로 확인)와 문자를 맞춘다.
  key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + '{#TeumAppId}' + '}}_is1';
  Result := '';
  if RegQueryStringValue(HKCU, key, 'UninstallString', value) then Result := value
  else if RegQueryStringValue(HKLM, key, 'UninstallString', value) then Result := value;
  // 값은 보통 "C:\...\unins000.exe"처럼 따옴표로 감싸져 있다.
  StringChangeEx(Result, '"', '', True);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  uninstaller: String;
  resultCode: Integer;
begin
  Result := '';
  KillDeskHooker();
  // 강제 종료된 WebView2 자식 프로세스가 {app}\EBWebView 파일 잠금을 풀 시간을 준다.
  Sleep(1500);
  uninstaller := PreviousUninstallCommand();
  if uninstaller <> '' then
    Exec(uninstaller, '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES', '', SW_HIDE, ewWaitUntilTerminated, resultCode);
  // 제거 도중 자동실행 등으로 앱이 다시 떴을 가능성을 막는다.
  KillDeskHooker();
  Sleep(500);
end;
