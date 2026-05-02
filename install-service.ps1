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

$serviceName = 'DlpAgent'
$displayName = 'Aerologue DLP Agent'

sc.exe create $serviceName binPath= "`"$exePath`"" start= auto DisplayName= "$displayName"
sc.exe failure $serviceName reset= 86400 actions= restart/600/restart/600/restart/600
sc.exe start $serviceName

Write-Host "Service '$serviceName' installed and started."