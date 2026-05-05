$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    Write-Error "This script must be run as Administrator."
    exit 1
}

$exePath = Join-Path $PSScriptRoot 'publish\dlp_agent.exe'
if (-not (Test-Path $exePath)) {
    Write-Error "Could not find published executable at $exePath. Run build_publish.ps1 first."
    exit 1
}

$startupPath = "C:\ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp"
$shortcutPath = Join-Path $startupPath 'AerologueDLPAgent.lnk'

$WshShell = New-Object -comObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($shortcutPath)
$Shortcut.TargetPath = $exePath
$Shortcut.Save()

Write-Host "Autostart shortcut created at $shortcutPath."