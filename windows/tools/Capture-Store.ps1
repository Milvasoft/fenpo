# Takes the Microsoft Store screenshots: every page of "fenpo --demo" (a made-up phone and made-up content, no real
# data) in Light/Dark x Türkçe/English, with the window's visible frame at exactly 1920 x 1080. Do not type while it
# runs: each new demo window takes the keyboard focus. Check every picture before uploading it.
# The build is registered for the run as a package of its own (Milvasoft.Fenpo.Demo, "fenpo-demo"), so the installed
# Fenpo, its data and a running instance are left alone; the demo package is removed at the end. Needs Developer Mode
# and a Debug build (dotnet build src/Fenpo.App -p:Platform=x64).
param(
    [string]$Layout = (Join-Path $PSScriptRoot '..\src\Fenpo.App\bin\x64\Debug\net10.0-windows10.0.22621.0'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\artifacts\store-screenshots'),
    [string[]]$Pages = @('Home', 'Notifications', 'Messages', 'Calls', 'Files', 'Clipboard', 'Camera', 'Settings'),
    [string[]]$Themes = @('Light', 'Dark'),
    [string[]]$Languages = @('tr-TR', 'en-US'),
    [int]$Width = 1920,
    [int]$Height = 1080,
    [int]$WaitSeconds = 5)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$staging = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\store-layout'))
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class FenpoStoreCapture {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

# Per-monitor DPI aware, so rectangles are in physical pixels like PrintWindow's output.
[void][FenpoStoreCapture]::SetProcessDpiAwarenessContext([IntPtr]-4)

function Get-DemoProcesses {
    Get-Process Fenpo -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($staging, [StringComparison]::OrdinalIgnoreCase) }
}

function Stop-Demo {
    Get-DemoProcesses | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

function Get-Rects([IntPtr]$hwnd) {
    $outer = New-Object FenpoStoreCapture+RECT
    $frame = New-Object FenpoStoreCapture+RECT
    [void][FenpoStoreCapture]::GetWindowRect($hwnd, [ref]$outer)
    # DWMWA_EXTENDED_FRAME_BOUNDS: the window without its invisible resize borders.
    [void][FenpoStoreCapture]::DwmGetWindowAttribute($hwnd, 9, [ref]$frame, 16)
    return $outer, $frame
}

function Find-MainWindow {
    $pids = @((Get-DemoProcesses).Id)
    $script:found = [IntPtr]::Zero
    $script:largest = 0
    [FenpoStoreCapture]::EnumWindows({
        param($hwnd, $lparam)
        $windowPid = 0
        [void][FenpoStoreCapture]::GetWindowThreadProcessId($hwnd, [ref]$windowPid)
        if ($pids -contains $windowPid -and [FenpoStoreCapture]::IsWindowVisible($hwnd)) {
            $text = New-Object System.Text.StringBuilder 256
            [void][FenpoStoreCapture]::GetWindowText($hwnd, $text, 256)
            $rect = New-Object FenpoStoreCapture+RECT
            [void][FenpoStoreCapture]::GetWindowRect($hwnd, [ref]$rect)
            $area = ($rect.Right - $rect.Left) * ($rect.Bottom - $rect.Top)
            # The main window, not the tray panel, the island or a window still being created.
            if ($text.ToString() -like '*Fenpo*' -and ($rect.Right - $rect.Left) -ge 800 -and ($rect.Bottom - $rect.Top) -ge 500 -and $area -gt $script:largest) { $script:largest = $area; $script:found = $hwnd }
        }
        return $true
    }, [IntPtr]::Zero) | Out-Null
    return $script:found
}

function Save-Window([IntPtr]$hwnd, [string]$path) {
    # Grow the window so its visible frame is exactly $Width x $Height, a little in from the primary screen's corner.
    # Moving to another screen can change the window's DPI and size, so it is measured and sized again.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $outer, $frame = Get-Rects $hwnd
        $borderX = ($outer.Right - $outer.Left) - ($frame.Right - $frame.Left)
        $borderY = ($outer.Bottom - $outer.Top) - ($frame.Bottom - $frame.Top)
        $left = 40 - ($frame.Left - $outer.Left)
        $top = 40 - ($frame.Top - $outer.Top)
        [void][FenpoStoreCapture]::SetWindowPos($hwnd, [IntPtr]::Zero, $left, $top, $Width + $borderX, $Height + $borderY, 0x0014)
        Start-Sleep -Milliseconds 1500
        $outer, $frame = Get-Rects $hwnd
        $frameWidth = $frame.Right - $frame.Left
        $frameHeight = $frame.Bottom - $frame.Top
        if ($frameWidth -eq $Width -and $frameHeight -eq $Height) { break }
    }

    if ($frameWidth -ne $Width -or $frameHeight -ne $Height) { throw "The window is ${frameWidth}x${frameHeight}, not ${Width}x${Height}" }

    $full = New-Object System.Drawing.Bitmap ($outer.Right - $outer.Left), ($outer.Bottom - $outer.Top)
    $graphics = [System.Drawing.Graphics]::FromImage($full)
    $hdc = $graphics.GetHdc()
    [void][FenpoStoreCapture]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $crop = New-Object System.Drawing.Rectangle ($frame.Left - $outer.Left), ($frame.Top - $outer.Top), $Width, $Height
    $image = $full.Clone($crop, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $image.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $image.Dispose()
    $full.Dispose()
    Write-Host "Saved $path"
}

# A copy of the build with its own identity, without the shell extension and the notification activator, whose COM
# classes belong to the installed Fenpo.
Get-AppxPackage Milvasoft.Fenpo.Demo | Remove-AppxPackage
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
Copy-Item -Recurse $Layout $staging
$manifestPath = Join-Path $staging 'AppxManifest.xml'
$manifest = New-Object System.Xml.XmlDocument
$manifest.Load($manifestPath)
$manifest.Package.Identity.Name = 'Milvasoft.Fenpo.Demo'
$manifest.Package.Properties.DisplayName = 'Fenpo Demo'
$extensions = $manifest.Package.Applications.Application.Extensions
foreach ($extension in @($extensions.ChildNodes)) {
    switch ($extension.Category) {
        'windows.appExecutionAlias' { $extension.AppExecutionAlias.ExecutionAlias.Alias = 'fenpo-demo.exe' }
        'windows.startupTask' { }
        default { [void]$extensions.RemoveChild($extension) }
    }
}
$manifest.Save($manifestPath)
Add-AppxPackage -Register $manifestPath
$alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\fenpo-demo.exe'

try {
    for ($wait = 0; -not (Test-Path $alias) -and $wait -lt 20; $wait++) { Start-Sleep -Milliseconds 500 }
    foreach ($theme in $Themes) {
        foreach ($language in $Languages) {
            foreach ($page in $Pages) {
                Stop-Demo
                Start-Process $alias -ArgumentList @('--demo', '--page', $page, '--theme', $theme, '--language', $language)
                $hwnd = [IntPtr]::Zero
                for ($wait = 0; $hwnd -eq [IntPtr]::Zero -and $wait -lt 30; $wait++) {
                    Start-Sleep -Milliseconds 500
                    $hwnd = Find-MainWindow
                }

                if ($hwnd -eq [IntPtr]::Zero) { throw "No Fenpo window for $page ($theme, $language)" }

                # Lists, the page transition and the Mica backdrop settle.
                Start-Sleep -Seconds $WaitSeconds
                Save-Window $hwnd (Join-Path $OutDir "$page-$theme-$language.png")
            }
        }
    }
}
finally {
    # Removing the package also removes its data; the copy goes with it.
    Stop-Demo
    Get-AppxPackage Milvasoft.Fenpo.Demo | Remove-AppxPackage
    Remove-Item -Recurse -Force $staging
}
