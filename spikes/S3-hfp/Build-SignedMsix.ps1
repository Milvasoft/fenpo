# Packs the spike into a signed .msix with a self-signed "CN=Fenpo Development" certificate, to compare a
# real MSIX install with the loose registration of Register-Package.ps1. Needs no admin rights.
# Installing needs the certificate trusted once (admin), see the lines printed at the end.
$ErrorActionPreference = 'Stop'
$kit = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64'
$project = Join-Path $PSScriptRoot 'HfpSpike\HfpSpike.csproj'
$layout = Join-Path $PSScriptRoot 'HfpSpike\bin\Debug\net10.0-windows10.0.22621.0\win-x64'
$out = Join-Path $PSScriptRoot 'HfpSpike\bin\msix'
$subject = 'CN=Fenpo Development'

dotnet build $project -c Debug
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

New-Item -ItemType Directory -Force $out | Out-Null
$msix = Join-Path $out 'FenpoS3.msix'
& "$kit\makeappx.exe" pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx failed' }

$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(2)
}

$cer = Join-Path $out 'FenpoDevelopment.cer'
Export-Certificate -Cert $cert -FilePath $cer | Out-Null

& "$kit\signtool.exe" sign /fd SHA256 /sha1 $cert.Thumbprint $msix
if ($LASTEXITCODE -ne 0) { throw 'signtool failed' }

Write-Host ''
Write-Host "Signed: $msix"
Write-Host "Certificate: $cer (thumbprint $($cert.Thumbprint))"
Write-Host 'Trust it once from an elevated PowerShell:'
Write-Host "  Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
Write-Host 'Then replace the loose registration with the signed package:'
Write-Host "  Get-AppxPackage Fenpo.Spikes.S3Hfp | Remove-AppxPackage; Add-AppxPackage '$msix'"
