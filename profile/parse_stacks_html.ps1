$html = Get-Content "D:\C++\120fpsvalheim\profile\cpu_stacks_pid.txt" -Raw
# Strip tags roughly and find UnityPlayer / mono sections
$text = [regex]::Replace($html, '<[^>]+>', "`n")
$text = [regex]::Replace($text, '&nbsp;', ' ')
$lines = $text -split "`n" | Where-Object { $_.Trim() -ne '' }
$lines | Select-Object -First 80 | Set-Content "D:\C++\120fpsvalheim\profile\stacks_text_head.txt"
# Find Modules by Exclusive
$idx = 0..($lines.Count-1) | Where-Object { $lines[$_] -match "Modules by Exclusive" } | Select-Object -First 1
"Exclusive section at $idx"
if ($null -ne $idx) {
  $lines[$idx..([Math]::Min($idx+80, $lines.Count-1))] | Set-Content "D:\C++\120fpsvalheim\profile\stacks_exclusive.txt"
}
Get-Content "D:\C++\120fpsvalheim\profile\stacks_text_head.txt"
Write-Host "==== EXCLUSIVE ===="
if (Test-Path "D:\C++\120fpsvalheim\profile\stacks_exclusive.txt") { Get-Content "D:\C++\120fpsvalheim\profile\stacks_exclusive.txt" }
