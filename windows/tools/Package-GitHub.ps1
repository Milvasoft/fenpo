# Makes the GitHub release zips (DECISIONS #41): Build-Release.ps1 -Channel GitHub, then one zip per platform with that
# platform's signed package, the Windows App SDK runtime packages it needs, the certificate and the installer
# (tools\github\Install.cmd). Until there is a code signing certificate from a CA, the package is signed with the
# "CN=Fenpo Development" certificate and Install.cmd trusts it on the user's PC.
#
#   powershell -File tools\Package-GitHub.ps1 -Version 1.0.8.0 -CertificateThumbprint 925D77DD...
#
# The zips land in artifacts\GitHub\zip\fenpo-<version>-<platform>.zip with a SHA-256 next to each.
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.0$')][string]$Version,
    [Parameter(Mandatory)][string]$CertificateThumbprint,
    [string[]]$Platforms = @('x64', 'ARM64')
)
$ErrorActionPreference = 'Stop'
$Platforms = $Platforms -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
$root = Split-Path -Parent $PSScriptRoot

$certificate = Get-Item "Cert:\CurrentUser\My\$CertificateThumbprint"
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-Release.ps1') -Channel GitHub -Version $Version -Publisher $certificate.Subject -Platforms ($Platforms -join ',') -CertificateThumbprint $CertificateThumbprint
if ($LASTEXITCODE -ne 0) { throw 'Build-Release.ps1 failed' }

# The runtime packages come with the Windows App SDK's NuGet package, at the version Fenpo builds with.
$sdkVersion = ([xml](Get-Content (Join-Path $root 'Directory.Packages.props'))).Project.ItemGroup.PackageVersion | Where-Object Include -eq 'Microsoft.WindowsAppSDK' | ForEach-Object Version
$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$runtime = Join-Path $nuget "microsoft.windowsappsdk.runtime\$sdkVersion\tools\MSIX"
if (-not (Test-Path $runtime)) { throw "Windows App SDK runtime packages not found: $runtime" }

$out = Join-Path $root 'artifacts\GitHub'
$zipDir = New-Item -ItemType Directory -Force (Join-Path $out 'zip')
$short = $Version -replace '\.0$', ''

foreach ($platform in $Platforms) {
    $arch = $platform.ToLowerInvariant()
    $stage = Join-Path $out "stage\$arch"
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force (Join-Path $stage 'Dependencies') | Out-Null

    $package = Get-ChildItem (Join-Path $out 'packages') -Filter "*_$platform.msix" | Select-Object -First 1
    if (-not $package) { throw "No $platform package in $out\packages" }
    Copy-Item $package.FullName (Join-Path $stage "Fenpo_$($Version)_$arch.msix")
    Copy-Item (Join-Path $runtime "win10-$arch\*.msix") (Join-Path $stage 'Dependencies')
    Export-Certificate -Cert $certificate -FilePath (Join-Path $stage 'Fenpo.cer') | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'github\Install.cmd'), (Join-Path $PSScriptRoot 'github\Install.ps1') $stage

    $zip = Join-Path $zipDir "fenpo-$short-$arch.zip"
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    Set-Content -Encoding ascii -Path "$zip.sha256" -Value "$hash  fenpo-$short-$arch.zip"
    Write-Host "$zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB) $hash"
}
