# Classic registration of the media source (needs an elevated PowerShell). Copies the AOT DLL to a folder the
# Frame Server (Local Service) can read and registers its COM class in HKLM. "-Unregister" undoes both.
param([switch]$Unregister)
$ErrorActionPreference = 'Stop'
$target = 'C:\ProgramData\Fenpo\S7VCam'
$dll = Join-Path $target 'VCamNetSampleSourceAOT.dll'

if ($Unregister) {
    if (Test-Path $dll) { & regsvr32.exe /s /u $dll; Remove-Item $target -Recurse -Force }
    Write-Host 'Unregistered and removed.'
    return
}

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'bin\source\VCamNetSampleSourceAOT.dll') $dll -Force
& regsvr32.exe /s $dll
if ($LASTEXITCODE -ne 0) { throw "regsvr32 failed with $LASTEXITCODE" }
Write-Host "Registered $dll in HKLM."
