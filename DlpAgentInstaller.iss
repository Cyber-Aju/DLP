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
Filename: "sc.exe"; Parameters: "create DlpAgent binPath= ""{app}\\dlp_agent.exe"" start= auto DisplayName= ""Aerologue DLP Agent"""; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "failure DlpAgent reset= 86400 actions= restart/600/restart/600/restart/600"; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "start DlpAgent"; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop DlpAgent"; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "delete DlpAgent"; Flags: runhidden waituntilterminated
