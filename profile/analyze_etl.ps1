$ErrorActionPreference = "Continue"
$etl = "D:\C++\120fpsvalheim\profile\valheim_cpu_20260913_115248.etl"
$out = "D:\C++\120fpsvalheim\profile"
"ETL bytes: $((Get-Item $etl).Length)"

# Try xperf summary
$xperf = @(
  "C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\xperf.exe",
  "C:\Windows\System32\xperf.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
"xperf=$xperf"

if ($xperf) {
  & $xperf -i $etl -a dumper -d "$out\xperf_dump.txt" 2>&1 | Select-Object -First 20
}

# Faster: use wpaexporter or tracerpt
$wpaex = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit" -Filter "wpaexporter.exe" -EA SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
"wpaexporter=$wpaex"

# tracerpt for summary (can be slow on 1GB)
# Instead use xperf -i with CPU profile summary actions
if ($xperf) {
  # Process summary
  & $xperf -i $etl -o "$out\xperf_proc.csv" -a process 2>&1 | Tee-Object "$out\xperf_process_log.txt" | Select-Object -Last 30
}
