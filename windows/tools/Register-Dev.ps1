# Builds Fenpo.App and registers the build output as a loose (development) package, replacing any earlier
# registration. Needs Developer Mode. Afterwards start Fenpo from the Start menu or by typing "fenpo".
param([string]$Platform = 'x64', [string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\Fenpo.App\Fenpo.App.csproj'

# Fenpo can come back while building (a notification click, the tray, a phone waking it): stop it again and retry
# once when the build could not overwrite its files.
for ($attempt = 1; $attempt -le 2; $attempt++) {
    Get-Process Fenpo -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    $output = dotnet build $project -c $Configuration -p:Platform=$Platform 2>&1
    if ($LASTEXITCODE -eq 0) { $output | Select-String 'Build succeeded' | Select-Object -First 1; break }
    if ($attempt -eq 2 -or -not ($output -match 'MSB3027|MSB3021')) { $output | Select-String ' error ' ; throw 'Build failed' }
}

$layout = Join-Path $PSScriptRoot "..\src\Fenpo.App\bin\$Platform\$Configuration\net10.0-windows10.0.22621.0"
Get-AppxPackage Milvasoft.Fenpo | Where-Object { $_.InstallLocation -ne (Resolve-Path $layout).Path } | Remove-AppxPackage
try
{
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ForceApplicationShutdown
}
catch
{
    # 0x80073CFB: the manifest changed but the version did not. Removing also clears LocalState (settings, logs).
    if ($_.Exception.HResult -ne 0x80073CFB -and $_.Exception.Message -notmatch '0x80073CFB') { throw }
    Write-Warning 'Manifest changed, removing the old registration (LocalState is cleared)'
    Get-AppxPackage Milvasoft.Fenpo | Remove-AppxPackage
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ForceApplicationShutdown
}
Get-AppxPackage Milvasoft.Fenpo | Select-Object Name, Version, InstallLocation
