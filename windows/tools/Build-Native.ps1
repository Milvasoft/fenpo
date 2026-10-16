# Builds the C++ credential provider (native/Fenpo.CredentialProvider) with Visual Studio's MSBuild, which `dotnet build`
# cannot do. Fenpo.App's build runs this and copies the DLL next to Fenpo.exe.
param([string]$Platform = 'x64')
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio with the C++ workload is needed to build the credential provider' }
$project = Join-Path $PSScriptRoot '..\native\Fenpo.CredentialProvider\Fenpo.CredentialProvider.vcxproj'
& $msbuild $project -p:Configuration=Release "-p:Platform=$Platform" -v:m -nologo
if ($LASTEXITCODE -ne 0) { throw 'Building the credential provider failed' }
