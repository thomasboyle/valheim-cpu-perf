Add-Type -Path "D:\Steam Library\steamapps\common\Valheim\BepInEx\core\Mono.Cecil.dll"
$asmPath = "D:\Steam Library\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll"
$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory((Split-Path $asmPath))
$rp = New-Object Mono.Cecil.ReaderParameters; $rp.AssemblyResolver = $resolver
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($asmPath, $rp)
foreach ($tn in @("ClutterSystem","Smoke","ParticleMist","BaseAI","EnvMan","ZoneSystem")) {
  $t = $asm.MainModule.Types | ? Name -eq $tn | Select -First 1
  if (-not $t) { continue }
  "==== $tn fields ===="
  $t.Fields | % { "  $($_.Attributes) $($_.FieldType.Name) $($_.Name)" } | Select -First 40
}
$asm.Dispose()
