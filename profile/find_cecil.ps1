# Inspect assembly_valheim for hotspot method sizes / field names via reflection load
Add-Type -Path "D:\Steam Library\steamapps\common\Valheim\valheim_Data\Managed\Mono.Cecil.dll" -ErrorAction SilentlyContinue
$cecilPath = Get-ChildItem "D:\Steam Library\steamapps\common\Valheim" -Recurse -Filter "Mono.Cecil.dll" -ErrorAction SilentlyContinue | Select-Object -First 3 -ExpandProperty FullName
$cecilPath
$nugetCecil = "$env:USERPROFILE\.nuget\packages"
Get-ChildItem $nugetCecil -Recurse -Filter "Mono.Cecil.dll" -ErrorAction SilentlyContinue | Select-Object -First 5 FullName
# Also check BepInEx for cecil
Get-ChildItem "D:\Steam Library\steamapps\common\Valheim\BepInEx" -Recurse -Filter "*Cecil*" -ErrorAction SilentlyContinue | Select-Object FullName
Get-ChildItem "D:\C++\120fpsvalheim" -Recurse -Filter "*Cecil*" -ErrorAction SilentlyContinue | Select-Object FullName
