$sf = "D:\C++\120fpsvalheim\profile\cpu_stacks_pid.txt"
# Extract interesting symbol-ish tokens (may be native only)
$patterns = @("Clutter","Smoke","Particle","Grass","BaseAI","Zone","Heightmap","EnvMan","Update","PhysX","Job","Burst","Cull","Shadow","ParticleSystem")
$content = Get-Content $sf -Raw
foreach ($p in $patterns) {
  $c = ([regex]::Matches($content, [regex]::Escape($p))).Count
  "{0}: {1}" -f $p, $c
}
"--- sample lines with Unity/mono ---"
Select-String -Path $sf -Pattern "UnityPlayer|mono-2|Burst|PhysX|nvogl" | Select-Object -First 20 | ForEach-Object { $_.Line.Substring(0, [Math]::Min(200, $_.Line.Length)) }
