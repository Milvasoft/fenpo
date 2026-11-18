# Installs Fenpo from a GitHub release zip (DECISIONS #41). Run Install.cmd next to it.
#   1. Trusts the package's certificate, once, with the user's administrator approval (LocalMachine\TrustedPeople).
#   2. Installs the Windows App SDK runtime packages in Dependencies\ that are missing or older.
#   3. Installs or updates Fenpo and starts it.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$family = 'Milvasoft.Fenpo_5vqs18gpndnp6'

function Fail([string]$Message) {
    Write-Host $Message -ForegroundColor Red
    exit 1
}

$package = Get-ChildItem $here -Filter 'Fenpo_*.msix' | Select-Object -First 1
if (-not $package) { Fail 'Fenpo_*.msix is missing next to Install.ps1. Unpack the whole zip and run Install.cmd again.' }

# Fenpo from Microsoft Store has another publisher, so Windows would keep both and both would run.
$store = Get-AppxPackage -Name 'Milvasoft.Fenpo' | Where-Object { $_.PackageFamilyName -ne $family }
if ($store) { Fail 'Fenpo from Microsoft Store is installed. Remove it in Settings > Apps, or keep using it and get updates from the Store.' }

$certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2 (Join-Path $here 'Fenpo.cer')
if (-not (Test-Path "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)")) {
    Write-Host "Windows asks for administrator approval once, to trust Fenpo's certificate."
    $command = "Import-Certificate -FilePath '$((Join-Path $here 'Fenpo.cer').Replace("'", "''"))' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    try {
        Start-Process powershell -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList '-NoProfile', '-EncodedCommand', $encoded
    }
    catch {
        Fail 'Fenpo was not installed: administrator approval is needed once. Run Install.cmd again and choose Yes.'
    }

    if (-not (Test-Path "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)")) {
        Fail "Fenpo's certificate could not be trusted. Run Install.cmd again and choose Yes."
    }
}

# The framework first, then the packages that use it. A newer one already installed is fine (0x80073D06).
$order = @('Microsoft.WindowsAppRuntime.2.msix', 'Microsoft.WindowsAppRuntime.Main.2.msix', 'Microsoft.WindowsAppRuntime.Singleton.2.msix', 'Microsoft.WindowsAppRuntime.DDLM.2.msix')
foreach ($name in $order) {
    $path = Join-Path $here "Dependencies\$name"
    if (-not (Test-Path $path)) { continue }

    try {
        Add-AppxPackage -Path $path
    }
    catch {
        if ($_.Exception.Message -notmatch '0x80073D06') { Fail "Installing $name failed: $($_.Exception.Message)" }
    }
}

Write-Host "Installing $($package.Name)..."
try {
    Add-AppxPackage -Path $package.FullName -ForceApplicationShutdown
}
catch {
    Fail "Installing Fenpo failed: $($_.Exception.Message)"
}

Write-Host 'Fenpo is installed.' -ForegroundColor Green
Start-Process "shell:AppsFolder\$family!App"
