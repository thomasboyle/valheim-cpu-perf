$ErrorActionPreference = "Stop"
$cecil = "D:\Steam Library\steamapps\common\Valheim\BepInEx\core\Mono.Cecil.dll"
Add-Type -Path $cecil
$asmPath = "D:\Steam Library\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll"
$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory((Split-Path $asmPath))
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.AssemblyResolver = $resolver
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($asmPath, $rp)

function Dump-MethodFull($typeName, $methodName) {
  $type = $asm.MainModule.Types | Where-Object { $_.Name -eq $typeName } | Select-Object -First 1
  $methods = $type.Methods | Where-Object { $_.Name -eq $methodName }
  foreach ($m in $methods) {
    Write-Output "`n========== $typeName.$methodName IL=$($m.Body.Instructions.Count) pub=$($m.IsPublic) =========="
    Write-Output "Params: $(($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', ')"
    $i = 0
    foreach ($ins in $m.Body.Instructions) {
      Write-Output ("{0,4}: {1}" -f $i, $ins.ToString())
      $i++
    }
  }
}

Dump-MethodFull "ZSyncTransform" "ClientSync"
# also list all ZSyncTransform methods with IL sizes
$t = $asm.MainModule.Types | Where-Object { $_.Name -eq "ZSyncTransform" } | Select-Object -First 1
Write-Output "`n=== ALL ZSyncTransform methods ==="
$t.Methods | Where-Object { $_.HasBody } | Sort-Object { $_.Body.Instructions.Count } -Descending | ForEach-Object {
  "{0} IL={1} pub={2}" -f $_.Name, $_.Body.Instructions.Count, $_.IsPublic
}

# Who calls CustomFixedUpdate - look at IMonoUpdater or similar
Write-Output "`n=== Types with CustomFixedUpdate dispatcher hints ==="
foreach ($tn in @("ZInput","MonoUpdater","IMonoUpdater","ZNetView","Game","Time")) {
  $ty = $asm.MainModule.Types | Where-Object { $_.Name -eq $tn } | Select-Object -First 1
  if ($ty) { "FOUND $tn methods=$($ty.Methods.Count)" }
}

# Search for references to ZSyncTransform Instances
Write-Output "`n=== Search CustomFixedUpdate callers by string in method bodies (sample) ==="
$hits = 0
foreach ($ty in $asm.MainModule.Types) {
  foreach ($m in $ty.Methods) {
    if (-not $m.HasBody) { continue }
    foreach ($ins in $m.Body.Instructions) {
      if ($ins.Operand -and $ins.Operand.ToString() -match "ZSyncTransform") {
        Write-Output "$($ty.Name).$($m.Name): $($ins.OpCode) $($ins.Operand)"
        $hits++
        if ($hits -gt 40) { break }
      }
    }
    if ($hits -gt 40) { break }
  }
  if ($hits -gt 40) { break }
}

# WaterVolume GetWaterSurface size
Dump-MethodFull "WaterVolume" "GetWaterSurface"
# Smoke too for secondary
$t2 = $asm.MainModule.Types | Where-Object { $_.Name -eq "Smoke" } | Select-Object -First 1
Write-Output "`nSmoke fields:"; $t2.Fields | ForEach-Object { "  $($_.FieldType.Name) $($_.Name)" }

$asm.Dispose()
