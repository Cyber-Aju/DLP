$publishDir = Join-Path $PSScriptRoot 'publish'

Write-Host "Stopping any running DlpAgent service or agent process..."
try {
    sc.exe stop DlpAgent 2>$null | Out-Null
} catch {
}
Get-Process -Name dlp_agent -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $publishDir | Out-Null

dotnet publish "$PSScriptRoot\dlp_agent.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false -o $publishDir

Copy-Item -Path "$PSScriptRoot\agent_config.json" -Destination $publishDir -Force

Write-Host "Published self-contained executable to $publishDir"
Write-Host "Then compile DlpAgentInstaller.iss with Inno Setup or use the install-service script."