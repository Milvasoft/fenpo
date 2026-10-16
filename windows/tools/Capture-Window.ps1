# Saves a PNG of a top-level window (by process name and optional title) for visual checks of both themes and
# languages. Uses PrintWindow with PW_RENDERFULLCONTENT so WinUI/DirectComposition content is captured.
param([string]$Process = 'Fenpo', [string]$Title = '', [Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class FenpoCapture {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

# Per-monitor DPI aware, so window rectangles are in physical pixels like PrintWindow's output.
[void][FenpoCapture]::SetProcessDpiAwarenessContext([IntPtr]-4)
$pids = (Get-Process $Process).Id
$target = [IntPtr]::Zero
$best = 0
[FenpoCapture]::EnumWindows({
    param($hwnd, $lparam)
    $windowPid = 0
    [void][FenpoCapture]::GetWindowThreadProcessId($hwnd, [ref]$windowPid)
    if ($pids -contains $windowPid -and [FenpoCapture]::IsWindowVisible($hwnd)) {
        $text = New-Object System.Text.StringBuilder 256
        [void][FenpoCapture]::GetWindowText($hwnd, $text, 256)
        $rect = New-Object FenpoCapture+RECT
        [void][FenpoCapture]::GetWindowRect($hwnd, [ref]$rect)
        $area = ($rect.Right - $rect.Left) * ($rect.Bottom - $rect.Top)
        if (($Title -eq '' -or $text.ToString() -like "*$Title*") -and $area -gt $script:best) { $script:best = $area; $script:target = $hwnd }
    }
    return $true
}, [IntPtr]::Zero) | Out-Null

if ($target -eq [IntPtr]::Zero) { throw "No visible window for $Process" }
[void][FenpoCapture]::SetForegroundWindow($target)
Start-Sleep -Milliseconds 400
$rect = New-Object FenpoCapture+RECT
[void][FenpoCapture]::GetWindowRect($target, [ref]$rect)
$bitmap = New-Object System.Drawing.Bitmap ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
[void][FenpoCapture]::PrintWindow($target, $hdc, 2)
$graphics.ReleaseHdc($hdc)
$graphics.Dispose()
$bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Host "Saved $Path"
