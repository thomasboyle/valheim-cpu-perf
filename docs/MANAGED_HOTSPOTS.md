# Managed hotspot capture (in-process Harmony sampler)

## Capture conditions

| Item | Value |
|------|-------|
| Date | 2026-09-13 (Europe/London, BST / UTC+1) |
| Process | `valheim.exe` PID **24200** |
| Plugin | **ValheimCpuPerf.Profile.dll** 0.3.0-profile (temporary; soft-cap shipping DLL disabled) |
| World | Multiplayer (PlayFab), **Spawned** at 12:18:37 local; `inWorld=True` during sample window |
| Sample window (steady-state) | **~12:18:48 → 12:20:00** (~72 s post-spawn) |
| Process CPU context | ~**3.2 cores** equivalent (`delta cpu_sec / wall`) during capture; WS ~3.2 GB |
| CSV (latest full) | `profile/managed_hotspots_20260913_122000.csv` |
| CSV delta basis | `managed_hotspots_20260913_121848.csv` → `...122000.csv` |
| Analyzer | `profile/analyze_delta.ps1` |

Menu/load hitch note: cumulative CSV ranks `ZNet.Update` / `Minimap.Update` / `FejdStartup.Update` highly because of **one-shot connect/load spikes** (max ≈ 9.7 s / 8.3 s). Steady-state **delta** rankings below exclude that skew.

Old v0.2 soft caps (clutter scale, mist/smoke every-other-frame, distant BaseAI, shadow 60 m) were **disabled** during capture so hotspots reflect vanilla managed cost.

---

## Top 15 (steady-state delta, ~72 s in-world)

Total delta sampled inclusive ms ≈ **28541**.

| Rank | Method | Δ calls | Δ ms | avg ms | % of sampled | Notes |
|------|--------|--------:|-----:|-------:|-------------:|-------|
| 1 | ZSyncTransform.CustomFixedUpdate | 712543 | 8744 | 0.012 | **30.6** | → ClientSync |
| 2 | ZNetScene.Update | 5386 | 2505 | 0.465 | **8.8** | inclusive of #3 |
| 3 | ZNetScene.CreateDestroyObjects | 1792 | 2494 | 1.391 | **8.7** | almost all of Update |
| 4 | WaterVolume.UpdateFloaters | 125282 | 2188 | 0.017 | **7.7** | GetWaterSurface/CalcWave |
| 5 | Smoke.CustomUpdate | 588347 | 1485 | 0.0025 | 5.2 | |
| 6 | Fish.CustomFixedUpdate | 283105 | 1058 | 0.0037 | 3.7 | |
| 7 | Humanoid.CustomFixedUpdate | 78118 | 903 | 0.012 | 3.2 | |
| 8 | Character.CustomFixedUpdate | 95114 | 876 | 0.009 | 3.1 | |
| 9 | StaticPhysics.SUpdate | 379799 | 748 | 0.002 | 2.6 | |
| 10 | ZNet.Update | 5386 | 605 | 0.112 | 2.1 | post-spike residual |
| 11 | VisEquipment.CustomUpdate | 15279 | 377 | 0.025 | 1.3 | |
| 12 | GameCamera.LateUpdate | 5386 | 353 | 0.066 | 1.2 | |
| 13 | ClutterSystem.LateUpdate | 5386 | 339 | 0.063 | 1.2 | |
| 14 | Player.Update | 15278 | 333 | 0.022 | 1.2 | |
| 15 | CharacterAnimEvent.CustomLateUpdate | 142823 | 305 | 0.002 | 1.1 | |

**Not hot (contra v0.2 assumptions):** `ParticleMist.Update` 0.08% lifetime; `BaseAI.UpdateAI` 0.07%; `MonsterAI.UpdateAI` 0.24%; clutter ~1.2%.

`ZNetScene.Update` inclusive time ≈ `CreateDestroyObjects` — treat as **one** bottleneck class.

---

## Named top 3 bottlenecks

### 1. ZSyncTransform.CustomFixedUpdate — **30.6%** (definitive fix shipped)

- Evidence: largest steady-state Δ ms; ~198 instances × FixedUpdate rate.
- IL: method only validates `ZNetView` then calls `ClientSync`; `ClientSync` **returns immediately when `ZDO.IsOwner()`**.
- Owner writes use `CustomLateUpdate` → `OwnerSync` (separate path).

**Shipped fix (v0.3.0):** Harmony Prefix — skip `CustomFixedUpdate` when `ZNetView.IsOwner()` (pure win). Additionally, non-`Character` / non-`Projectile` instances farther than **64 m** from local player run on **1/3** of frames (staggered). Characters/projectiles stay full-rate.

### 2. ZNetScene.CreateDestroyObjects (via Update) — **~8.8%** (no safe definitive fix)

- Evidence: Update ≈ CreateDestroyObjects inclusive cost; already rate-limited to 30 Hz (`m_createDestroyFps`), max 10 creates/frame.
- Lowering rate or skipping when zone unchanged risks multiplayer pop-in / delayed despawn.

**Shipped fix:** none. Documented only.

### 3. WaterVolume.UpdateFloaters — **7.7%** (definitive fix shipped)

- Evidence: high call volume; per-floater `GetWaterSurface` → `Depth` / `CalcWave`.
- Visual water time/wind remain on `WaterVolume.StaticUpdate` (unpatched).

**Shipped fix (v0.3.0):** Harmony Prefix — if closest point on volume `m_collider` (else transform) is farther than **48 m** from local player, skip floater liquid updates. Distant ocean volumes cannot affect local buoyancy/gameplay.

---

## Removed v0.2 always-on tweaks

Removed because they did **not** map to measured top-3: clutter amount/distance scale, ParticleMist/Smoke every-other-frame, distant BaseAI 1/3, `QualitySettings.shadowDistance` 60 m cap.

---

## Sampler artifact

Temporary project: `ValheimCpuPerf.Profile/` (not shipped). Remove `ValheimCpuPerf.Profile.dll` from `BepInEx/plugins` after capture (done in deploy step).
