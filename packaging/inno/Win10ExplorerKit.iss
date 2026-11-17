#ifndef AppVersion
  #error Supply AppVersion through scripts/Package.ps1
#endif
#ifndef AppCompany
  #error Supply AppCompany through scripts/Package.ps1
#endif
#ifndef AppBuildVersion
  #error Supply AppBuildVersion through scripts/Package.ps1
#endif
#ifndef SourceDir
  #error Supply SourceDir through scripts/Package.ps1
#endif
#ifndef ReleaseDir
  #error Supply ReleaseDir through scripts/Package.ps1
#endif

[Setup]
AppId={{57D60808-429A-4ABC-9303-38E819A0635A}
AppName=Win10 Explorer Kit
AppVersion={#AppBuildVersion}
AppPublisher={#AppCompany}
DefaultDirName={localappdata}\Programs\Win10ExplorerKit
DisableDirPage=auto
UsePreviousAppDir=yes
ShowLanguageDialog=auto
UsePreviousLanguage=yes
DefaultGroupName=Win10 Explorer Kit
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x86os or x64os
MinVersion=10.0
WizardStyle=modern
LicenseFile=..\..\LICENSE
SetupIconFile=..\..\src\Win10ExplorerKit\Resources\app.ico
UninstallDisplayIcon={app}\Win10ExplorerKit.exe
OutputDir={#ReleaseDir}
OutputBaseFilename=Win10-Explorer-Kit-v{#AppVersion}-Windows-Setup
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppBuildVersion}
VersionInfoProductVersion={#AppBuildVersion}
VersionInfoCompany={#AppCompany}
VersionInfoProductName=Win10 Explorer Kit
VersionInfoCopyright=Copyright (c) 2026 Win10 Explorer Kit contributors

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "zhcn"; MessagesFile: "Languages\ChineseSimplified.isl"

[CustomMessages]
en.ShellTask=Add "Copy path" to the Explorer context menu
zhcn.ShellTask=将“复制路径”添加到资源管理器右键菜单
en.StartupTask=Start when I sign in
zhcn.StartupTask=登录时自动启动
en.CopyLabel=Copy path (Ctrl+Shift+C)
zhcn.CopyLabel=复制路径 (Ctrl+Shift+C)
en.Launch=Launch Win10 Explorer Kit
zhcn.Launch=启动 Win10 Explorer Kit
en.RuntimeMissing=Microsoft .NET Framework 4.8 or later is required. Install it and run this setup again.
zhcn.RuntimeMissing=需要 Microsoft .NET Framework 4.8 或更新版本，请安装后重新运行本安装程序。
en.StopFailed=Close Win10 Explorer Kit before continuing.
zhcn.StopFailed=请先退出 Win10 Explorer Kit 后再继续。

[Tasks]
Name: "shell"; Description: "{cm:ShellTask}"
Name: "startup"; Description: "{cm:StartupTask}"

[Files]
Source: "{#SourceDir}\Win10ExplorerKit.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\Win10ExplorerKit.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\locales\*.json"; DestDir: "{app}\locales"; Flags: ignoreversion
Source: "{#SourceDir}\README*.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\assets\app.png"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "{#SourceDir}\licenses\Inno-Setup.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{group}\Win10 Explorer Kit"; Filename: "{app}\Win10ExplorerKit.exe"

[Registry]
Root: HKCU; Subkey: "Software\Classes\*\shell\Win10ExplorerKit"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:CopyLabel}"; Tasks: shell; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shell\Win10ExplorerKit"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Win10ExplorerKit.exe"",0"; Tasks: shell
Root: HKCU; Subkey: "Software\Classes\*\shell\Win10ExplorerKit"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: shell
Root: HKCU; Subkey: "Software\Classes\*\shell\Win10ExplorerKit\command"; ValueType: string; ValueData: """{app}\Win10ExplorerKit.exe"" --copy %* ""%1"""; Tasks: shell
Root: HKCU; Subkey: "Software\Classes\Directory\shell\Win10ExplorerKit"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:CopyLabel}"; Tasks: shell; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\Win10ExplorerKit"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Win10ExplorerKit.exe"",0"; Tasks: shell
Root: HKCU; Subkey: "Software\Classes\Directory\shell\Win10ExplorerKit"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: shell
Root: HKCU; Subkey: "Software\Classes\Directory\shell\Win10ExplorerKit\command"; ValueType: string; ValueData: """{app}\Win10ExplorerKit.exe"" --copy %* ""%1"""; Tasks: shell
Root: HKCU; Subkey: "Software\Classes\Drive\shell\Win10ExplorerKit"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{cm:CopyLabel}"; Tasks: shell; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Drive\shell\Win10ExplorerKit"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Win10ExplorerKit.exe"",0"; Tasks: shell
Root: HKCU; Subkey: "Software\Classes\Drive\shell\Win10ExplorerKit"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: shell
Root: HKCU; Subkey: "Software\Classes\Drive\shell\Win10ExplorerKit\command"; ValueType: string; ValueData: """{app}\Win10ExplorerKit.exe"" --copy %* ""%1"""; Tasks: shell
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Win10ExplorerKitHotkey"; ValueData: """{app}\Win10ExplorerKit.exe"" --hotkey"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Win10ExplorerKit.exe"; Parameters: "--set-language {code:ApplicationLanguage}"; Flags: runhidden
Filename: "{app}\Win10ExplorerKit.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent

[Code]
{ Map the installer language to the application's locale before launching the tray }
function ApplicationLanguage(Param: String): String;
begin
  if ActiveLanguage = 'zhcn' then Result := 'zh-CN'
  else Result := 'en';
end;

{ Read attributes without following a directory link during cleanup }
function ReadAttributes(Path: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

{ Check the framework before attempting to run the managed application }
function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox(CustomMessage('RuntimeMissing'), mbError, MB_OK, IDOK);
end;

{ Ask the existing host to exit before upgrade replaces any executable }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\Win10ExplorerKit.exe')) then
    if not Exec(ExpandConstant('{app}\Win10ExplorerKit.exe'), '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
      Result := CustomMessage('StopFailed');
end;

{ Stop the host before removing registration, program files and personal data }
function InitializeUninstall(): Boolean;
var
  ExitCode: Integer;
begin
  Result := True;
  if FileExists(ExpandConstant('{app}\Win10ExplorerKit.exe')) then
    Result := Exec(ExpandConstant('{app}\Win10ExplorerKit.exe'), '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
  if not Result then SuppressibleMsgBox(CustomMessage('StopFailed'), mbError, MB_OK, IDOK);
end;

{ Delete only an owned subtree and never follow a junction or symbolic link }
procedure CleanOwnedTree(Path: String);
var
  Entry: TFindRec;
  Child: String;
begin
  if not DirExists(Path) then Exit;
  if (ReadAttributes(Path) and $400) <> 0 then Exit;
  if FindFirst(AddBackslash(Path) + '*', Entry) then
  try
    repeat
      if (Entry.Name <> '.') and (Entry.Name <> '..') then
      begin
        Child := AddBackslash(Path) + Entry.Name;
        if (Entry.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          if (Entry.Attributes and $400) = 0 then CleanOwnedTree(Child);
        end
        else DeleteFile(Child);
      end;
    until not FindNext(Entry);
  finally
    FindClose(Entry);
  end;
  RemoveDir(Path);
end;

{ Run cleanup after installed files and registration have been removed }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataPath: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  #ifdef IntegrationTestDataDir
  DataPath := ExpandConstant('{#IntegrationTestDataDir}');
  #else
  DataPath := ExpandConstant('{localappdata}\Win10ExplorerKit');
  #endif
  if not DirExists(DataPath) then Exit;
  if (ReadAttributes(DataPath) and $400) <> 0 then Exit;
  CleanOwnedTree(DataPath);
end;
