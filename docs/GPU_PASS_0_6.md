# GPU Pass 0.6.0 — toward 120 FPS (GTX 1070 Ti)

Date: 2026-09-13 (Europe/London). Shipping plugin **0.6.0** keeps all **0.5.1 CPU** Harmony gates and adds five **stable GPU** caps.

## Baseline (0.5.1 DLL, in-world, PresentMon + nvidia-smi)

Capture: `profile/gpu_pass_baseline_20260913_200152.csv` (~40 s), `profile/nvidia_smi_baseline_*.csv`.

| Metric | Value |
|--------|-------|
| PresentMon FPS avg / p50 | **77.3 / 75.1** |
| MsBetweenPresents avg | 15.8 ms |
| MsGPUBusy avg / p50 | **14.9 / 14.9** (≈ full frame — GPU bound) |
| nvidia-smi GPU util | **avg 94.8%** (87–99%) |
| VRAM | ~3574 MiB / 8192 |
| Clocks | ~1885 / 4455 MHz |

Conclusion: after CPU 0.5.1, the frame is **GPU over-subscribed** on a 1070 Ti. Further CPU early-outs will not reach 120 FPS without reducing GPU work.

## Exactly 5 bottlenecks (evidence)

### 1. Shadow distance + cascades + resolution
- **IL:** `GraphicsSettingsManager.ApplyQualitySettings` sets `QualitySettings.shadowCascades` to **2/3/4** and `shadowDistance` to **80 / 120 / 150** with Low/Med/High `shadowResolution`.
- **Why GPU:** cascaded shadow maps dominate fill + bandwidth on Pascal; 150 m / 4 cascades is Extreme for 1080p 1070 Ti.
- **PresentMon/smi:** GPUBusy ≈ FrameTime and util ~95% in outdoor scenes where shadows are live.

### 2. Soft particles
- **IL:** `ApplyQualitySettings` → `QualitySettings.softParticles = GraphicsSettingsState.m_softParticles`.
- **Why GPU:** soft particles sample scene depth per particle — expensive overdraw (fire, mist, surf). Known Valheim/Unity GPU killer on mid-tier cards.

### 3. Pixel lights + point-light LOD / shadows
- **IL:** `pixelLightCount` via `GetLightLimit` (2/4/8); `ApplyLightLod` sets `LightLod.m_lightLimit` to 4/15/40/-1 and `m_shadowLimit` to 0/1/3/-1.
- **Why GPU:** each additional realtime point light (especially shadow-casting) multiplies draw cost in bases / villages / dungeons — common 1070 Ti hitch source.

### 4. SSAO (Amplify Occlusion) + sun shafts
- **IL:** `CameraEffects.ApplySettings` → `SetSSAO(m_ssao)` enables `AmplifyOcclusionEffect` (fullscreen); `SetSunShafts` toggles `SunShafts` image effect.
- **Why GPU:** full-screen post passes stack on an already saturated GPU; SSAO sample counts and sun shafts are classic Valheim GPU spikes.

### 5. Clutter / vegetation density distance
- **IL:** `ClutterSystem` default `m_distance = 40`, `m_quality` High (3), `m_amountScale = 1`; `ApplySettings` copies `GraphicsSettingsState.m_vegetation`.
- **Why GPU:** grass/clutter is heavy alpha overdraw. Removed from early CPU passes for the wrong reason; **GPU-evidenced** here via fill-bound PresentMon + known 1070 Ti vegetation cost.

## Exactly 5 stable fixes shipped

| # | Fix | Mechanism | Visual tradeoff |
|---|-----|-----------|-----------------|
| 1 | Cap shadows | Postfix `ApplyQualitySettings` (+ session apply): `shadowDistance≤55`, `cascades≤2`, `shadowResolution≤Medium` | Softer / shorter shadows; distant shadow pop reduced range |
| 2 | Soft particles off | Same postfix: `QualitySettings.softParticles = false` | Harder particle edges (smoke/fire/mist less “soft”) |
| 3 | Cap lights | `pixelLightCount≤3`; `LightLod.m_lightLimit≤12`, `m_shadowLimit≤1` | Fewer simultaneous lit/shadowed point lights in dense bases |
| 4 | SSAO + sun shafts off | Postfix `CameraEffects.ApplySettings`: `SetSSAO(0)`, `SetSunShafts(false)` | Flatter contact shadows; no god-ray shafts |
| 5 | Clutter GPU trim | Postfix `ClutterSystem.ApplySettings`: quality ≤ Med, `m_distance≤28`, `m_amountScale≤0.70` | Less grass farther out; slightly thinner near clutter |

Also nudges `lodBias≤1.25` with quality caps (earlier mesh LOD — minor silhouette soften at distance).

**Stability notes**
- Caps re-run after every vanilla apply — not one-shot menu clicks.
- Does **not** write PlayerPrefs; remove DLL + restart restores vanilla graphics prefs as saved.
- No MP protocol changes; clutter `ClearAll` only when quality tier is lowered (one rebuild).
- CPU 0.5.1 patches untouched.

## Gap to 120 FPS (honest)

- Need ~8.3 ms/frame; baseline ~15.8 ms (~77 FPS) with GPU fully busy.
- These caps target the largest known GPU multipliers on 1070 Ti but **will not guarantee 120**. Realistic band after restart: high-80s to ~100+ in lighter biomes if shadows/SSAO were the main tax; heavy bases/mist may still sit GPU-bound.
- Remaining walls: native resolution, water, clouds, character count, uncapped present mode, VRAM bandwidth.

## Verify

Restart Valheim to load `0.6.0`, then re-run PresentMon + nvidia-smi into `profile/gpu_pass_post_*.csv` and compare to baseline above.
