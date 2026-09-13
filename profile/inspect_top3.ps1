$ErrorActionPreference = "Stop"
$cecil = "D:\Steam Library\steamapps\common\Valheim\BepInEx\core\Mono.Cecil.dll"
Add-Type -Path $cecil
$asmPath = "D:\Steam Library\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll"
$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory((Split-Path $asmPath))
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.AssemblyResolver = $resolver
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($asmPath, $rp)

function Dump-Method($typeName, $methodName) {
  $type = $asm.MainModule.Types | Where-Object { $_.Name -eq $typeName } | Select-Object -First 1
  if (-not $type) { Write-Output "MISSING TYPE $typeName"; return }
  Write-Output "`n========== $typeName fields =========="
  $type.Fields | ForEach-Object { "  $($_.Attributes) $($_.FieldType.Name) $($_.Name)" }
  $methods = $type.Methods | Where-Object { $_.Name -eq $methodName }
  foreach ($m in $methods) {
    Write-Output "`n========== $typeName.$methodName IL=$($m.Body.Instructions.Count) =========="
    Write-Output "Params: $(($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', ')"
    if ($m.Body.HasVariables) {
      Write-Output "Locals: $(($m.Body.Variables | ForEach-Object { $_.VariableType.Name }) -join ', ')"
    }
    $i = 0
    foreach ($ins in $m.Body.Instructions) {
      Write-Output ("{0,4}: {1}" -f $i, $ins.ToString())
      $i++
      if ($i -gt 120) { Write-Output "  ... truncated ..."; break }
    }
  }
}

Dump-Method "ZSyncTransform" "CustomFixedUpdate"
Dump-Method "ZSyncTransform" "Awake"
Dump-Method "WaterVolume" "UpdateFloaters"
Dump-Method "ZNetScene" "Update"
Dump-Method "ZNetScene" "CreateDestroyObjects"

$asm.Dispose()
