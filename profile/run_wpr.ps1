wpr -cancel 2>$null
wpr -start CPU -filemode
Start-Sleep -Seconds 25
wpr -stop "D:\C++\120fpsvalheim\profile\valheim_cpu_20260913_115248.etl"
