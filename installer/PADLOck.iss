; Установщик PADLOck · vvedyaev
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
; Для свойств exe нужна версия только из цифр: 1.2.0-dev.3 → 1.2.0
#ifndef NumVersion
  #define NumVersion AppVersion
#endif

[Setup]
AppId={{B3D1F0A2-6C4E-4F7B-8E29-1A5C7D9E3F60}
AppName=PADLOck
AppVersion={#AppVersion}
AppVerName=PADLOck {#AppVersion}
AppPublisher=vvedyaev
AppCopyright=© 2026 vvedyaev
VersionInfoVersion={#NumVersion}
VersionInfoCompany=vvedyaev
VersionInfoCopyright=© 2026 vvedyaev
VersionInfoDescription=PADLOck — установка
DefaultDirName={autopf}\PADLOck
DefaultGroupName=PADLOck
DisableProgramGroupPage=yes
; Установка в Program Files: файлы программы защищены от изменения обычными пользователями
PrivilegesRequired=admin
UsePreviousAppDir=no
OutputDir=..\dist
OutputBaseFilename=PADLOck-Setup-{#AppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\PADLOck.exe
UninstallDisplayName=PADLOck
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

; APK раньше входил в установку, теперь он приходит с релизом — старые файлы убираем
[InstallDelete]
Type: files; Name: "{app}\kiosk\*.apk"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PADLOck"; Filename: "{app}\PADLOck.exe"
Name: "{group}\Инструкция PADLOck"; Filename: "{app}\Инструкция.txt"
Name: "{group}\Удалить PADLOck"; Filename: "{uninstallexe}"
Name: "{autodesktop}\PADLOck"; Filename: "{app}\PADLOck.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PADLOck.exe"; Description: "{cm:LaunchProgram,PADLOck}"; Flags: nowait postinstall skipifsilent
; Обновление из программы идёт с /SILENT — после него программа запускается сама, от имени пользователя, а не администратора
Filename: "{app}\PADLOck.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent

[UninstallRun]
Filename: "{app}\tools\adb.exe"; Parameters: "kill-server"; Flags: runhidden skipifdoesntexist; RunOnceId: "KillAdb"

[Code]
// Запущенный adb держит файлы программы — при обновлении его нужно остановить
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  if FileExists(ExpandConstant('{app}\tools\adb.exe')) then
    Exec(ExpandConstant('{app}\tools\adb.exe'), 'kill-server', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;
