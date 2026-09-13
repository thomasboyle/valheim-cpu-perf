$ErrorActionPreference = "Continue"
$etl = "D:\C++\120fpsvalheim\profile\valheim_cpu_20260913_115248.etl"
$out = "D:\C++\120fpsvalheim\profile"
$xperf = "C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\xperf.exe"

Write-Host "=== PROFILE DETAIL ==="
& $xperf -i $etl -o "$out\cpu_detail.txt" -a profile -detail 2>&1 | Select-Object -Last 15
"detail_size=$((Get-Item "$out\cpu_detail.txt").Length)"

Write-Host "=== STACK BUTTERFLY VALHEIM ==="
& $xperf -i $etl -o "$out\cpu_stacks_valheim.txt" -a stack -butterfly 50 -process "valheim" 2>&1 | Select-Object -Last 20
"stacks_size=$((Get-Item "$out\cpu_stacks_valheim.txt" -EA SilentlyContinue).Length)"

# Also try pid
& $xperf -i $etl -o "$out\cpu_stacks_pid.txt" -a stack -butterfly 30 -pid 19912 2>&1 | Select-Object -Last 10
"stacks_pid_size=$((Get-Item "$out\cpu_stacks_pid.txt" -EA SilentlyContinue).Length)"
