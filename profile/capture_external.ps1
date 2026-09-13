$ErrorActionPreference = "Continue"
$outDir = "D:\C++\120fpsvalheim\profile"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$etl = Join-Path $outDir ("valheim_cpu_" + (Get-Date -Format "yyyyMMdd_HHmmss") + ".etl")
$sampleLog = Join-Path $outDir "cpu_samples.csv"

# Sample process CPU for ~25s while trying WPR
$p = Get-Process -Name valheim -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { "NO_VALHEIM"; exit 2 }
"PID=$($p.Id) WS=$([math]::Round($p.WorkingSet64/1MB))MB"

# Start elevated WPR via scheduled task / Start-Process -Verb RunAs if possible
$wprScript = @"
wpr -cancel 2>`$null
wpr -start CPU -filemode
Start-Sleep -Seconds 25
wpr -stop `"$etl`"
"@
$wprPs1 = Join-Path $outDir "run_wpr.ps1"
Set-Content -Path $wprPs1 -Value $wprScript -Encoding UTF8

# Try direct wpr first
$wprOk = $false
try {
  $r = & wpr -start CPU -filemode 2>&1
  if ($LASTEXITCODE -eq 0) {
    $wprOk = $true
    "WPR_STARTED_DIRECT"
  } else {
    "WPR_DIRECT_FAIL: $r"
  }
} catch {
  "WPR_DIRECT_EX: $_"
}

# CPU sample loop (~25s)
$sw = [Diagnostics.Stopwatch]::StartNew()
$rows = New-Object System.Collections.Generic.List[string]
$rows.Add("utc,pid,cpu_sec,ws_mb,threads,priv_mb")
$lastCpu = $null
$lastT = $null
while ($sw.Elapsed.TotalSeconds -lt 25) {
  $proc = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
  if (-not $proc) { "PROCESS_EXITED"; break }
  $now = Get-Date
  $cpu = $proc.CPU
  $ws = [math]::Round($proc.WorkingSet64/1MB,1)
  $pm = [math]::Round($proc.PrivateMemorySize64/1MB,1)
  $thr = $proc.Threads.Count
  $deltaCpu = ""
  if ($null -ne $lastCpu -and $null -ne $lastT) {
    $dt = ($now - $lastT).TotalSeconds
    if ($dt -gt 0) { $deltaCpu = [math]::Round(($cpu - $lastCpu) / $dt * 100, 1) } # % of one core
  }
  $rows.Add(("{0},{1},{2},{3},{4},{5}" -f $now.ToUniversalTime().ToString("o"), $proc.Id, $cpu, $ws, $thr, $pm))
  if ($deltaCpu -ne "") { "t=$([int]$sw.Elapsed.TotalSeconds)s cpu_core%≈$deltaCpu ws=${ws}MB thr=$thr" }
  $lastCpu = $cpu
  $lastT = $now
  Start-Sleep -Milliseconds 1000
}
$rows | Set-Content -Path $sampleLog -Encoding UTF8

if ($wprOk) {
  & wpr -stop $etl 2>&1
  "WPR_STOPPED=$etl exit=$LASTEXITCODE"
} else {
  # Try elevating via Start-Process
  $elev = Join-Path $outDir "elev_wpr.ps1"
  @"
wpr -cancel 2>`$null | Out-Null
`$code = 0
try {
  wpr -start CPU -filemode
  if (`$LASTEXITCODE -ne 0) { exit `$LASTEXITCODE }
  Start-Sleep -Seconds 25
  wpr -stop `"$etl`"
  exit `$LASTEXITCODE
} catch { exit 1 }
"@ | Set-Content -Path $elev -Encoding UTF8
  try {
    $p2 = Start-Process -FilePath "powershell.exe" -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$elev`"" -Verb RunAs -PassThru -Wait -WindowStyle Hidden
    "ELEV_WPR_EXIT=$($p2.ExitCode) etl_exists=$(Test-Path $etl)"
  } catch {
    "ELEV_FAILED: $_"
  }
}

"SAMPLE_LOG=$sampleLog"
Get-ChildItem $outDir | Select-Object Name, Length, LastWriteTime
