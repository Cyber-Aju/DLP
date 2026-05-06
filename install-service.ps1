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
$userStartupPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
$userShortcutPath = Join-Path $userStartupPath 'AerologueDLPAgent.lnk'
$vbsPath = Join-Path $startupPath 'AerologueDLPAgent.vbs'

if (Test-Path $userShortcutPath) {
    Remove-Item $userShortcutPath -Force
    Write-Host "Removed duplicate user startup shortcut at $userShortcutPath."
}

# Create VBScript to launch hidden
$vbsContent = @"
Set WshShell = CreateObject("WScript.Shell")
WshShell.Run """$exePath""", 0, False
"@

Set-Content -Path $vbsPath -Value $vbsContent -Encoding ASCII

Write-Host "Hidden autostart VBScript created at $vbsPath."