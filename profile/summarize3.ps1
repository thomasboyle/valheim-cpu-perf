$lines = Get-Content "D:\C++\120fpsvalheim\profile\cpu_detail.txt" | Where-Object { $_ -match "valheim\.exe" }
$rows = foreach ($l in $lines) {
  if ($l -match 'valheim\.exe\s*\((\d+)\),\s*([\d.]+),\s*([\d.]+),\s*(.*)$') {
    [pscustomobject]@{ Weight=[double]$Matches[2]; Usage=[double]$Matches[3]; Module=$Matches[4].Trim().Trim('"') }
  }
}
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("TOTAL_VALHEIM_WEIGHT=$(( $rows | Measure-Object Weight -Sum).Sum)")
[void]$sb.AppendLine("TOP MODULES:")
foreach ($r in ($rows | Sort-Object Weight -Descending | Select-Object -First 35)) {
  [void]$sb.AppendLine(("{0,14:N0}  {1,6:N2}%  {2}" -f $r.Weight, $r.Usage, $r.Module))
}
[void]$sb.AppendLine("STACK FILES:")
Get-Item "D:\C++\120fpsvalheim\profile\cpu_stacks*.txt" -EA SilentlyContinue | ForEach-Object {
  [void]$sb.AppendLine("$($_.Name) $($_.Length)")
}
$out = "D:\C++\120fpsvalheim\profile\module_summary.txt"
$sb.ToString() | Set-Content $out -Encoding UTF8
Get-Content $out
