# Builds the spike and registers its output folder as a loose MSIX package (needs Developer Mode).
# Afterwards run "FenpoS3" from any terminal; that starts HfpSpike.exe with package identity.
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'HfpSpike\HfpSpike.csproj'

dotnet build $project -c Debug
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$layout = Join-Path $PSScriptRoot 'HfpSpike\bin\Debug\net10.0-windows10.0.22621.0\win-x64'
Get-AppxPackage -Name 'Fenpo.Spikes.S3Hfp' | Remove-AppxPackage
Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
Get-AppxPackage -Name 'Fenpo.Spikes.S3Hfp' | Select-Object Name, Version, InstallLocation
