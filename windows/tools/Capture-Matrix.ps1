# Captures a page in Light/Dark x Türkçe/English by writing the settings file, launching "fenpo --page <Page>"
# and saving one PNG per combination. Used to check every screen in both themes and both languages.
param([Parameter(Mandatory)][string]$Page, [Parameter(Mandatory)][string]$OutDir, [int]$WaitSeconds = 5)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$package = Get-AppxPackage Milvasoft.Fenpo
$settingsPath = Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalState\settings.json"
$original = if (Test-Path $settingsPath) { Get-Content $settingsPath -Raw } else { $null }

try {
    foreach ($theme in 'Light', 'Dark') {
        foreach ($language in 'tr-TR', 'en-US') {
            Get-Process Fenpo -ErrorAction SilentlyContinue | Stop-Process -Force
            Start-Sleep -Milliseconds 500
            $settings = if ($original) { $original | ConvertFrom-Json } else { [pscustomobject]@{} }
            $settings | Add-Member -NotePropertyName Theme -NotePropertyValue $theme -Force
            $settings | Add-Member -NotePropertyName Language -NotePropertyValue $language -Force
            $settings | Add-Member -NotePropertyName SetupCompleted -NotePropertyValue $true -Force
            New-Item -ItemType Directory -Force (Split-Path $settingsPath) | Out-Null
            $settings | ConvertTo-Json | Set-Content -Encoding utf8 $settingsPath
            Start-Process fenpo.exe -ArgumentList '--page', $Page
            Start-Sleep -Seconds $WaitSeconds
            & (Join-Path $PSScriptRoot 'Capture-Window.ps1') -Title 'Fenpo' -Path (Join-Path $OutDir "$Page-$theme-$language.png")
        }
    }
}
finally {
    Get-Process Fenpo -ErrorAction SilentlyContinue | Stop-Process -Force
    if ($original) { Set-Content -Encoding utf8 $settingsPath $original } elseif (Test-Path $settingsPath) { Remove-Item $settingsPath }
}
