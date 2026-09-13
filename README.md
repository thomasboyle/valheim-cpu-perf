# ValheimCpuPerf

Open-source **performance** mod for Valheim (BepInEx 5 / HarmonyX).  
**Not a cheat mod** — no godmode, damage, stamina, teleport, or item exploits.

Goal: deeply profile CPU-bound systems, report the **top 3 bottlenecks** against an **8.33 ms** frame budget (~120 FPS when CPU-bound), and offer **configurable mitigations** (throttle / density / distance).

**Honest disclaimer:** this does **not** guarantee 120 FPS. GPU limits, sync, and uncapped vs VSync settings still apply.

## Requirements

- Valheim (Steam)
- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) (denikson)

Default install path expected by `Environment.props`:

`D:\Steam Library\steamapps\common\Valheim`

## Build

```bat
cd /d D:\C++\120fpsvalheim
dotnet build ValheimCpuPerf\ValheimCpuPerf.csproj -c Release
```

On success the DLL is copied to `BepInEx\plugins\ValheimCpuPerf.dll`.

Edit `Environment.props` if your Valheim path differs.

## In-game profiling

1. Launch Valheim with Doorstop/BepInEx (normal Steam launch if `winhttp.dll` + `doorstop_config.ini` are present).
2. Enter the world (profiling patches need runtime systems).
3. **F8** — dump top 3 CPU bottlenecks to `BepInEx/LogOutput.log` (and console).
4. **F9** — toggle on-screen overlay.

Instrumented systems (verified against `assembly_valheim.dll` where possible):

- `ZoneSystem.Update`
- `ZNetScene.Update` / `CreateDestroyObjects`
- `ClutterSystem.LateUpdate + UpdateGrass`
- `Smoke.CustomUpdate` / `ParticleMist.Update`
- `BaseAI.UpdateAI`
- `Heightmap.LateUpdate` (if present)
- `EnvMan.Update` / `SpawnSystem.UpdateSpawning` (if present)

## Mitigations

Off by default (`EnableMitigations = false`) so first dumps are honest.

Config file (created on first run):

`BepInEx/config/com.thomasboyle.valheimcpuperf.cfg`

| Option | Effect |
|--------|--------|
| `ClutterDensityMultiplier` | Scale grass/clutter amount |
| `ClutterDistanceMultiplier` | Scale clutter distance |
| `ThrottleParticleMist` / `ParticleMistSkipFrames` | Skip mist updates some frames |
| `ThrottleSmoke` / `SmokeSkipFrames` | Skip smoke updates some frames |
| `ThrottleDistantAI` / `DistantAIDistance` / `DistantAISkipFrames` | Run far creature AI less often |
| `ReduceShadowDistance` / `ShadowDistanceMeters` | Cap Unity shadow distance |
| `ThrottleZNetSceneCreates` | Soft-limit create/destroy spikes |

None of these alter combat math, inventory, or exploration unlocks.

## Workflow

1. Profile with mitigations **off** → F8 dump → note top 3.
2. Enable only mitigations that match those hotspots.
3. Re-profile; aim for lower avg ms/call on those buckets vs 8.33 ms.

## License

MIT
