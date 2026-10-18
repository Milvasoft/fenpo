# Builds Fenpo.App and registers the build output as a loose (development) package, replacing any earlier
# development registration. Needs Developer Mode. Afterwards start Fenpo from the Start menu or by typing "fenpo".
# The Microsoft Store and GitHub installs have the same package name with another publisher; they are left alone
# (only one copy of Fenpo runs at a time, so quit the installed one before starting this one).
param([string]$Platform = 'x64', [string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\Fenpo.App\Fenpo.App.csproj'
$devRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\Fenpo.App\bin'))
$layout = Join-Path $devRoot "$Platform\$Configuration\net10.0-windows10.0.22621.0"

# Development registrations and processes are the ones whose files are in this repository's bin folder.
function Get-DevPackage { Get-AppxPackage Milvasoft.Fenpo | Where-Object { $_.InstallLocation -like "$devRoot*" } }

# Fenpo can come back while building (a notification click, the tray, a phone waking it): stop it again and retry
# once when the build could not overwrite its files.
for ($attempt = 1; $attempt -le 2; $attempt++) {
    Get-Process Fenpo -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$devRoot*" } | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    $output = dotnet build $project -c $Configuration -p:Platform=$Platform 2>&1
    if ($LASTEXITCODE -eq 0) { $output | Select-String 'Build succeeded' | Select-Object -First 1; break }
    if ($attempt -eq 2 -or -not ($output -match 'MSB3027|MSB3021')) { $output | Select-String ' error ' ; throw 'Build failed' }
}

Get-DevPackage | Where-Object { $_.InstallLocation -ne $layout } | Remove-AppxPackage
try
{
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ForceApplicationShutdown
}
catch
{
    # 0x80073CFB: the manifest changed but the version did not. Removing also clears LocalState (settings, logs).
    if ($_.Exception.HResult -ne 0x80073CFB -and $_.Exception.Message -notmatch '0x80073CFB') { throw }
    Write-Warning 'Manifest changed, removing the old registration (LocalState is cleared)'
    Get-DevPackage | Remove-AppxPackage
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ForceApplicationShutdown
}
Get-DevPackage | Select-Object Name, Version, InstallLocation
