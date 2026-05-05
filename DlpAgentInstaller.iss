[Setup]
AppName=DLP Agent
AppVersion=1.0
DefaultDirName={pf}\DLP Agent
DefaultGroupName=DLP Agent
OutputBaseFilename=DlpAgentInstaller
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin

[Files]
Source: "publish\dlp_agent.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\agent_config.json"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
Name: desktopicon; Description: "Create a desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Icons]
Name: "{commonprograms}\DLP Agent"; Filename: "{app}\dlp_agent.exe"
Name: "{autodesktop}\DLP Agent"; Filename: "{app}\dlp_agent.exe"; Tasks: desktopicon

[Run]
Filename: "powershell.exe"; Parameters: "-Command ""$WshShell = New-Object -comObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('{commonstartup}\AerologueDLPAgent.lnk'); $Shortcut.TargetPath = '{app}\dlp_agent.exe'; $Shortcut.Save()"""; Flags: runhidden

[UninstallRun]
Filename: "cmd.exe"; Parameters: "/c del ""{commonstartup}\AerologueDLPAgent.lnk"""; Flags: runhidden
