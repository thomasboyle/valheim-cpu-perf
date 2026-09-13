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

Menu/load hitch note: cumulative CSV ranks `ZNet.Update` / `Minimap.Update` / `FejdStartup.Update` highly because of **one-shot connect/load spikes** (max ~ 9.7 s / 8.3 s). Steady-state **delta** rankings below exclude that skew.

Old v0.2 soft caps (clutter scale, mist/smoke every-other-frame, distant BaseAI, shadow 60 m) were **disabled** during capture so hotspots reflect vanilla managed cost.

---

## Top 15 (steady-state delta, ~72 s in-world)

Total delta sampled inclusive ms ~ **28541**.

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

## Named bottlenecks and shipped fixes

### 1. ZSyncTransform.CustomFixedUpdate — **30.6%** (definitive fix shipped)

- Evidence: largest steady-state Δ ms; ~198 instances × FixedUpdate rate.
- IL: method only validates `ZNetView` then calls `ClientSync`; `ClientSync` **returns immediately when `ZDO.IsOwner()`**.
- Owner writes use `CustomLateUpdate` → `OwnerSync` (separate path).

**Shipped fix (v0.3.0 + deepened v0.4.0):** Harmony Prefix — skip `CustomFixedUpdate` when `ZNetView.IsOwner()` (pure win). Non-`Character` / non-`Projectile` instances: **64–128 m** run on **1/3** of frames; **>128 m** on **1/6** of frames (staggered). Characters/projectiles stay full-rate.

### 2. ZNetScene.CreateDestroyObjects (via Update) — **~8.8%** (no safe definitive fix)

- Evidence: Update ≈ CreateDestroyObjects inclusive cost; already rate-limited to 30 Hz (`m_createDestroyFps`), max 10 creates/frame.
- IL: every tick clears lists → `ZDOMan.FindSectorObjects` → `CreateObjects` / `RemoveObjects`. `RemoveObjects` walks **all** `m_instances` marking earmarks.
- Lowering rate, skipping when zone unchanged, or capping destroys risks multiplayer pop-in / delayed despawn / leaked instances. No Valheim-intended tighter gate found that is safe to tighten further.

**Shipped fix:** none. Documented only. Re-evaluate only if a future profile shows a safe zone/ZDO dirty-bit the game already maintains.

### 3. WaterVolume.UpdateFloaters — **7.7%** (definitive fix shipped)

- Evidence: high call volume; per-floater `GetWaterSurface` → `Depth` / `CalcWave`.
- Visual water time/wind remain on `WaterVolume.StaticUpdate` (unpatched).

**Shipped fix (v0.3.0):** Harmony Prefix — if closest point on volume `m_collider` (else transform) is farther than **48 m** from local player, skip floater liquid updates.

### 4. Smoke.CustomUpdate — **5.2%** (definitive fix shipped in v0.4.0)

- Evidence: ~588k calls / 1485 ms; per-smoke Rigidbody mass + `AddForce` every Update.
- IL: TTL/fade/destroy are independent of the force path.

**Shipped fix (v0.4.0):** Harmony Prefix — beyond **64 m** from local player, advance TTL/fade/destroy only (same end state as vanilla) and **skip Rigidbody force work**. Near smoke unchanged.

### 5. Fish.CustomFixedUpdate — **3.7%** (definitive fix shipped in v0.4.0)

- Evidence: ~283k calls / 1058 ms; coastal fish density.
- IL: vanilla already `return`s for `!IsOwner()` **after** water `Depth`/`CalcWave`, collision bookkeeping, `SetVisible`, and owner-change `WakeUp`.

**Shipped fix (v0.4.0):** Harmony Prefix — for non-owners, perform `SetVisible(HasOwner)`, owner-change `WakeUp`, and fall through to vanilla only while hooked (`ZDOVars.s_hooked`/`s_escape`) so spectator struggle VFX still runs; otherwise skip the expensive pre-ret work. Owner swim AI untouched.

### Other high ranks (analyzed, not patched)

| Method | % | Why not patched |
|--------|--:|-----------------|
| Humanoid/Character.CustomFixedUpdate | ~6.3 | Non-owners still need visual water/tilt/effects; early-out would be speculative |
| StaticPhysics.SUpdate | 2.6 | Already `ShouldUpdate` + `OutsideActiveArea`; cost is call volume via SlowUpdater |
| VisEquipment / GameCamera / Clutter / Player / AnimEvent | ≤1.3 | Small; no clear redundant early-out |

---

## Removed v0.2 always-on tweaks

Removed because they did **not** map to measured top hotspots: clutter amount/distance scale, ParticleMist/Smoke every-other-frame, distant BaseAI 1/3, `QualitySettings.shadowDistance` 60 m cap.

(v0.4 Smoke fix is **not** every-other-frame — it is distance-gated timer-only, preserving near-field physics.)

---

## Shipping versions

| Version | Fixes |
|---------|-------|
| 0.3.0 | ZSyncTransform owner skip + 64 m 1/3; WaterVolume 48 m floater skip |
| **0.4.0** | + ZSyncTransform 128 m 1/6; Smoke distant timer-only; Fish non-owner early-out |

Deployed DLL: `BepInEx/plugins/ValheimCpuPerf.dll`. **Restart Valheim** after replace.

---

## Sampler artifact

Temporary project: `ValheimCpuPerf.Profile/` (not shipped). Remove `ValheimCpuPerf.Profile.dll` from `BepInEx/plugins` after capture (not present in shipping deploy).

---

## Post-0.4.0 re-profile (COMPLETE 2026-09-13 ~19:08–19:10 BST)

| Item | Value |
|------|-------|
| Process | `valheim.exe` PID **10852** |
| Plugins | **ValheimCpuPerf 0.4.0** + Profile 0.3.0-profile (Profile **removed from disk after capture**) |
| Sample window | **19:08:30 → 19:10:00** (~90 s, `inWorld=True`) |
| Process CPU | **3.06** equiv cores (WS ~3.45 GB) — vs baseline **3.22** (shipping off + Profile) |
| PresentMon | **~73.1 avg FPS** / ~85.8 p50 (vs baseline ~58 / prior shipping-only ~65) |
| Delta CSV | `managed_hotspots_20260913_190830.csv` → `...191000.csv` |
| Analyzer | `profile/analyze_delta_0_4_postrestart.ps1` → `managed_delta_0_4_postrestart.csv` |

### Patched methods: before → after

| Method | Baseline % | Post-0.4 % | Baseline ms (~72s) | Post-0.4 ms (~90s) |
|--------|----------:|-----------:|-------------------:|-------------------:|
| ZSyncTransform.CustomFixedUpdate | 30.6 | **1.5** | 8744 | **365** |
| WaterVolume.UpdateFloaters | 7.7 | **1.4** | 2188 | **344** |
| Smoke.CustomUpdate | 5.2 | **0.0** | 1485 | **0** |
| Fish.CustomFixedUpdate | 3.7 | **0.8** | 1058 | **193** |

### Top 5 remaining (post-0.4 delta)

1. Character.CustomFixedUpdate — 11.6%
2. Humanoid.CustomFixedUpdate — 10.7%
3. ZNetScene.CreateDestroyObjects (via Update) — ~9.3%
4. StaticPhysics.SUpdate — 6.8%
5. Character.UpdateMotion — 4.4%

See **`docs/PROFILE_0_4.md`** for full tables. Measure-only; no new fixes this run.

**Note:** Profile.dll deleted from `BepInEx/plugins`. Restart Valheim once more when convenient to unload it from the live process; shipping 0.4.0 stays loaded from disk on next launch.

---

## 0.5.0 (2026-09-13)

**New definitive fix:** `Character.CustomFixedUpdate` distant non-owner lite (64 m) - SetVisible(HasOwner) only; skip liquid/effects/tilt/look cosmetics. Owners + near non-owners unchanged.

**Re-checked, still unfixed:** ZNetScene.CreateDestroyObjects (`m_dirtyChunks` is save-only). StaticPhysics already gated. UpdateMotion owner-only.

See `docs/DEEP_PROFILE_NEXT.md`.

---

## 0.5.1 (2026-09-13)

**New definitive fix:** `Humanoid.CustomFixedUpdate` distant non-owner lite (64 m) — same `SetVisible(HasOwner)` gate as Character; also skips `UpdateUseVisual` (equip VFX / hand visual). Owners + near non-owners unchanged.

**Re-checked hard IL, still unfixed:** `ZNetScene.CreateDestroyObjects` — Update hardcodes 1/30s (`m_createDestroyFps` unused); `RemoveObjects` walks all `m_instances`; `m_dirtyChunks` save-only; `m_clientChangeQueue` is sync SendZDOs only. StaticPhysics already gated.

See `docs/DEEP_PROFILE_NEXT.md`.

### 0.5.1 VERIFY (2026-09-13 ~19:52-19:53 BST)

See **`docs/PROFILE_0_5.md`**.

| Metric | Deep pre-0.5 | **0.5.1** |
|--------|--------------|-----------|
| Character.CustomFixedUpdate | 10.6% / 3416 ms | **3.9% / 744 ms** |
| Humanoid.CustomFixedUpdate | 10.0% / 3220 ms | **4.0% / 774 ms** |
| CPU cores | 3.18 (deep) / 3.06 (post-0.4) | **2.95** |
| FPS avg / p50 | 67.9 / 83.6 (deep) ; 73.1 / 85.8 (post-0.4) | **64.9 / 81.7** |
| GPU util avg | 81.3% | **93.3%** |

Remaining top 5: ZSync 19.5%, ZNetScene CDO ~5.8%, StaticPhysics 5.5%, Player.Update 4.6% (hitch), Humanoid ~4%. Profile.dll removed after capture.
