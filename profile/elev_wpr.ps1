wpr -cancel 2>$null | Out-Null
$code = 0
try {
  wpr -start CPU -filemode
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  Start-Sleep -Seconds 25
  wpr -stop "D:\C++\120fpsvalheim\profile\valheim_cpu_20260913_115248.etl"
  exit $LASTEXITCODE
} catch { exit 1 }
