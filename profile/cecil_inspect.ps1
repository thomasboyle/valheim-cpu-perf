$ErrorActionPreference = "Stop"
$cecil = "D:\Steam Library\steamapps\common\Valheim\BepInEx\core\Mono.Cecil.dll"
Add-Type -Path $cecil
$asmPath = "D:\Steam Library\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll"
$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory((Split-Path $asmPath))
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.AssemblyResolver = $resolver
$rp.ReadWrite = $false
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($asmPath, $rp)

$targets = @(
  @{T="ClutterSystem"; M=@("LateUpdate","UpdateGrass","Awake","Update")},
  @{T="ParticleMist"; M=@("Update")},
  @{T="Smoke"; M=@("CustomUpdate","Update")},
  @{T="SmokeSpawner"; M=@("Update","FixedUpdate","CustomUpdate")},
  @{T="BaseAI"; M=@("UpdateAI","CustomFixedUpdate","FixedUpdate","Update")},
  @{T="ZoneSystem"; M=@("Update")},
  @{T="ZNetScene"; M=@("Update","CreateDestroyObjects")},
  @{T="Heightmap"; M=@("LateUpdate","CustomLateUpdate","Update")},
  @{T="EnvMan"; M=@("Update","FixedUpdate","StaticUpdate")},
  @{T="SpawnSystem"; M=@("UpdateSpawning","Update","FixedUpdate")}
)

$out = New-Object System.Collections.Generic.List[string]
$out.Add("type,method,il_instructions,locals,has_body,is_public")

foreach ($t in $targets) {
  $type = $asm.MainModule.Types | Where-Object { $_.Name -eq $t.T } | Select-Object -First 1
  if (-not $type) { $out.Add("$($t.T),MISSING,,,," ); continue }
  foreach ($mname in $t.M) {
    $methods = $type.Methods | Where-Object { $_.Name -eq $mname }
    foreach ($m in $methods) {
      $il = 0; $locals = 0; $body = $false
      if ($m.HasBody) {
        $body = $true
        $il = $m.Body.Instructions.Count
        if ($m.Body.HasVariables) { $locals = $m.Body.Variables.Count }
      }
      $out.Add(("$($t.T),$($m.Name),$il,$locals,$body,$($m.IsPublic)"))
    }
  }
  # fields of interest
  $fields = $type.Fields | Where-Object { $_.Name -match "amount|distance|density|scale|interval|update|smoke|clutter|grass|view|lod" } | ForEach-Object { $_.Name + ":" + $_.FieldType.Name }
  if ($fields) { $out.Add("#FIELDS $($t.T): " + ($fields -join ", ")) }
}

$path = "D:\C++\120fpsvalheim\profile\cecil_hotspots.csv"
$out | Set-Content $path -Encoding UTF8
Get-Content $path

# Also dump ClutterSystem / Smoke / BaseAI method list with IL sizes sorted
$extraTypes = @("ClutterSystem","Smoke","ParticleMist","BaseAI","ZNetScene","ZoneSystem","EnvMan")
$rank = New-Object System.Collections.Generic.List[object]
foreach ($tn in $extraTypes) {
  $type = $asm.MainModule.Types | Where-Object { $_.Name -eq $tn } | Select-Object -First 1
  if (-not $type) { continue }
  foreach ($m in $type.Methods) {
    if (-not $m.HasBody) { continue }
    $rank.Add([pscustomobject]@{Type=$tn; Method=$m.Name; IL=$m.Body.Instructions.Count})
  }
}
"`n=== LARGEST METHODS (IL) ==="
$rank | Sort-Object IL -Descending | Select-Object -First 40 | Format-Table -AutoSize | Out-String -Width 200

$asm.Dispose()
