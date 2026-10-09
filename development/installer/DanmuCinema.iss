#ifndef PackageRoot
  #define PackageRoot "..\bin\installer-payload"
#endif
#ifndef PackageVersion
  #define PackageVersion "1.0.8"
#endif
#ifndef OutputFolder
  #define OutputFolder "..\..\packages"
#endif

[Setup]
#ifdef TestBuild
AppId={{EE79307C-D687-48AB-99FA-6771BB020154}
AppName=DanmuCinema Installer Test
CreateUninstallRegKey=no
#else
AppId={{19241646-79A7-4D32-8DBA-CB9DF3B7992E}
AppName=DanmuCinema 弹幕影院
#endif
AppVersion={#PackageVersion}
AppPublisher=DanmuCinema
AppPublisherURL=https://github.com/1772866953/DanmuCinema
DefaultDirName={localappdata}\Programs\DanmuCinema
DefaultGroupName=DanmuCinema
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
SetupIconFile=..\assets\DanmuCinema.ico
UninstallDisplayIcon={app}\assets\DanmuCinema.ico
OutputDir={#OutputFolder}
OutputBaseFilename=DanmuCinema-Setup-{#PackageVersion}-win-x64
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern dark polar
WizardSizePercent=120
LicenseFile=..\..\LICENSE
CloseApplications=no
RestartApplications=no
SetupLogging=yes
Uninstallable=yes
UsePreviousTasks=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
Source: "{#PackageRoot}\bin\*"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "{#PackageRoot}\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "{#PackageRoot}\runtime\*"; DestDir: "{app}\runtime"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageRoot}\plugins\Danmu_2.8.0.0\*"; DestDir: "{app}\data\jellyfin\plugins\Danmu_2.8.0.0"; Flags: ignoreversion recursesubdirs
Source: "{#PackageRoot}\plugins\DanmuCinemaPlayback_1.0.0.0\*"; DestDir: "{app}\data\jellyfin\plugins\DanmuCinemaPlayback_1.0.0.0"; Flags: ignoreversion
Source: "{#PackageRoot}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{app}\config\dandanplay.example.json"
Type: filesandordirs; Name: "{app}\licenses"

[Icons]
#ifndef TestBuild
Name: "{autoprograms}\DanmuCinema"; Filename: "{app}\bin\DanmuCinema.exe"; WorkingDir: "{app}"; IconFilename: "{app}\assets\DanmuCinema.ico"
Name: "{autodesktop}\DanmuCinema"; Filename: "{app}\bin\DanmuCinema.exe"; WorkingDir: "{app}"; IconFilename: "{app}\assets\DanmuCinema.ico"; Tasks: desktopicon
Name: "{app}\启动弹幕影院"; Filename: "{app}\bin\DanmuCinema.exe"; WorkingDir: "{app}"; IconFilename: "{app}\assets\DanmuCinema.ico"
#endif

[Run]
Filename: "{app}\bin\DanmuCinema.exe"; Description: "启动弹幕影院"; Flags: nowait postinstall skipifsilent; Check: CanLaunch

[Code]
var
  AccountPage: TInputQueryWizardPage;
  MediaPage: TInputDirWizardPage;
  ContentPage: TInputOptionWizardPage;
  NetworkPage: TInputQueryWizardPage;
  OptionsPage: TInputOptionWizardPage;
  OfficialPage: TInputFileWizardPage;
  ProvisionFile: String;
  Configured: Boolean;
  Failed: Boolean;
  KeepCacheAndLogs: Boolean;

function InitializeSetup: Boolean;
var Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then
    MsgBox('此安装包需要 Windows 10 2004 或更新版本 / Windows 11（64 位），以及系统自带的 .NET Framework 4.8。未通过运行环境检查，安装已停止。', mbError, MB_OK);
end;

function ExistingConfiguration: Boolean;
begin
  // During early wizard pages {app} has not been initialized yet.
  Result := FileExists(AddBackslash(WizardDirValue) + 'data\install-complete.json');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PageID <> AccountPage.ID) and (PageID <> MediaPage.ID) and (PageID <> ContentPage.ID) and
    (PageID <> NetworkPage.ID) and (PageID <> OptionsPage.ID) and (PageID <> OfficialPage.ID) then exit;
  if ProvisionFile <> '' then Result := True
  else Result := ExistingConfiguration;
end;

#ifdef TestBuild
procedure ProbeWizardPages;
var ReportPath, SavedDir, SavedProvision, NewDir, ExistingDir, ReportText: String; I: Integer; PageIDs: array[0..5] of Integer; Lines: TArrayOfString;
begin
  ReportPath := ExpandConstant('{param:WIZARDPROBE|}');
  if ReportPath = '' then exit;
  SavedDir := WizardDirValue; SavedProvision := ProvisionFile;
  try
    if MediaPage.Values[0] <> '' then RaiseException('Media directory must default to empty');
    if not ContentPage.Values[0] then RaiseException('Automatic media recognition must be selected by default');
    PageIDs[0] := AccountPage.ID; PageIDs[1] := MediaPage.ID; PageIDs[2] := ContentPage.ID; PageIDs[3] := NetworkPage.ID; PageIDs[4] := OptionsPage.ID; PageIDs[5] := OfficialPage.ID;
    if ShouldSkipPage(wpWelcome) or ShouldSkipPage(wpLicense) or ShouldSkipPage(wpInfoBefore) or ShouldSkipPage(wpSelectDir) then RaiseException('Built-in pages were incorrectly skipped');
    // Exercise the same callbacks before {app} is initialized, without installing files.
    NewDir := ExpandConstant('{param:PROBENEW|}'); ExistingDir := ExpandConstant('{param:PROBEEXISTING|}');
    if (NewDir = '') or (ExistingDir = '') then RaiseException('Probe directories are missing');
    WizardForm.DirEdit.Text := NewDir;
    for I := 0 to 5 do if ShouldSkipPage(PageIDs[I]) then RaiseException('Fresh installation must show setup page');
    WizardForm.DirEdit.Text := ExistingDir;
    for I := 0 to 5 do if not ShouldSkipPage(PageIDs[I]) then RaiseException('Existing configuration must skip setup page');
    WizardForm.DirEdit.Text := NewDir;
    for I := 0 to 5 do if ShouldSkipPage(PageIDs[I]) then RaiseException('Changed directory must refresh configuration detection');
    ProvisionFile := 'fixture-only';
    for I := 0 to 5 do if not ShouldSkipPage(PageIDs[I]) then RaiseException('Provisioned installation must skip setup page');
    ReportText := 'PASS: empty media default, simplified description, early wizard callbacks, fresh install, existing configuration, changed directory, and provisioned pages';
  except
    ReportText := 'FAIL: ' + GetExceptionMessage;
  end;
  WizardForm.DirEdit.Text := SavedDir; ProvisionFile := SavedProvision;
  SetArrayLength(Lines, 1); Lines[0] := ReportText;
  SaveStringsToUTF8File(ReportPath, Lines, False);
  PostMessage(WizardForm.Handle, $0010, 0, 0);
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if ExpandConstant('{param:WIZARDPROBE|}') <> '' then begin Cancel := True; Confirm := False; end;
end;
#endif

procedure InitializeWizard;
begin
  ProvisionFile := ExpandConstant('{param:CONFIG|}');
  AccountPage := CreateInputQueryPage(wpSelectDir, '设置管理员账号', '用于连接和管理本机视频服务', '请设置服务管理员账号与密码。这不是 Windows 登录密码。');
  AccountPage.Add('管理员账号：', False);
  AccountPage.Add('管理员密码（至少 8 位）：', True);
  AccountPage.Add('确认密码：', True);
  AccountPage.Values[0] := 'admin';
  MediaPage := CreateInputDirPage(AccountPage.ID, '选择媒体文件夹', '安装完成后自动添加到媒体库', '请选择保存视频的现有文件夹。', False, '');
  MediaPage.Add('媒体文件夹：');
  MediaPage.Values[0] := '';
  ContentPage := CreateInputOptionPage(MediaPage.ID, '媒体库识别方式', '电影、电视剧和动漫可使用同一个媒体库', '自动识别会按文件名和目录结构识别电影与剧集。安装后可在软件中调整已有媒体库的识别方式。', True, False);
  ContentPage.Add('自动识别：电影 / 电视剧 / 动漫（推荐）');
  ContentPage.Add('仅按电影组织');
  ContentPage.Add('按电视剧 / 动漫组织');
  ContentPage.Values[0] := True;
  NetworkPage := CreateInputQueryPage(ContentPage.ID, '设置局域网端口', '视频服务与弹幕服务使用不同端口', '默认值适合大多数情况。如有其他服务占用端口，请设置为不同的值。');
  NetworkPage.Add('视频服务端口：', False);
  NetworkPage.Add('弹幕 API 端口：', False);
  NetworkPage.Values[0] := '8096';
  NetworkPage.Values[1] := '9321';
  OptionsPage := CreateInputOptionPage(NetworkPage.ID, '启动与网络设置', '安装完成后即可启动服务', '开机启动使用当前 Windows 账号。局域网放行仅限专用网络和本地子网，需要 Windows 管理员确认。', False, False);
  OptionsPage.Add('开机自动启动，并在托盘运行');
  OptionsPage.Add('关闭主窗口时收起到托盘');
  OptionsPage.Add('允许局域网访问（会弹出 Windows 管理员确认）');
  OptionsPage.Values[1] := True;
  OptionsPage.Values[2] := True;
  OfficialPage := CreateInputFilePage(OptionsPage.ID, '导入官方弹幕配置（可选）', '可沿用你已经申请的接口配置', '已有配置可选择 dandanplay.json。留空会使用其他已启用来源，安装后也能在 config 目录配置。加密配置仅可在原 Windows 账号下导入。');
  OfficialPage.Add('已有的官方弹幕配置：', 'JSON 配置文件|*.json|所有文件|*.*', '.json');
#ifdef TestBuild
  ProbeWizardPages;
#endif
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var P, D: Integer;
begin
  Result := False;
  if CurPageID = AccountPage.ID then begin
    if (Trim(AccountPage.Values[0]) = '') or (Length(Trim(AccountPage.Values[0])) > 64) then begin
      MsgBox('请输入 1 至 64 个字符的管理员账号。', mbError, MB_OK); exit;
    end;
    if Length(AccountPage.Values[1]) < 8 then begin
      MsgBox('管理员密码至少需要 8 个字符。', mbError, MB_OK); exit;
    end;
    if AccountPage.Values[1] <> AccountPage.Values[2] then begin
      MsgBox('两次输入的密码不一致。', mbError, MB_OK); exit;
    end;
  end;
  if (CurPageID = MediaPage.ID) and ((Trim(MediaPage.Values[0]) = '') or not DirExists(MediaPage.Values[0])) then begin
    MsgBox('请选择已存在的视频文件夹。', mbError, MB_OK); exit;
  end;
  if (CurPageID = OfficialPage.ID) and (OfficialPage.Values[0] <> '') and not FileExists(OfficialPage.Values[0]) then begin
    MsgBox('找不到所选的配置文件，请重新选择，或留空跳过。', mbError, MB_OK); exit;
  end;
  if CurPageID = NetworkPage.ID then begin
    P := StrToIntDef(NetworkPage.Values[0], 0);
    D := StrToIntDef(NetworkPage.Values[1], 0);
    if (P < 1024) or (P > 65535) or (D < 1024) or (D > 65535) or (P = D) then begin
      MsgBox('端口必须在 1024 至 65535 之间，两个端口不能相同。', mbError, MB_OK); exit;
    end;
  end;
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer; Probe: String;
begin
  Result := '';
  if WizardSilent and not ExistingConfiguration and (ProvisionFile = '') then begin
    Result := '首次静默安装必须通过 /CONFIG= 提供私有配置文件，不能在命令行传递密码。'; exit;
  end;
  if (ProvisionFile <> '') and not FileExists(ProvisionFile) then begin
    Result := '找不到指定的私有配置文件。'; exit;
  end;
  if FileExists(ExpandConstant('{app}\bin\DanmuCinema.exe')) then
    if not Exec(ExpandConstant('{app}\bin\DanmuCinema.exe'), '--installer-check', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then begin
      Result := '请先从托盘退出此安装目录中的弹幕影院，再安装或更新。'; exit;
    end;
  if not ForceDirectories(ExpandConstant('{app}')) then begin Result := '无法创建安装目录，请选择当前 Windows 用户可写的位置。'; exit; end;
  Probe := ExpandConstant('{app}\.danmucinema-write-check');
  if not SaveStringToFile(Probe, 'test', False) then begin Result := '安装目录不可写，请选择当前 Windows 用户可写的位置。'; exit; end;
  DeleteFile(Probe);
end;

function J(S: String): String;
begin
  StringChangeEx(S, '\', '\\', True);
  StringChangeEx(S, '"', '\"', True);
  StringChangeEx(S, #13, '\r', True);
  StringChangeEx(S, #10, '\n', True);
  StringChangeEx(S, #9, '\t', True);
  Result := '"' + S + '"';
end;
function B(V: Boolean): String;
begin if V then Result := 'true' else Result := 'false'; end;

procedure CurStepChanged(CurStep: TSetupStep);
var RequestFile, JsonText, MessageText, LibraryType: String; Lines: TArrayOfString; Code: Integer; Good: Boolean;
begin
  if CurStep <> ssPostInstall then exit;
#ifdef TestBuild
  if ExpandConstant('{param:CLEANUPPROBE|}') = '1' then begin Configured := True; exit; end;
#endif
  WizardForm.StatusLabel.Caption := '正在初始化账号、媒体库与内置插件，请稍候…';
  RequestFile := ExpandConstant('{tmp}\initialize.json');
  if ExistingConfiguration then JsonText := '{}' else if ProvisionFile <> '' then begin
    if not CopyFile(ProvisionFile, RequestFile, False) then RaiseException('无法读取私有配置文件。');
    JsonText := '';
  end else begin
    LibraryType := 'mixed'; if ContentPage.Values[1] then LibraryType := 'movies' else if ContentPage.Values[2] then LibraryType := 'tvshows';
    JsonText := '{"AdminName":' + J(Trim(AccountPage.Values[0])) + ',"Password":' + J(AccountPage.Values[1]) +
      ',"MediaFolder":' + J(MediaPage.Values[0]) + ',"LibraryType":' + J(LibraryType) +
      ',"Port":' + NetworkPage.Values[0] + ',"DanmuPort":' + NetworkPage.Values[1] +
      ',"CloseToTray":' + B(OptionsPage.Values[1]) + ',"AutoStart":' + B(OptionsPage.Values[0]) + ',"DandanConfigFile":' + J(OfficialPage.Values[0]) + '}';
  end;
  if JsonText <> '' then begin
    SetArrayLength(Lines, 1); Lines[0] := JsonText;
    if not SaveStringsToUTF8File(RequestFile, Lines, False) then RaiseException('无法保存临时安装配置。');
  end;
  AccountPage.Values[1] := ''; AccountPage.Values[2] := ''; JsonText := ''; SetArrayLength(Lines, 0);
  Good := Exec(ExpandConstant('{app}\bin\DanmuCinema.exe'), '--installer-configure "' + RequestFile + '"', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code);
  DeleteFile(RequestFile);
  if not Good or (Code <> 0) then begin
    Failed := True;
    MessageText := '安装文件已解压，但服务初始化失败。请检查端口和媒体目录，然后重新运行安装器。';
    if FileExists(RequestFile + '.result') then begin
      LoadStringsFromFile(RequestFile + '.result', Lines);
      if GetArrayLength(Lines) > 0 then MessageText := Lines[0];
    end;
    DeleteFile(RequestFile + '.result');
    RaiseException(MessageText);
  end;
  DeleteFile(RequestFile + '.result'); Configured := True;
  // CONFIG mode is used for isolated tests and does not change the host firewall.
  if (ProvisionFile = '') and OptionsPage.Values[2] then
    if not ShellExec('runas', ExpandConstant('{app}\bin\DanmuCinema.exe'), '--installer-firewall', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      MsgBox('服务已初始化。局域网防火墙放行未完成；可稍后在软件中开启局域网访问。', mbInformation, MB_OK);
end;

function CanLaunch: Boolean;
begin Result := Configured and not Failed; end;

function GetCustomSetupExitCode: Integer;
begin if Failed or not Configured then Result := 1 else Result := 0; end;

function SelectUninstallData: Boolean;
var Form: TSetupForm; Note: TNewStaticText; Keep: TNewCheckBox; ContinueButton, CancelButton: TNewButton;
begin
  Result := True;
  KeepCacheAndLogs := ExpandConstant('{param:REMOVECACHELOGS|0}') <> '1';
  if UninstallSilent then exit;
  Form := CreateCustomForm(ScaleX(470), ScaleY(200), False, False);
  try
    Form.Caption := '卸载弹幕影院';
    Form.Position := poScreenCenter;
    Note := TNewStaticText.Create(Form); Note.Parent := Form;
    Note.Left := ScaleX(24); Note.Top := ScaleY(22); Note.Width := ScaleX(422); Note.Height := ScaleY(58);
    Note.AutoSize := False; Note.WordWrap := True;
    Note.Caption := '请选择是否保留缓存和日志记录。账号和 API 配置将删除，媒体文件不受影响。';
    Keep := TNewCheckBox.Create(Form); Keep.Parent := Form;
    Keep.Left := ScaleX(24); Keep.Top := ScaleY(88); Keep.Width := ScaleX(422); Keep.Height := ScaleY(26);
    Keep.Caption := '保留缓存和日志记录'; Keep.Checked := True;
    ContinueButton := TNewButton.Create(Form); ContinueButton.Parent := Form;
    ContinueButton.Left := ScaleX(244); ContinueButton.Top := ScaleY(148); ContinueButton.Width := ScaleX(96); ContinueButton.Height := ScaleY(30);
    ContinueButton.Caption := '继续卸载'; ContinueButton.ModalResult := mrOk; ContinueButton.Default := True;
    CancelButton := TNewButton.Create(Form); CancelButton.Parent := Form;
    CancelButton.Left := ScaleX(350); CancelButton.Top := ScaleY(148); CancelButton.Width := ScaleX(96); CancelButton.Height := ScaleY(30);
    CancelButton.Caption := '取消'; CancelButton.ModalResult := mrCancel; CancelButton.Cancel := True;
    Result := Form.ShowModal = mrOk;
    if Result then KeepCacheAndLogs := Keep.Checked;
  finally
    Form.Free;
  end;
end;

function InitializeUninstall: Boolean;
var Code: Integer;
begin
  Result := Exec(ExpandConstant('{app}\bin\DanmuCinema.exe'), '--installer-check', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
  if not Result then MsgBox('请先从托盘退出弹幕影院，再卸载。', mbError, MB_OK);
  if not Result then exit;
  Result := SelectUninstallData;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Code: Integer; CleanupArgument: String;
begin
  if CurUninstallStep = usUninstall then begin
    Exec(ExpandConstant('{app}\bin\DanmuCinema.exe'), '--installer-cleanup', '', SW_HIDE, ewWaitUntilTerminated, Code);
    if not UninstallSilent then
      if MsgBox('是否移除本安装目录创建的局域网防火墙规则？需要 Windows 管理员确认。', mbConfirmation, MB_YESNO) = IDYES then
        ShellExec('runas', ExpandConstant('{app}\bin\DanmuCinema.exe'), '--installer-remove-firewall', '', SW_HIDE, ewWaitUntilTerminated, Code);
    CleanupArgument := '--installer-remove-data';
    if KeepCacheAndLogs then CleanupArgument := '--installer-remove-data-keep-cache-logs';
    if not Exec(ExpandConstant('{app}\bin\DanmuCinema.exe'), CleanupArgument, '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException('账号、API 配置或缓存清理失败。请关闭占用文件的程序后重新卸载。');
  end;
end;
