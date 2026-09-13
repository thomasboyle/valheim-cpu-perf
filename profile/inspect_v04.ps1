$ErrorActionPreference = "Stop"
$cecil = "D:\Steam Library\steamapps\common\Valheim\BepInEx\core\Mono.Cecil.dll"
if (-not (Test-Path $cecil)) { $cecil = "D:\C++\120fpsvalheim\_bepinex_dl\extract\BepInExPack_Valheim\BepInEx\core\Mono.Cecil.dll" }
Add-Type -Path $cecil
$asmPath = "D:\Steam Library\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll"
$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory((Split-Path $asmPath))
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.AssemblyResolver = $resolver
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($asmPath, $rp)
$outPath = "D:\C++\120fpsvalheim\profile\inspect_v04_out.txt"
$sb = New-Object System.Text.StringBuilder

function Dump-TypeInfo($typeName) {
  $type = $asm.MainModule.Types | Where-Object { $_.Name -eq $typeName } | Select-Object -First 1
  if (-not $type) { [void]$sb.AppendLine("MISSING TYPE $typeName"); return $null }
  [void]$sb.AppendLine("`n========== $typeName FIELDS ==========")
  foreach ($f in $type.Fields) {
    $c = ""
    if ($f.HasConstant) { $c = " = $($f.Constant)" }
    [void]$sb.AppendLine("  $($f.Attributes) $($f.FieldType.Name) $($f.Name)$c")
  }
  [void]$sb.AppendLine("`n========== $typeName METHODS (body sizes) ==========")
  $type.Methods | Where-Object { $_.HasBody } | Sort-Object { $_.Body.Instructions.Count } -Descending | ForEach-Object {
    [void]$sb.AppendLine(("  {0} IL={1} pub={2} params=[{3}]" -f $_.Name, $_.Body.Instructions.Count, $_.IsPublic, (($_.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ",")))
  }
  return $type
}

function Dump-Method($typeName, $methodName, $maxIns=250) {
  $type = $asm.MainModule.Types | Where-Object { $_.Name -eq $typeName } | Select-Object -First 1
  if (-not $type) { [void]$sb.AppendLine("MISSING $typeName"); return }
  $methods = $type.Methods | Where-Object { $_.Name -eq $methodName }
  foreach ($m in $methods) {
    [void]$sb.AppendLine("`n========== $typeName.$methodName IL=$($m.Body.Instructions.Count) ==========")
    [void]$sb.AppendLine("Params: $(($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', ')")
    if ($m.Body.HasVariables) {
      [void]$sb.AppendLine("Locals: $(($m.Body.Variables | ForEach-Object { $_.VariableType.Name }) -join ', ')")
    }
    $i = 0
    foreach ($ins in $m.Body.Instructions) {
      [void]$sb.AppendLine(("{0,4}: {1}" -f $i, $ins.ToString()))
      $i++
      if ($i -ge $maxIns) { [void]$sb.AppendLine("  ... truncated ..."); break }
    }
  }
}

Dump-TypeInfo "Smoke" | Out-Null
Dump-Method "Smoke" "CustomUpdate" 200
Dump-Method "Smoke" "Awake" 80
Dump-Method "Smoke" "Start" 80
Dump-Method "Smoke" "OnEnable" 80

Dump-TypeInfo "Fish" | Out-Null
Dump-Method "Fish" "CustomFixedUpdate" 250
Dump-Method "Fish" "Awake" 100

Dump-TypeInfo "StaticPhysics" | Out-Null
Dump-Method "StaticPhysics" "SUpdate" 200
Dump-Method "StaticPhysics" "Awake" 80
Dump-Method "StaticPhysics" "Start" 80

Dump-TypeInfo "EffectArea" | Out-Null
Dump-Method "EffectArea" "CustomFixedUpdate" 150

Dump-TypeInfo "RandomFlyingBird" | Out-Null
Dump-Method "RandomFlyingBird" "CustomFixedUpdate" 150

Dump-TypeInfo "CharacterAnimEvent" | Out-Null
Dump-Method "CharacterAnimEvent" "CustomLateUpdate" 150

Dump-TypeInfo "SmokeSpawner" | Out-Null
Dump-Method "SmokeSpawner" "CustomUpdate" 120

Dump-TypeInfo "ZNetScene" | Out-Null
Dump-Method "ZNetScene" "CreateObjects" 200
Dump-Method "ZNetScene" "RemoveObjects" 200
Dump-Method "ZNetScene" "CreateObject" 150

# constants for ZNetScene
$t = $asm.MainModule.Types | Where-Object { $_.Name -eq "ZNetScene" } | Select-Object -First 1
[void]$sb.AppendLine("`nZNetScene const fields:")
foreach ($f in $t.Fields) {
  if ($f.HasConstant) { [void]$sb.AppendLine("  $($f.Name) = $($f.Constant)") }
}

# Look for MonoUpdaters FixedUpdate that calls these
Dump-TypeInfo "MonoUpdaters" | Out-Null
Dump-Method "MonoUpdaters" "FixedUpdate" 120
Dump-Method "MonoUpdaters" "Update" 120
Dump-Method "MonoUpdaters" "LateUpdate" 120

# Humanoid / Character briefly - just method sizes and early checks
Dump-TypeInfo "Humanoid" | Out-Null
Dump-Method "Humanoid" "CustomFixedUpdate" 80
Dump-TypeInfo "Character" | Out-Null
Dump-Method "Character" "CustomFixedUpdate" 100

# VisEquipment
Dump-TypeInfo "VisEquipment" | Out-Null
Dump-Method "VisEquipment" "CustomUpdate" 100

# FootStep
Dump-TypeInfo "FootStep" | Out-Null
Dump-Method "FootStep" "CustomUpdate" 100

# WearNTear UpdateWear
Dump-TypeInfo "WearNTear" | Out-Null
Dump-Method "WearNTear" "UpdateWear" 120

# CraftingStation
Dump-TypeInfo "CraftingStation" | Out-Null
Dump-Method "CraftingStation" "CustomUpdate" 80

# Heightmap CustomLateUpdate
Dump-TypeInfo "Heightmap" | Out-Null
Dump-Method "Heightmap" "CustomLateUpdate" 80

[System.IO.File]::WriteAllText($outPath, $sb.ToString())
Write-Output "Wrote $outPath length=$($sb.Length)"
$asm.Dispose()
