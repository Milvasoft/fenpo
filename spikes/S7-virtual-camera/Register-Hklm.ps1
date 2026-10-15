# Classic registration of the media source (needs an elevated PowerShell). Copies the AOT DLL to a folder the
# Frame Server (Local Service) can read and registers its COM class in HKLM. "-Unregister" undoes both.
param([switch]$Unregister)
$ErrorActionPreference = 'Stop'
$target = 'C:\ProgramData\Fenpo\S7VCam'
$dll = Join-Path $target 'VCamNetSampleSourceAOT.dll'
$key = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\{570b1532-4fcb-472f-ae37-b5daf86abfd4}\InprocServer32'

$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $elevated) { throw 'Run this from an elevated PowerShell (Run as administrator).' }

if ($Unregister) {
    if (Test-Path $dll) {
        (Start-Process regsvr32.exe -ArgumentList '/s', '/u', "`"$dll`"" -Wait -PassThru).ExitCode | Out-Null
        Remove-Item $target -Recurse -Force
    }
    Write-Host "Unregistered. Key present: $(Test-Path $key)"
    return
}

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'bin\source\VCamNetSampleSourceAOT.dll') $dll -Force

# regsvr32 is a GUI program: start it explicitly and wait, otherwise PowerShell does not see its exit code.
$exit = (Start-Process regsvr32.exe -ArgumentList '/s', "`"$dll`"" -Wait -PassThru).ExitCode
if ($exit -ne 0 -or -not (Test-Path $key)) { throw "regsvr32 exit code $exit, key present: $(Test-Path $key)" }
Write-Host "Registered: $((Get-ItemProperty $key).'(default)')"
