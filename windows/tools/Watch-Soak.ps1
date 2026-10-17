# The overnight run (ASAMA-1 "Sağlamlaştırma"): samples Fenpo once a minute (memory, handles, threads, CPU) into a CSV
# and, at the end, counts from Fenpo's own log how often the phones dropped and came back and whether anything failed.
# Run it before leaving the PC; it keeps the PC awake while it runs and stops by itself after -Hours.
#   powershell -ExecutionPolicy Bypass -File tools\Watch-Soak.ps1 -Hours 8
param([double]$Hours = 8, [string]$OutDir = (Join-Path $env:USERPROFILE 'Desktop'))
$ErrorActionPreference = 'Stop'

Add-Type -Namespace Fenpo -Name Power -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);'
# ES_CONTINUOUS | ES_SYSTEM_REQUIRED: no sleep while this runs (the screen may still turn off).
[Fenpo.Power]::SetThreadExecutionState([uint32]2147483649) | Out-Null

$started = Get-Date
$stamp = $started.ToString('yyyyMMdd-HHmm')
$csv = Join-Path $OutDir "fenpo-soak-$stamp.csv"
$package = Get-AppxPackage Milvasoft.Fenpo
$logs = Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalState\logs"
'time,running,pid,working_set_mb,private_mb,handles,threads,cpu_s' | Set-Content -Encoding utf8 $csv
Write-Host "Watching Fenpo for $Hours h into $csv. Leave this window open; Ctrl+C stops early."

try {
    while ((Get-Date) -lt $started.AddHours($Hours)) {
        $p = Get-Process Fenpo -ErrorAction SilentlyContinue | Select-Object -First 1
        $line = if ($p) {
            '{0},1,{1},{2:F1},{3:F1},{4},{5},{6:F0}' -f (Get-Date -Format s), $p.Id, ($p.WorkingSet64 / 1MB), ($p.PrivateMemorySize64 / 1MB), $p.HandleCount, $p.Threads.Count, $p.TotalProcessorTime.TotalSeconds
        } else {
            '{0},0,,,,,,' -f (Get-Date -Format s)
        }
        Add-Content -Encoding utf8 $csv $line
        Start-Sleep -Seconds 60
    }
}
finally {
    [Fenpo.Power]::SetThreadExecutionState([uint32]2147483648) | Out-Null

    # Summary from the samples and from Fenpo's log over the same time.
    $rows = Import-Csv $csv
    $up = @($rows | Where-Object running -eq '1')
    $lines = Get-ChildItem $logs -Filter *.log | ForEach-Object { Get-Content $_.FullName -Encoding utf8 } |
        Where-Object { $_ -match '^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)' -and [datetime]$Matches[1] -ge $started }
    $count = { param($pattern) @($lines | Where-Object { $_ -match $pattern }).Count }
    $summary = @(
        "Fenpo overnight run, $($started.ToString('g')) - $((Get-Date).ToString('g'))"
        "Samples: $($rows.Count), Fenpo running in $($up.Count)"
        if ($up.Count -gt 1) {
            "Private memory: $($up[0].private_mb) MB at start, $($up[-1].private_mb) MB at end, max $(($up | Measure-Object private_mb -Maximum).Maximum) MB"
            "Handles: $($up[0].handles) at start, $($up[-1].handles) at end; threads $($up[0].threads) -> $($up[-1].threads)"
        }
        "Fenpo restarts: $(& $count 'started from')"
        "iPhone Bluetooth: $(& $count 'status "Connected"') connects"
        "Wi-Fi sessions opened: $(& $count 'connected over Wi-Fi'), closed: $(& $count 'left the Wi-Fi channel')"
        "Notifications shown: $(& $count 'notification from|Notification \d')"
        "Errors: $(& $count '\[ERR\]'), warnings: $(& $count '\[WRN\]')"
    )
    $summaryPath = [IO.Path]::ChangeExtension($csv, '.txt')
    $summary | Set-Content -Encoding utf8 $summaryPath
    $summary | Write-Host
    Write-Host "Summary saved to $summaryPath"
}
