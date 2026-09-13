# Alternative: sample valheim thread CPU with Get-Process; try xperf/wpr with GeneralProfile if CPU verbose needs admin
$ErrorActionPreference = "Continue"
$outDir = "D:\C++\120fpsvalheim\profile"

# Check elev etl and any leftover wpr status
wpr -status 2>&1
"ETL_SIZE=$((Get-Item "$outDir\valheim_cpu_20260913_115248.etl" -EA SilentlyContinue).Length)"

# Thread CPU breakdown for valheim
$p = Get-Process valheim -EA SilentlyContinue | Select-Object -First 1
if ($p) {
  $threads = $p.Threads | Sort-Object TotalProcessorTime -Descending | Select-Object -First 15 Id, @{N='CpuMs';E={[int]$_.TotalProcessorTime.TotalMilliseconds}}, ThreadState, WaitReason
  $threads | Format-Table -AutoSize | Out-String -Width 200 | Tee-Object "$outDir\thread_cpu.txt"
}

# Try lightweight ETW via logman (may also need admin)
logman query providers "Microsoft-Windows-Kernel-Process" 2>&1 | Select-Object -First 5

# Check if PerfView exists
Get-Command PerfView -EA SilentlyContinue
Get-ChildItem "C:\Program Files*\*" -Filter "PerfView.exe" -Recurse -EA SilentlyContinue | Select-Object -First 3 FullName
Get-ChildItem "C:\Tools","C:\Users\thoma\Downloads","D:\" -Filter "PerfView.exe" -Recurse -Depth 3 -EA SilentlyContinue | Select-Object -First 5 FullName
