# Builds Fenpo.App and registers the build output as a loose (development) package, replacing any earlier
# registration. Needs Developer Mode. Afterwards start Fenpo from the Start menu or by typing "fenpo".
param([string]$Platform = 'x64', [string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\Fenpo.App\Fenpo.App.csproj'

Get-Process Fenpo -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300
dotnet build $project -c $Configuration -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$layout = Join-Path $PSScriptRoot "..\src\Fenpo.App\bin\$Platform\$Configuration\net10.0-windows10.0.22621.0"
Get-AppxPackage Milvasoft.Fenpo | Where-Object { $_.InstallLocation -ne (Resolve-Path $layout).Path } | Remove-AppxPackage
Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ForceApplicationShutdown
Get-AppxPackage Milvasoft.Fenpo | Select-Object Name, Version, InstallLocation
