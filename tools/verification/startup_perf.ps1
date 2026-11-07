# Starts a portable Tibia Foundry build from a fresh folder, measures time to the main window,
# memory after start-up work has settled, and closes it again. Prints one CSV line.
param([Parameter(Mandatory)][string]$SourceExe, [Parameter(Mandatory)][string]$Label, [Parameter(Mandatory)][string]$WorkDir, [int]$SettleSeconds = 20)
if (Test-Path -LiteralPath $WorkDir) { Remove-Item -LiteralPath $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force $WorkDir | Out-Null
Copy-Item -LiteralPath $SourceExe -Destination $WorkDir
$exe = Join-Path $WorkDir "TibiaFoundry.exe"
$sw = [Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $exe -PassThru
$window = -1
$peakPrivate = 0
while ($sw.Elapsed.TotalSeconds -lt ($SettleSeconds + 30)) {
    Start-Sleep -Milliseconds 100
    $proc.Refresh()
    if ($proc.HasExited) { break }
    if ($window -lt 0 -and $proc.MainWindowHandle -ne [IntPtr]::Zero) { $window = $sw.Elapsed.TotalMilliseconds }
    if ($proc.PrivateMemorySize64 -gt $peakPrivate) { $peakPrivate = $proc.PrivateMemorySize64 }
    if ($window -ge 0 -and $sw.Elapsed.TotalSeconds -ge $SettleSeconds) { break }
}
$proc.Refresh()
$ws = $proc.WorkingSet64; $priv = $proc.PrivateMemorySize64; $peakWs = $proc.PeakWorkingSet64
$cpu = $proc.TotalProcessorTime.TotalSeconds
[void]$proc.CloseMainWindow()
if (-not $proc.WaitForExit(15000)) { $proc.Kill() }
$log = Get-ChildItem (Join-Path $WorkDir "TibiaFoundry-Data\logs") -Filter *.log | Select-Object -First 1
$lines = if ($log) { Get-Content -LiteralPath $log.FullName } else { @() }
function Stamp($pattern) { $l = $lines | Where-Object { $_ -match $pattern } | Select-Object -First 1; if ($l) { [datetime]::ParseExact($l.Substring(0, 23), 'yyyy-MM-dd HH:mm:ss.fff', $null) } }
# "Compatibility check:" is the result line of every version (6.0 also logs when the check is queued and started).
$start = Stamp 'starting'; $loaded = Stamp 'Loaded appearances'; $compat = Stamp 'Compatibility check:'
$loadMs = if ($start -and $loaded) { [int]($loaded - $start).TotalMilliseconds } else { '' }
$compatMs = if ($start -and $compat) { [int]($compat - $start).TotalMilliseconds } else { '' }
"{0},{1:0},{2},{3},{4:0},{5:0},{6:0},{7:0.0}" -f $Label, $window, $loadMs, $compatMs, ($ws / 1MB), ($priv / 1MB), ($peakWs / 1MB), $cpu
