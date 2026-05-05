$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
    Write-Error "This script must be run as Administrator."
    exit 1
}

$serviceName = 'DlpAgent'
$startupPath = "C:\ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp"
$shortcutPath = Join-Path $startupPath 'AerologueDLPAgent.lnk'

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -eq 'Running' -or $service.Status -eq 'StartPending') {
        sc.exe stop $serviceName | Out-Null
    }
    sc.exe delete $serviceName | Out-Null
    Write-Host "Service '$serviceName' stopped and deleted."
} else {
    Write-Host "Service '$serviceName' does not exist."
}

if (Test-Path $shortcutPath) {
    Remove-Item $shortcutPath -Force
    Write-Host "Autostart shortcut removed from $shortcutPath."
} else {
    Write-Host "Autostart shortcut does not exist."
}
