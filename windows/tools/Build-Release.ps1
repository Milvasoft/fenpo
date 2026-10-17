# Builds the release MSIX bundle of Fenpo for Windows (DECISIONS #41), x64 and ARM64 in one .msixbundle.
#   Store:  the package identity Partner Center gave the app; left unsigned, the Store signs it.
#   GitHub: our own identity, signed with the code signing certificate (by thumbprint, from the certificate store;
#           Certum's SimplySign puts its cloud certificate there). Fenpo Kamera's and the Explorer command's DLLs are
#           signed too, before they go into the package: Fenpo Kamera's DLL is copied out of the package when installed.
#
#   powershell -File tools\Build-Release.ps1 -Channel Store -Version 0.2.0.0 -IdentityName 12345Milvasoft.Fenpo -Publisher "CN=..."
#   powershell -File tools\Build-Release.ps1 -Channel GitHub -Version 0.2.0.0 -Publisher "CN=..., O=..." -CertificateThumbprint ABC...
param(
    [Parameter(Mandatory)][ValidateSet('Store', 'GitHub')][string]$Channel,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.0$')][string]$Version,
    [string]$IdentityName = 'Milvasoft.Fenpo',
    [string]$Publisher,
    [string]$PublisherDisplayName = 'Milvasoft',
    [string[]]$Platforms = @('x64', 'ARM64'),
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://time.certum.pl',
    [string]$OutDir
)
$ErrorActionPreference = 'Stop'
$Platforms = $Platforms -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ } # "x64,ARM64" from -File
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot "..\artifacts\$Channel" }
$src = Join-Path $PSScriptRoot '..\src'
$manifest = Join-Path $src 'Fenpo.App\Package.appxmanifest'
$project = Join-Path $src 'Fenpo.App\Fenpo.App.csproj'

if ($Channel -eq 'GitHub' -and -not $CertificateThumbprint) { throw 'GitHub builds are signed: pass -CertificateThumbprint' }

# Windows SDK tools: the newest makeappx and signtool.
$sdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\10.*\x64\makeappx.exe' | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $sdk) { throw 'Windows SDK not found (makeappx.exe)' }
$makeappx = $sdk.FullName
$signtool = Join-Path $sdk.DirectoryName 'signtool.exe'

function Sign([string[]]$Files) {
    & $signtool sign /fd SHA256 /sha1 $CertificateThumbprint /tr $TimestampUrl /td SHA256 $Files
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $Files" }
}

# Native AOT publishing links with Visual Studio's tools, found through vswhere.
$env:PATH = "$env:PATH;${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"

$original = [IO.File]::ReadAllBytes($manifest)
try {
    # The identity of this channel, in a copy of the manifest that is put back afterwards.
    $xml = New-Object Xml.XmlDocument
    $xml.Load($manifest)
    $identity = $xml.Package.Identity
    $identity.Name = $IdentityName
    $identity.Version = $Version
    if ($Publisher) { $identity.Publisher = $Publisher }
    $xml.Package.Properties.PublisherDisplayName = $PublisherDisplayName
    $settings = New-Object Xml.XmlWriterSettings -Property @{ Indent = $true; Encoding = New-Object Text.UTF8Encoding($true) }
    $writer = [Xml.XmlWriter]::Create($manifest, $settings)
    $xml.Save($writer)
    $writer.Dispose()

    Remove-Item $OutDir -Recurse -Force -ErrorAction SilentlyContinue
    $packages = New-Item -ItemType Directory -Force (Join-Path $OutDir 'packages')

    foreach ($platform in $Platforms) {
        $rid = if ($platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }

        # The two Native AOT DLLs, published where Fenpo.App's build picks them up (Fenpo.App.csproj).
        foreach ($native in @(@{ Project = 'Fenpo.VirtualCamera'; Dir = 'vcam' }, @{ Project = 'Fenpo.ShellExtension'; Dir = 'shell' })) {
            $out = Join-Path $src "Fenpo.App\obj\$($native.Dir)\$rid"
            dotnet publish (Join-Path $src "$($native.Project)\$($native.Project).csproj") -c Release -r $rid -o $out -nologo -v q
            if ($LASTEXITCODE -ne 0) { throw "Publishing $($native.Project) for $rid failed" }
            if ($Channel -eq 'GitHub') { Sign (Join-Path $out "$($native.Project).dll") }
        }

        $appx = Join-Path $OutDir "appx\$platform\"
        dotnet build $project -c Release -p:Platform=$platform -p:GenerateAppxPackageOnBuild=true -p:AppxPackageDir=$appx -p:AppxPackageSigningEnabled=false -p:AppxBundle=Never -nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "Building Fenpo for $platform failed" }
        Get-ChildItem $appx -Recurse -Filter *.msix | Where-Object { $_.FullName -notlike '*\Dependencies\*' } | Copy-Item -Destination $packages
    }

    $bundle = Join-Path $OutDir "Fenpo_$($Version)_$Channel.msixbundle"
    & $makeappx bundle /o /d $packages /p $bundle /bv $Version
    if ($LASTEXITCODE -ne 0) { throw 'Bundling failed' }
    if ($Channel -eq 'GitHub') { Sign $bundle }

    Get-Item $bundle | Select-Object Name, Length, FullName
}
finally {
    [IO.File]::WriteAllBytes($manifest, $original)
}
