# Builds VCamHost, AOT-publishes the media source, packs both into a signed .msix (reusing the
# "CN=Fenpo Development" certificate from spikes/S3-hfp) and installs it. The certificate must already be
# trusted (Trusted People), see spikes/S3-hfp/Build-SignedMsix.ps1.
$ErrorActionPreference = 'Stop'
$kit = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64'
$env:PATH += ';C:\Program Files (x86)\Microsoft Visual Studio\Installer'
$layout = Join-Path $PSScriptRoot 'VCamHost\bin\Debug\net10.0-windows10.0.22621.0\win-x64'
$out = Join-Path $PSScriptRoot 'bin\msix'

dotnet publish (Join-Path $PSScriptRoot 'upstream\VCamNetSampleSourceAOT\VCamNetSampleSourceAOT.csproj') -c Release -r win-x64 -p:Platform=x64 -o (Join-Path $PSScriptRoot 'bin\source')
if ($LASTEXITCODE -ne 0) { throw 'Media source publish failed' }

dotnet build (Join-Path $PSScriptRoot 'VCamHost\VCamHost.csproj') -c Debug
if ($LASTEXITCODE -ne 0) { throw 'VCamHost build failed' }

Copy-Item (Join-Path $PSScriptRoot 'bin\source\VCamNetSampleSourceAOT.dll') $layout -Force

New-Item -ItemType Directory -Force $out | Out-Null
$msix = Join-Path $out 'FenpoS7.msix'
& "$kit\makeappx.exe" pack /d $layout /p $msix /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makeappx failed' }

$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=Fenpo Development' -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $cert) { throw 'Run spikes/S3-hfp/Build-SignedMsix.ps1 first to create the certificate' }
& "$kit\signtool.exe" sign /fd SHA256 /sha1 $cert.Thumbprint $msix | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'signtool failed' }

Get-AppxPackage Fenpo.Spikes.S7VCam | Remove-AppxPackage
Add-AppxPackage $msix
Get-AppxPackage Fenpo.Spikes.S7VCam | Select-Object Name, SignatureKind, InstallLocation
