# ValheimCpuPerf

Open-source **performance** mod for Valheim (BepInEx 5 / HarmonyX).  
**Not a cheat mod** — no godmode, damage, stamina, teleport, or item exploits.

## Approach

1. **Profile** with a temporary in-process Harmony sampler (`ValheimCpuPerf.Profile`) and/or external WPR — see [`docs/MANAGED_HOTSPOTS.md`](docs/MANAGED_HOTSPOTS.md) and [`docs/EXTERNAL_PROFILE.md`](docs/EXTERNAL_PROFILE.md).
2. **Bake only definitive fixes** for measured CPU + GPU bottlenecks into the shipping plugin.
3. **Restart Valheim** after replacing `ValheimCpuPerf.dll` so BepInEx loads the new assembly.

There is **no** in-game F8/F9 profiler overlay in the shipping DLL and **no** mitigation config toggles.

**Honest disclaimer:** this does **not** guarantee 120 FPS. GPU limits, sync, and uncapped vs VSync settings still apply.

## Always-on core tweaks (v0.8.1)

### CPU (kept from v0.5.1 + 0.8.0 ZSync rewrite)

| Rank | Bottleneck | Baked behaviour | Confidence |
|------|------------|-----------------|------------|
| 1 | ZSyncTransform.CustomFixedUpdate | Owner skip; distant static 1/3 / 1/6; **distant characters >80 m 1/3**; projectiles full-rate | **Definitive** |
| — | WaterVolume.UpdateFloaters | Skip when >48 m from local player | **Definitive** |
| — | Smoke.CustomUpdate / Fish.CustomFixedUpdate | Distant smoke timer-only; Fish non-owner early-out | **Definitive** |
| — | Character / Humanoid.CustomFixedUpdate | Distant (>64 m) non-owner: SetVisible only | **Definitive** |
| — | ZNetScene.CreateDestroyObjects | *No shipped fix* (MP pop-in risk) | Documented only |

### GPU (v0.8.1 — Tier A/B + flash fix)

See [docs/REWRITE_0_8.md](docs/REWRITE_0_8.md) and [docs/RENDERER_PASS_0_7.md](docs/RENDERER_PASS_0_7.md).

0.7.1 MEASURED baseline (GTX 1070 Ti): FPS **87.1/85.1**, MsGPUBusy **13.6**, util **94.2%**, CPU **3.63**.

| # | Bottleneck | Baked behaviour | Visual tradeoff |
|---|------------|-----------------|-----------------|
| A1 | AmplifyOcclusionEffect | ENABLED cheap + OnPreRender **every frame** (0.8.1) | Softer AO; **no white bushes** |
| A2 | ZSync character sync | Distant characters 1/3 | Distant remote motion slightly choppier |
| A3 | Soft shadows + veg cast | Closest 3 Soft lights; veg MeshRenderer Off | Fewer soft maps; grass casts no shadow |
| B4 | ClutterSystem.GenerateVegPatch | Per-call amountScale thin + early-out | Less grass density mid-ring |
| B5 | ReflectionUpdate | **VANILLA (0.8.1)** — no interval/res/IndividualFaces patch | Fixes ~3s foliage white flash from 0.8.0 |
| B6 | Water / extra cams | Surface shadow Off; disable Reflect/Water cams | Flatter water reflections if cams existed |

No `QualitySettings.*` writes. No `SetSSAO(0)`. Probes never forced Custom/disabled.
**0.8.1 flash fix:** 0.8.0 ReflectionUpdate rewrite (interval >=2.5 s / res 128 / IndividualFaces) caused vegetation ambient/specular to flash white every ~2.5-3 s. Probes restored to fully vanilla (like 0.7.1). Extra Depth/Reflect camera disable kept.


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
