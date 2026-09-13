# Post-0.4.0 re-profile (MEASURE) — COMPLETE after restart

## Status (2026-09-13 ~19:08–19:10 BST / UTC+1)

| Item | Value |
|------|-------|
| Shipping DLL | **ValheimCpuPerf 0.4.0** loaded |
| Profile sampler | **ValheimCpuPerf.Profile 0.3.0-profile** loaded for this capture; **removed from plugins after capture** |
| Process | `valheim.exe` PID **10852** (started 19:03:30 BST) |
| World | Multiplayer; `inWorld=True` from 19:07:36; capture after ~10s settle |
| Managed delta | `managed_hotspots_20260913_190830.csv` → `...191000.csv` (~90 s) |
| Process CPU | **3.06 equiv cores** (`verify_cpu_0_4_postrestart.csv`) |
| PresentMon FPS | **~73.1 avg / ~85.8 p50** (`verify_presentmon_0_4_postrestart.csv`) |

Profile.dll removed from `BepInEx/plugins` after this run. **Another Valheim restart is required to unload Profile from memory** (DLL already deleted from disk; shipping 0.4.0 remains).

---

## Process CPU

| Metric | Baseline (shipping OFF + Profile ON) | Post-0.4 shipping only (Profile OFF) | **Post-0.4 + Profile (this run)** |
|--------|--------------------------------------|--------------------------------------|-----------------------------------|
| equiv cores | **3.22** | **3.52** | **3.06** |
| WS | ~3.21 GB | ~3.93 GB | **~3.45 GB** |
| File | `cpu_during_managed_summary.txt` | `verify_cpu_0_4.csv` | `verify_cpu_0_4_postrestart.csv` |

This co-capture (shipping + Profile) is the fair compare vs the 3.22-core baseline: **~0.16 cores lower** despite Profile sampler overhead. Earlier 3.52-core run lacked Profile and had higher WS (~3.9 GB).

Sample window: 19:08:46–19:10:05 BST, 40 samples @ 2 s, wall 80.5 s, cpu_delta 246.1 s.

---

## PresentMon FPS

| Metric | Prior baseline | Post-0.4 (no Profile) | **Post-0.4 post-restart** |
|--------|----------------|-----------------------|---------------------------|
| frames | 1155 | 1616 | **1826** |
| avg msBetweenPresents | 17.25 | 15.46 | **13.68** |
| avg FPS | **~58** | **~64.7** | **~73.1** |
| p50 FPS | - | ~77.9 | **~85.8** |
| p95 ms | - | 29.3 | **26.3** |

FPS up vs both prior windows. Scene/camera may differ; managed deltas below corroborate CPU relief on patched paths.

---

## Managed hotspot ranking (post-0.4, steady-state delta ~90 s)

Total delta sampled inclusive ms ~ **24714**.

### Patched-method deltas vs prior baseline (shipping OFF)

| Method | Baseline % / ms (~72 s) | Post-0.4 % / ms (~90 s) | Approx ms/s before → after |
|--------|-------------------------|-------------------------|----------------------------|
| ZSyncTransform.CustomFixedUpdate | **30.6% / 8744** | **1.5% / 365** | ~121 → **~4.1** |
| WaterVolume.UpdateFloaters | **7.7% / 2188** | **1.4% / 344** | ~30 → **~3.8** |
| Smoke.CustomUpdate | **5.2% / 1485** | **0.0% / 0** | ~21 → **0** (no smoke activity in this window) |
| Fish.CustomFixedUpdate | **3.7% / 1058** | **0.8% / 193** | ~15 → **~2.1** |

All four targeted paths dropped sharply in share and ms/s. Smoke at 0 may be scene-empty rather than only the distant gate; ZSync/Water/Fish drops are unambiguous.

### New top 5 remaining bottlenecks

| Rank | Method | Δ ms | % sampled |
|------|--------|-----:|----------:|
| 1 | Character.CustomFixedUpdate | 2877 | **11.6** |
| 2 | Humanoid.CustomFixedUpdate | 2641 | **10.7** |
| 3 | ZNetScene.Update / CreateDestroyObjects | ~2310 / 2294 | **~9.3** |
| 4 | StaticPhysics.SUpdate | 1683 | **6.8** |
| 5 | Character.UpdateMotion | 1096 | **4.4** |

No new fixes implemented this run (measure-only). Character/Humanoid remain speculative to early-out; CreateDestroyObjects still lacks a safe definitive gate.

---

## Artifacts

- `profile/verify_cpu_0_4_postrestart.csv` + `_summary.txt`
- `profile/verify_presentmon_0_4_postrestart.csv` + `_summary.txt`
- `profile/managed_hotspots_20260913_190830.csv` → `...191000.csv`
- `profile/managed_delta_0_4_postrestart.csv`
- `profile/analyze_delta_0_4_postrestart.ps1`
- Plugins after cleanup: **ValheimCpuPerf.dll only** (Profile.dll removed)

