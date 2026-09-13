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
$outPath = "D:\C++\120fpsvalheim\profile\inspect_v04b_out.txt"
$sb = New-Object System.Text.StringBuilder

function Dump-Method($typeName, $methodName, $maxIns=800) {
  $type = $asm.MainModule.Types | Where-Object { $_.Name -eq $typeName } | Select-Object -First 1
  $methods = $type.Methods | Where-Object { $_.Name -eq $methodName }
  foreach ($m in $methods) {
    [void]$sb.AppendLine("`n========== $typeName.$methodName IL=$($m.Body.Instructions.Count) ==========")
    $i = 0
    foreach ($ins in $m.Body.Instructions) {
      [void]$sb.AppendLine(("{0,4}: {1}" -f $i, $ins.ToString()))
      $i++
      if ($i -ge $maxIns) { [void]$sb.AppendLine("  ... truncated ..."); break }
    }
  }
}

Dump-Method "Fish" "CustomFixedUpdate" 600
Dump-Method "Fish" "SetVisible" 50
Dump-Method "StaticPhysics" "ShouldUpdate" 40
Dump-Method "Smoke" "FadeMostDistant" 80
Dump-Method "ZSyncTransform" "ClientSync" 250
Dump-Method "RandomFlyingBird" "SetVisible" 50

# Search Fish for IsOwner / HasOwner calls in CustomFixedUpdate
$fish = $asm.MainModule.Types | Where-Object { $_.Name -eq "Fish" } | Select-Object -First 1
$m = $fish.Methods | Where-Object { $_.Name -eq "CustomFixedUpdate" } | Select-Object -First 1
[void]$sb.AppendLine("`n=== Fish.CustomFixedUpdate call sites of interest ===")
$i=0
foreach ($ins in $m.Body.Instructions) {
  $s = $ins.ToString()
  if ($s -match "IsOwner|HasOwner|SetVisible|Player|Distance|m_localPlayer|FindClosest|OutsideActive|GetZDO") {
    [void]$sb.AppendLine(("{0,4}: {1}" -f $i, $s))
  }
  $i++
}

# MonoUpdaters - how Smoke/Fish are called
$mu = $asm.MainModule.Types | Where-Object { $_.Name -eq "MonoUpdaters" } | Select-Object -First 1
[void]$sb.AppendLine("`n=== MonoUpdaters methods ===")
foreach ($mm in $mu.Methods) {
  if ($mm.HasBody) {
    [void]$sb.AppendLine("$($mm.Name) IL=$($mm.Body.Instructions.Count)")
  }
}
Dump-Method "MonoUpdaters" "FixedUpdate" 200
Dump-Method "MonoUpdaters" "Update" 200

# ZDO GetPositionDataRevision or similar
$zdo = $asm.MainModule.Types | Where-Object { $_.Name -eq "ZDO" } | Select-Object -First 1
[void]$sb.AppendLine("`n=== ZDO methods matching DataRev|Revision|Owner|Position ===")
$zdo.Methods | Where-Object { $_.Name -match "Rev|Owner|Position|Data" } | ForEach-Object {
  [void]$sb.AppendLine("  $($_.Name) IL=$(if($_.HasBody){$_.Body.Instructions.Count}else{0})")
}
[void]$sb.AppendLine("`n=== ZDO fields matching Rev|Owner ===")
$zdo.Fields | Where-Object { $_.Name -match "Rev|Owner|Data" } | ForEach-Object {
  [void]$sb.AppendLine("  $($_.FieldType.Name) $($_.Name)")
}

# ZNetScene CreateObjectsSorted - is it expensive?
Dump-Method "ZNetScene" "CreateObjectsSorted" 200
Dump-Method "ZDOMan" "FindSectorObjects" 150

# Look at Fish fields for m_nview etc missing from earlier truncate
[void]$sb.AppendLine("`n=== Fish remaining private fields ===")
$fish.Fields | Where-Object { $_.Name -match "nview|owner|water|body|visible|lod|last" } | ForEach-Object {
  [void]$sb.AppendLine("  $($_.FieldType.Name) $($_.Name)")
}

[System.IO.File]::WriteAllText($outPath, $sb.ToString())
Write-Output "Wrote $outPath length=$($sb.Length)"
$asm.Dispose()
