# ValheimCpuPerf

Open-source **performance** mod for Valheim (BepInEx 5 / HarmonyX).  
**Not a cheat mod** — no godmode, damage, stamina, teleport, or item exploits.

## Approach

1. **Profile** with a temporary in-process Harmony sampler (`ValheimCpuPerf.Profile`) and/or external WPR — see [`docs/MANAGED_HOTSPOTS.md`](docs/MANAGED_HOTSPOTS.md) and [`docs/EXTERNAL_PROFILE.md`](docs/EXTERNAL_PROFILE.md).
2. **Bake only definitive fixes** for the measured top CPU bottlenecks into the shipping plugin.
3. **Restart Valheim** after replacing `ValheimCpuPerf.dll` so BepInEx loads the new assembly.

There is **no** in-game F8/F9 profiler overlay in the shipping DLL and **no** mitigation config toggles.

**Honest disclaimer:** this does **not** guarantee 120 FPS. GPU limits, sync, and uncapped vs VSync settings still apply.

## Always-on core tweaks (v0.3.0)

From **live** managed sampling 2026-09-13 (steady-state in-world delta):

| Rank | Bottleneck | Baked behaviour | Confidence |
|------|------------|-----------------|------------|
| 1 | `ZSyncTransform.CustomFixedUpdate` (~31% sampled) | Skip when `ZNetView.IsOwner()` (ClientSync no-op anyway). Distant non-character/non-projectile (>64 m) sync 1/3 frames. | **Definitive** |
| 2 | `ZNetScene.CreateDestroyObjects` (~9%) | *No shipped fix* — rate changes risk multiplayer pop-in. | Documented only |
| 3 | `WaterVolume.UpdateFloaters` (~8%) | Skip when closest collider point is >48 m from local player. Visual water `StaticUpdate` unchanged. | **Definitive** |

**Removed from v0.2** (not top-3 in live data): clutter scale, mist/smoke every-other-frame, distant BaseAI throttle, shadow distance soft-cap.

## Requirements

- Valheim (Steam)
- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) (denikson)

Default install path expected by `Environment.props`:

`D:\Steam Library\steamapps\common\Valheim`

## Build / deploy

```bat
cd /d D:\C++\120fpsvalheim
dotnet build ValheimCpuPerf\ValheimCpuPerf.csproj -c Release
```

On success the DLL is copied to:

`D:\Steam Library\steamapps\common\Valheim\BepInEx\plugins\ValheimCpuPerf.dll`

**You must fully restart Valheim** (quit to desktop, launch again) after a new DLL is deployed. Hot-reload is not supported.

Edit `Environment.props` if your Valheim path differs.

## Temporary managed sampler (contributors)

```bat
dotnet build ValheimCpuPerf.Profile\ValheimCpuPerf.Profile.csproj -c Release
copy /Y ValheimCpuPerf.Profile\bin\Release\ValheimCpuPerf.Profile.dll "D:\Steam Library\steamapps\common\Valheim\BepInEx\plugins\"
rem Disable shipping ValheimCpuPerf.dll while capturing, restart game, play in-world ~60-90s
rem CSVs land in profile\managed_hotspots_*.csv — then delete the Profile DLL from plugins
```

## License

MIT
