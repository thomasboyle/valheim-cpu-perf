$ErrorActionPreference = "Stop"
function Read-Hotspots($path) {
  $map = @{}
  Import-Csv $path | ForEach-Object {
    $map[$_.method] = [pscustomobject]@{
      method = $_.method
      calls = [long]$_.calls
      total_ms = [double]$_.total_ms
      max_ms = [double]$_.max_ms
    }
  }
  return $map
}

$a = Read-Hotspots "D:\C++\120fpsvalheim\profile\managed_hotspots_20260913_121848.csv"
$b = Read-Hotspots "D:\C++\120fpsvalheim\profile\managed_hotspots_20260913_122000.csv"

$keys = ($a.Keys + $b.Keys) | Select-Object -Unique
$rows = foreach ($k in $keys) {
  $ca = if ($a.ContainsKey($k)) { $a[$k].calls } else { 0 }
  $cb = if ($b.ContainsKey($k)) { $b[$k].calls } else { 0 }
  $ta = if ($a.ContainsKey($k)) { $a[$k].total_ms } else { 0 }
  $tb = if ($b.ContainsKey($k)) { $b[$k].total_ms } else { 0 }
  $mb = if ($b.ContainsKey($k)) { $b[$k].max_ms } else { 0 }
  [pscustomobject]@{
    method = $k
    d_calls = $cb - $ca
    d_ms = $tb - $ta
    max_ms = $mb
  }
}

$total = ($rows | Measure-Object d_ms -Sum).Sum
Write-Output "DELTA window ~12:18:48 -> 12:20:00 (~72s in-world). Total delta sampled ms = $([math]::Round($total,1))"
Write-Output ""
Write-Output "method,d_calls,d_ms,avg_ms,pct,max_ms_lifetime"
$rows | Sort-Object d_ms -Descending | Select-Object -First 20 | ForEach-Object {
  $avg = if ($_.d_calls -gt 0) { $_.d_ms / $_.d_calls } else { 0 }
  $pct = if ($total -gt 0) { 100.0 * $_.d_ms / $total } else { 0 }
  "{0},{1},{2:F1},{3:F4},{4:F1},{5:F2}" -f $_.method, $_.d_calls, $_.d_ms, $avg, $pct, $_.max_ms
}

# Also dump full latest CSV
Write-Output "`n=== LATEST FULL CSV TOP 20 ==="
Import-Csv "D:\C++\120fpsvalheim\profile\managed_hotspots_20260913_122000.csv" | Select-Object -First 20 | Format-Table -AutoSize | Out-String -Width 200

# Check BaseAI / MonsterAI / ParticleMist presence
Write-Output "`n=== KEY METHODS IN LATEST ==="
Import-Csv "D:\C++\120fpsvalheim\profile\managed_hotspots_20260913_122000.csv" | Where-Object { $_.method -match 'BaseAI|MonsterAI|AnimalAI|ParticleMist|Smoke|Clutter|ZSync|Minimap|ZNet|Water|Fish|WearNTear' } | Format-Table -AutoSize | Out-String -Width 200
