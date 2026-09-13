$lines = Get-Content "D:\C++\120fpsvalheim\profile\cpu_detail.txt" | Where-Object { $_ -match "valheim\.exe" }
$rows = foreach ($l in $lines) {
  if ($l -match 'valheim\.exe\s*\((\d+)\),\s*([\d.]+),\s*([\d.]+),\s*(.*)$') {
    [pscustomobject]@{ Weight=[double]$Matches[2]; Usage=[double]$Matches[3]; Module=$Matches[4].Trim().Trim('"') }
  }
}
"TOTAL_VALHEIM_WEIGHT=$(( $rows | Measure-Object Weight -Sum).Sum)"
"TOP MODULES:"
$rows | Sort-Object Weight -Descending | Select-Object -First 40 | Format-Table -AutoSize | Out-String -Width 200

Write-Host "===== STACK FILES ====="
Get-Item "D:\C++\120fpsvalheim\profile\cpu_stacks*.txt" -EA SilentlyContinue | Format-Table Name, Length
$sf = "D:\C++\120fpsvalheim\profile\cpu_stacks_valheim.txt"
if ((Test-Path $sf) -and ((Get-Item $sf).Length -gt 0)) {
  Get-Content $sf -TotalCount 80
  Write-Host "===== STACK TAIL ====="
  Get-Content $sf -Tail 40
}
$sf2 = "D:\C++\120fpsvalheim\profile\cpu_stacks_pid.txt"
if ((Test-Path $sf2) -and ((Get-Item $sf2).Length -gt 0)) {
  Write-Host "===== PID STACKS HEAD ====="
  Get-Content $sf2 -TotalCount 60
}
