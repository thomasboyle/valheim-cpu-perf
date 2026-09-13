# Renderer pass 0.7.0 â€” structural GPU hooks (not QualitySettings)

Date: 2026-09-13 (Europe/London). Shipping plugin **0.7.0** keeps all **0.5.1 CPU** Harmony gates and **replaces** the 0.6.0 QualitySettings / graphics-menu caps with five **structural** patches on Valheim managed render paths.

## How this differs from 0.6.0

0.6.0 was rejected: "Those are just config changes, we need deeper renderer improvements to the code."

| 0.6.0 (removed) | 0.7.0 (shipped) |
|-----------------|-----------------|
| `QualitySettings.shadowDistance / cascades / resolution` | Per-instance `Light.shadows = None` on distant `LightLod`; `MeshRenderer.shadowCastingMode = Off` on distant Heightmap LOD |
| `QualitySettings.softParticles = false` | Clamp `ParticleMist.Emit(toEmit)` + skip distant `MisterEmit` + disable `_SOFTPARTICLES_ON` on the mist renderer + pause far particle systems |
| `QualitySettings.pixelLightCount` + `LightLod.m_lightLimit / m_shadowLimit` | No static limit writes. Shadows dropped per light by distance in `UpdateLights` postfix |
| `CameraEffects.SetSSAO(0)` / `SetSunShafts(false)` | `AmplifyOcclusionEffect.OnEnable/Update`: SampleCount=Low, Downsample, then `enabled=false` on the effect object. SunShafts behaviour disabled by type, not the settings wrapper |
| `ClutterSystem.m_distance / m_amountScale / m_quality` field caps | Prefix `GeneratePatch` + `GenerateVegPatch` â€” skip outer patches so the spawn/mesh-build loop never runs |

BepInEx cannot rewrite Unity native GfxDevice. "Deeper" here means **managed** cameras, lights, MeshRenderers, particle systems, reflection probes, clutter mesh build, OnPreRender command buffers.

## Baseline (still 0.5.1 / pre-0.7 GPU-bound)

Capture: `profile/gpu_pass_baseline_20260913_200152.csv` (~40 s).

| Metric | Value |
|--------|-------|
| PresentMon FPS avg / p50 | **77.3 / 75.1** |
| MsBetweenPresents avg | 15.8 ms |
| MsGPUBusy avg / p50 | **14.9 / 14.9** (â‰ˆ full frame â€” GPU bound) |
| nvidia-smi GPU util | **avg 94.8%** |
| VRAM | ~3574 MiB / 8192 |

Need ~8.3 ms/frame for 120 FPS. These patches cut managed GPU work (shadow maps, AO CB, probe cubemaps, mist fill, grass instances). They will **not** guarantee 120 on a 1070 Ti.

## Exactly 5 structural bottlenecks (IL-confirmed)

### 1. LightLod + Heightmap shadow casters
- **IL:** `LightLod.Awake` caches `m_light` / `m_baseShadowStrength`; `UpdateLights` (96 IL, 1 Hz) sorts `m_lights` and writes `m_lightPrio`; iterator `UpdateLoop` (252 IL) applies range + `Light.set_shadows`. `Heightmap.UpdateShadowSettings` writes `MeshRenderer.shadowCastingMode` from `GraphicsSettingsState.m_distantShadows`.
- **Why GPU:** each realtime shadow-casting point light + distant terrain shadow maps fill the 1070 Ti. PresentMon GPUBusy â‰ˆ frame outdoors.
- **Not a QualitySettings cap:** we do not write `shadowDistance`.

### 2. ReflectionUpdate dual probes (+ extra cameras)
- **IL:** `ReflectionUpdate` has `m_probe1` / `m_probe2`, `Update` (120 IL), `UpdateReflection` (34 IL). `DepthCamera` has its own `Camera` + `RenderTexture`. `GameCamera.m_skyCamera` is the legitimate second cam (left alone).
- **Why GPU:** realtime cubemap + extra camera renders are full extra views.

### 3. ParticleMist emit / distant particle fill
- **IL:** `ParticleMist.Update` (241 IL) accumulates 0.1 s then calls `Emit` (90 IL) and `MisterEmit` (152 IL) with `m_emissionMax` / `m_localEmission`. `m_ps` is the ParticleSystem.
- **Why GPU:** mist/smoke/surf overdraw. Soft particles are a material keyword, not only the global QualitySettings flag.

### 4. ClutterSystem.GenerateVegPatch spawn loop
- **IL:** `GeneratePatch` DistanceXZ-gates against `m_distance` (default 40) then calls `GenerateVegPatch`. That method (450+ IL) loops `m_clutter`, sets `V_11 = m_amount [/2 or /4 by quality] * m_amountScale`, then a Random-point Instantiate loop.
- **Why GPU:** grass/clutter alpha overdraw. Skipping the **method** is deeper than multiplying `m_amountScale` once after `ApplySettings`.

### 5. AmplifyOcclusionEffect command-buffer pass
- **IL:** Public fields `SampleCount` (Low/Med/High/VeryHigh), `Downsample`, `BlurEnabled`, `FilterEnabled`, `FilterDownsample`, `Intensity`. `OnPreRender` (233 IL) + `commandBuffer_FillComputeOcclusion` (363 IL) + temporal RTs. `CameraEffects.SetSSAO` only `enabled` + those fields (the 0.6 wrapper).
- **Why GPU:** fullscreen AO on an already saturated GPU.

## Exactly 5 code fixes

| # | Fix | Hook | Stability |
|---|-----|------|-----------|
| 1 | Distant `Light.shadows = None`; distant Heightmap `shadowCastingMode = Off` | Postfix `LightLod.UpdateLights`; postfix `Heightmap.UpdateShadowSettings` | Hysteresis 28 m / 22 m; cache instance ids; vanilla UpdateLoop may restore Soft inside 22 m |
| 2 | Skip `ReflectionUpdate.Update`; disable probes; disable Depth/Reflect extra cameras | Prefix `ReflectionUpdate.Update`; 30-frame `Camera.allCameras` scan | Sky + main cameras untouched; inventory preview not matched by name |
| 3 | Clamp `Emit(toEmitâ‰¤6)`; skip distant `MisterEmit`; pause far ParticleSystems; kill `_SOFTPARTICLES_ON` on mist renderer | Prefix `Emit` / `MisterEmit`; postfix `Awake`; 30-frame scan | Pause/Play hysteresis 48 / 40 m; no MP/ZDO |
| 4 | Skip `GeneratePatch` / `GenerateVegPatch` beyond 24 m; checkerboard-skip odd patches beyond 14 m | Prefix both methods | `GeneratePatch` already treats null PatchData as skip; no `m_amountScale` write |
| 5 | `AmplifyOcclusionEffect`: Low + Downsample + `enabled=false`; prefix `Update` keeps it off; disable SunShafts behaviour by type | Postfix `OnEnable` + `CameraEffects.ApplySettings`; prefix `Update` | Re-assert after vanilla `SetSSAO` without calling that API |

## What happened to the 0.6 QualitySettings layer

**Removed from shipping.** `Patches_Gpu.cs` no longer writes `QualitySettings.*`, `LightLod.m_lightLimit`, `LightLod.m_shadowLimit`, `ClutterSystem.m_distance` / `m_amountScale` / `m_quality`, or `CameraEffects.SetSSAO` / `SetSunShafts`.

PlayerPrefs were never written in 0.6 either. Removing the DLL still restores vanilla.

## Honest 120 FPS outlook (GTX 1070 Ti)

- Baseline ~77 FPS, GPU fully busy (~15 ms). 120 needs ~8.3 ms.
- These hooks cut the largest **managed** multipliers (point-light shadows, AO CB, probe cubemaps, mist emit, outer grass build). Native GfxDevice, resolution, water shader, clouds, and character mesh count remain.
- Realistic band after restart: high-80s to ~100 in lighter biomes if AO + shadows + mist were the tax; heavy bases / Ashlands / thick mist can stay GPU-bound. **120 is not guaranteed.**

## Verify (post-restart 0.7.0) — 2026-09-13 ~20:39 Europe/London

**Loaded:** BepInEx `Loading [ValheimCpuPerf 0.7.0]`; banner confirms **structural** renderer patches (LightLod / Heightmap shadows, ReflectionUpdate skip, ParticleMist, Clutter GeneratePatch/VegPatch, AmplifyOcclusion disable). **No** 0.6 QualitySettings / graphics-menu caps. AmplifyOcclusion + ReflectionUpdate applied at runtime. In-world: `Spawned after 8.0s`, Black Forest music. **No** Error / Exception / NullReference spam from 0.7 during session.

**Capture:** settle ~10 s, PresentMon ~40 s (`--v2_metrics`) + concurrent nvidia-smi 1 Hz + short CPU process sample.

| Artifact | Path |
|----------|------|
| PresentMon | `profile/gpu_pass_post_0_7_20260913_203934.csv` (3198 frames) |
| nvidia-smi | `profile/nvidia_smi_post_0_7_20260913_203934.csv` |
| CPU sample | `profile/cpu_cores_post_0_7_20260913_203934.csv` |
| Summary | `profile/gpu_pass_post_0_7_SUMMARY.txt` |

### Results vs baseline (0.5.1 / pre-0.7 GPU pass)

| Metric | Baseline 0.5.1 | Post 0.7.0 | Delta |
|--------|----------------|------------|-------|
| FPS avg | **77.3** | **102.3** | **+25.0** |
| FPS p50 | **75.1** | **96.2** | **+21.1** |
| MsBetweenPresents avg | 15.8 | 12.5 | −3.3 |
| MsGPUBusy avg / p50 | **14.9 / 14.9** | **10.4 / 10.4** | **−4.5** |
| nvidia-smi GPU util | **94.8%** | **82.9%** (66–93) | **−11.9 pp** |
| VRAM | ~3574 MiB | ~3501 MiB | ~−70 |

CPU process sample during capture: ~327% avg (multi-core; context only — not a WPA stack capture).

### Honest 120 FPS note

Need ~**8.3 ms**/frame for 120. Post-0.7 MsGPUBusy is still ~**10.4 ms** (FPS avg ~102, p50 ~96). Structural hooks clearly reduced GPU work (busy −4.5 ms, util −12 pp, FPS +25), but the 1070 Ti remains **GPU-bound** outdoors in Black Forest. **120 is not reached** on this pass; remaining headroom is native GfxDevice / resolution / water / clouds / residual shadows & overdraw — not more QualitySettings caps.

No new code fixes applied (no crash / null spam).

## 0.7.1 hotfix — white bushes / washed foliage (2026-09-13)

**Report:** after 0.7.0, bushes looked very white / weird lighting.

**Cause (high confidence):**
1. `AmplifyOcclusionEffect` was forced `enabled=false` (and Update kept it off) → foliage lost contact AO → washed/white.
2. `ReflectionUpdate` prefix-skipped Update and forced probes Custom/disabled → ambient/specular blow-out on vegetation.

**Fix (shipped in 0.7.1):**
- **AO:** keep the component **ENABLED**. Apply only cheap settings: `SampleCount=Low`, `Downsample=true`, `FilterDownsample=true`, `BlurEnabled=false`, Intensity cap ~0.45. Removed `ao.enabled = false` and removed `Gpu_AmplifyOcclusionStayOff`. `CameraEffects.ApplySettings` postfix re-applies cheap settings but does **not** disable the effect.
- **Reflections:** deleted `Gpu_ReflectionUpdateSkip` — probes run vanilla again (lighting correctness over that GPU win). Extra Depth/Reflect cameras still disabled via `RendererScan`.
- **Kept from 0.7.0:** LightLod distant shadows, Heightmap distant shadow Off, ParticleMist clamps, Clutter early-out, distant particle pause, extra cam scan.
- **Not restored:** 0.6 QualitySettings layer.

**Action for player:** replace DLL and **restart Valheim** to see the bush lighting fix.
