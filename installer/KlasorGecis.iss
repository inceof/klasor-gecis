#define AppName "Klasör Geçiş"
#define AppVersion "0.5.0"
#define AppFileName "KlasorGecis.exe"
#define RootDir AddBackslash(SourcePath) + ".."
#define OutputFolder RootDir + "\release\setup"

[Setup]
AppId=KlasorGecis.FolderQuickSwitch
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Klasör Geçiş
DefaultDirName={localappdata}\Programs\KlasorGecis
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
UsePreviousPrivileges=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile={#RootDir}\src\KlasorGecis\app.ico
UninstallDisplayIcon={app}\{#AppFileName}
OutputDir={#OutputFolder}
OutputBaseFilename=KlasorGecis_Setup_{#AppVersion}_x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=yes
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=0.5.0.0
VersionInfoDescription=Klasör Geçiş Windows x64 Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "hindi"; MessagesFile: "Languages\Hindi.islu"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[CustomMessages]
turkish.DesktopShortcut=Masaüstü kısayolu oluştur
english.DesktopShortcut=Create a desktop shortcut
chinesesimplified.DesktopShortcut=创建桌面快捷方式
hindi.DesktopShortcut=डेस्कटॉप शॉर्टकट बनाएँ
spanish.DesktopShortcut=Crear un acceso directo en el escritorio
arabic.DesktopShortcut=إنشاء اختصار على سطح المكتب
turkish.ExtraOptions=Ek seçenekler:
english.ExtraOptions=Additional options:
chinesesimplified.ExtraOptions=其他选项：
hindi.ExtraOptions=अतिरिक्त विकल्प:
spanish.ExtraOptions=Opciones adicionales:
arabic.ExtraOptions=خيارات إضافية:
turkish.StartWithWindows=Windows ile başlat (mevcut kullanıcı)
english.StartWithWindows=Start with Windows (current user)
chinesesimplified.StartWithWindows=随 Windows 启动（当前用户）
hindi.StartWithWindows=Windows के साथ शुरू करें (वर्तमान उपयोगकर्ता)
spanish.StartWithWindows=Iniciar con Windows (usuario actual)
arabic.StartWithWindows=التشغيل مع Windows (المستخدم الحالي)
turkish.LaunchApp=%1 uygulamasını başlat
english.LaunchApp=Launch %1
chinesesimplified.LaunchApp=启动 %1
hindi.LaunchApp=%1 शुरू करें
spanish.LaunchApp=Iniciar %1
arabic.LaunchApp=تشغيل %1

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:ExtraOptions}"; Flags: unchecked
Name: "autostart"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:ExtraOptions}"; Flags: unchecked

[Files]
Source: "{#RootDir}\release\win-x64\KlasorGecis.exe"; DestDir: "{app}"; Flags: ignoreversion

Source: "{#RootDir}\release\win-x64\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RootDir}\release\win-x64\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RootDir}\release\win-x64\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppFileName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppFileName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "KlasorGecis"; ValueData: """{app}\{#AppFileName}"""; Tasks: autostart

[Run]
Filename: "{app}\{#AppFileName}"; Description: "{cm:LaunchApp,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AutoRunValue, InstalledCommand: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    InstalledCommand := '"' + ExpandConstant('{app}\KlasorGecis.exe') + '"';
    if RegQueryStringValue(HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      'KlasorGecis', AutoRunValue) then
    begin
      // Remove ONLY the startup entry associated with this installation.
      // Preserve a user's independent portable copy or another installation.
      if CompareText(Trim(AutoRunValue), InstalledCommand) = 0 then
        RegDeleteValue(HKCU,
          'Software\Microsoft\Windows\CurrentVersion\Run', 'KlasorGecis');
    end;
  end;
end;
