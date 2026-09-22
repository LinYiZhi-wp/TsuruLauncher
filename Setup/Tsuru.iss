; ═══════════════════════════════════════════════════════════════
;  Tsuru Launcher — Inno Setup 打包脚本
;
;  用法（在 Setup/ 目录下）：
;    "C:\Users\<你>\AppData\Local\Programs\Inno Setup 6\ISCC.exe" Tsuru.iss
;
;  前置：先在仓库根目录执行
;    dotnet publish TsuruLauncher/TsuruLauncher.csproj -c Release -o publish --no-restore
;  （注意加 --no-restore：本机 NuGet 源配置有问题，带 restore 会报 path1 null）
;
;  与旧 LYZL.iss 的区别：
;    * 旧脚本 [Files] 只放了 LYZL.exe —— 漏掉全部依赖 DLL / WebView2Loader，
;      装出来是跑不起来的。这里改成整个 publish 目录递归打包。
;    * 应用名 / 版本 / 仓库地址 / AppId 全部换成 Tsuru。
; ═══════════════════════════════════════════════════════════════

#define MyAppName "Tsuru Launcher"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "Tsuru"
#define MyAppURL "https://github.com/LinYiZhi-wp/TsuruLauncher"
#define MyAppExeName "TsuruLauncher.exe"

[Setup]
; 新产品，用新的 AppId（旧的 LYZL 安装不会自动升级，需要手动卸载）
AppId={{7C4B2E91-3A5D-4F8E-B6C2-9D1E5A7B3F04}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=Tsuru-Launcher-Setup-v{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
; 整个 publish 目录递归打包（含 runtimes\ 下的 WebView2Loader.dll）
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DotNetDownloadUrl = 'https://dotnet.microsoft.com/download/dotnet/8.0';

{ 检查 .NET 8 桌面运行时是否已安装（本发布是框架依赖型，没装就起不来）。
  查两个位置：系统级 Program Files\dotnet 与用户级 %USERPROFILE%\.dotnet。 }
function HasDotNet8DesktopRuntime: Boolean;
var
  FindRec: TFindRec;
  BaseDir: String;
begin
  Result := False;

  BaseDir := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if FindFirst(BaseDir + '\8.*', FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
    Exit;
  end;

  BaseDir := ExpandConstant('{userprofile}\.dotnet\shared\Microsoft.WindowsDesktop.App');
  if FindFirst(BaseDir + '\8.*', FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function InitializeSetup: Boolean;
var
  ErrCode: Integer;
begin
  Result := True;

  if not HasDotNet8DesktopRuntime then
  begin
    if MsgBox('未检测到 .NET 8 桌面运行时（Desktop Runtime）。' + #13#10#13#10 +
              'Tsuru Launcher 需要它才能启动。是否现在打开下载页面？' + #13#10#13#10 +
              '（选择「否」仍会继续安装，你可以稍后自行安装运行时）',
              mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', DotNetDownloadUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrCode);
  end;
end;
