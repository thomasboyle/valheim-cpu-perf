$ErrorActionPreference = "Continue"
$etl = "D:\C++\120fpsvalheim\profile\valheim_cpu_20260913_115248.etl"
$out = "D:\C++\120fpsvalheim\profile"
$xperf = "C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\xperf.exe"
$wpaex = "C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\wpaexporter.exe"

# Check outputs so far
Get-ChildItem $out | Sort-Object Length -Descending | Select-Object Name, Length | Format-Table -AutoSize

# CPU utilization by process via xperf
& $xperf -i $etl -o "$out\cpu_usage.txt" -a profile 2>&1 | Select-Object -Last 40
"profile_exit=$LASTEXITCODE size=$((Get-Item "$out\cpu_usage.txt" -EA SilentlyContinue).Length)"

# Also try symbols-less module summary
& $xperf -i $etl -o "$out\cpu_stacks.txt" -a stackwalk 2>&1 | Select-Object -Last 20
"stackwalk_exit=$LASTEXITCODE"

# Module summary for valheim process
& $xperf -i $etl -o "$out\modules.txt" -a module 2>&1 | Select-Object -Last 15
