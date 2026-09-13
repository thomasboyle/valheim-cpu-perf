# ValheimCpuPerf

Open-source **performance** mod for Valheim (BepInEx 5 / HarmonyX).  
**Not a cheat mod** — no godmode, damage, stamina, teleport, or item exploits.

## Approach

1. **Profile** with a temporary in-process Harmony sampler (`ValheimCpuPerf.Profile`) and/or external WPR — see [`docs/MANAGED_HOTSPOTS.md`](docs/MANAGED_HOTSPOTS.md) and [`docs/EXTERNAL_PROFILE.md`](docs/EXTERNAL_PROFILE.md).
2. **Bake only definitive fixes** for measured CPU + GPU bottlenecks into the shipping plugin.
3. **Restart Valheim** after replacing `ValheimCpuPerf.dll` so BepInEx loads the new assembly.

There is **no** in-game F8/F9 profiler overlay in the shipping DLL and **no** mitigation config toggles.

**Honest disclaimer:** this does **not** guarantee 120 FPS. GPU limits, sync, and uncapped vs VSync settings still apply.

## Always-on core tweaks (v0.7.0)

### CPU (kept from v0.5.1)

From **live** managed sampling 2026-09-13 + post-0.4 deep profile:

| Rank | Bottleneck | Baked behaviour | Confidence |
|------|------------|-----------------|------------|
| 1 | ZSyncTransform.CustomFixedUpdate | Owner skip + distant 1/3 / very-distant 1/6 client sync | **Definitive** |
| — | WaterVolume.UpdateFloaters | Skip when >48 m from local player | **Definitive** |
| — | Smoke.CustomUpdate / Fish.CustomFixedUpdate | Distant smoke timer-only; Fish non-owner early-out | **Definitive** |
| — | Character / Humanoid.CustomFixedUpdate | Distant (>64 m) non-owner: SetVisible only | **Definitive** |
| — | ZNetScene.CreateDestroyObjects | *No shipped fix* (MP pop-in risk) | Documented only |

### GPU (v0.7.0 — structural renderer patches)

0.6.0 QualitySettings / graphics-menu caps were **removed**. 0.7.0 patches actual render code paths (Mono.Cecil-mapped). See [docs/RENDERER_PASS_0_7.md](docs/RENDERER_PASS_0_7.md).

PresentMon + nvidia-smi on GTX 1070 Ti (~77 FPS avg, MsGPUBusy≈frame, util ~95%).

| # | Bottleneck | Baked behaviour | Visual tradeoff |
|---|------------|-----------------|-----------------|
| 1 | LightLod + Heightmap shadow casters | Distant lights `Light.shadows=None` (28 m); distant Heightmap LOD `shadowCastingMode=Off` | No point-light / distant-terrain shadows far away |
| 2 | ReflectionUpdate + extra cameras | Prefix-skip probe Update; disable Depth/Reflect extra cams | Flatter env reflections; no extra probe views |
| 3 | ParticleMist / distant particles | Clamp Emit(toEmit≤6); skip distant MisterEmit; pause far systems; kill soft-particle keyword on mist | Thinner mist; harder particle edges |
| 4 | ClutterSystem.GenerateVegPatch | Skip patch build >24 m; checkerboard-skip odd patches >14 m | Less grass in the outer ring |
| 5 | AmplifyOcclusionEffect | Disable the AO behaviour (Low/Downsample first); disable SunShafts component | No SSAO / shafts |

No `QualitySettings.*` writes. No `SetSSAO(0)` / clutter `m_amountScale` field caps.

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
