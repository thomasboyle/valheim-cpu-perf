# Deep profile next (0.5.0) - 2026-09-13 ~19:21-19:35 BST

## Phase A - Deep profile (post-0.4, Profile still in-memory)

| Item | Value |
|------|-------|
| Process | valheim.exe PID **10852** (started ~19:03 BST; still running 0.4.0 from disk + Profile from earlier load) |
| Managed delta | `managed_hotspots_20260913_192107.csv` -> `...192237.csv` (~90 s, inWorld) |
| Process CPU | **3.18** equiv cores (`deep_cpu_0_5_pre_summary.txt`) - Profile overhead vs post-0.4 3.06 |
| PresentMon | **~67.9 avg / ~83.6 p50** FPS (`deep_presentmon_0_5_pre_summary.txt`) |
| GPU util (nvidia-smi) | **avg 81.3%** (min 40 max 98) over 18 samples - still often CPU-bound, not stuck at 99% |
| WS | ~3.91 GB |

### Top 15 managed (steady-state delta ~90 s, total ~32204 ms)

| Rank | Method | d_ms | % | Notes |
|------|--------|-----:|--:|-------|
| 1 | Character.CustomFixedUpdate | 3416 | **10.6** | **patched in 0.5** (distant non-owner lite) |
| 2 | Humanoid.CustomFixedUpdate | 3220 | **10.0** | inclusive of Character for humanoids |
| 3 | ZSyncTransform.CustomFixedUpdate | 2803 | 8.7 | denser scene; characters/projectiles still full-rate |
| 4 | ZNetScene.Update | 2549 | 7.9 | ~CreateDestroyObjects |
| 5 | ZNetScene.CreateDestroyObjects | 2533 | 7.9 | **no safe fix** (re-checked) |
| 6 | StaticPhysics.SUpdate | 1797 | 5.6 | already gated |
| 7 | Character.UpdateMotion | 1216 | 3.8 | owner-only already |
| 8 | Character.UpdateWalking | 961 | 3.0 | under UpdateMotion |
| 9 | ClutterSystem.LateUpdate | 848 | 2.6 | |
| 10 | Heightmap.Regenerate | 846 | 2.6 | |
| 11 | ZNet.Update | 844 | 2.6 | |
| 12 | Heightmap.CustomLateUpdate | 728 | 2.3 | |
| 13 | ClutterSystem.UpdateGrass | 667 | 2.1 | |
| 14 | WearNTear.UpdateWear | 566 | 1.8 | |
| 15 | MonsterAI.UpdateAI | 546 | 1.7 | |

Artifacts: `profile/managed_delta_deep_pre.csv`, `deep_cpu_0_5_pre*`, `deep_presentmon_0_5_pre*`, `deep_gpu_0_5_pre*`, `il_deep_0_5.txt`.

---

## Phase B - IL conclusions

### Character / Humanoid.CustomFixedUpdate - DEFINITIVE fix shipped

- Owners: full sim after `IsOwner` branch (UpdateMotion, stagger, SEMan, etc.).
- Non-owners always still run: CalculateLiquidDepth, UpdateLayer, UpdateContinousEffects (SetupContinuousEffect x3), UpdateWater (early-ret after swimTimer), UpdateGroundTilt **client path** (ZDO tiltrot lerp + Animator), SetVisible, UpdateLookTransition, UpdateHeatEffects (local-player only early-ret).
- `GetLiquidLevel` is a field read (cheap); cost is continuous effects + ground-tilt client work at high instance count.
- **Safe gate:** beyond **64 m**, non-owners only need `SetVisible(HasOwner)` for LOD ownership bookkeeping. Cosmetics cannot affect local gameplay.
- Owners never gated. Near non-owners unchanged.
- Humanoid calls `Character.CustomFixedUpdate` via non-virtual `call` - Character Harmony Prefix applies.

### Character.UpdateMotion - no patch

- Already owner-only (called only inside owner branch of CustomFixedUpdate).
- Sleeping AI already skips UpdateWalking. Distant owned-AI throttle would desync physics - speculative.

### StaticPhysics.SUpdate - no patch

- Already `ShouldUpdate(time)` + `ZNetScene.OutsideActiveArea`.
- Invoked via SlowUpdater coroutine: 100 instances/frame then yield, full list pass then WaitForSeconds(0.1).
- Further call-volume reduction would need SlowUpdater restructuring - not definitive.

### ZNetScene.CreateDestroyObjects - still no safe fix

- Harder re-check: `ZDOMan.m_dirtyChunks` is **SAVE-ONLY** (`UpdateSaveState`, `get_DirtyChunks`, `AddObjectsPerChunk`) - not a runtime sector-membership dirty bit.
- No unchanged-sector skip without risking delayed spawn/despawn / leaked instances.
- Already rate-limited (~30 Hz via `m_createDestroyTimer`).

### Speculative (documented, skipped)

- Mid-range (32-64 m) non-owner CFU 1/3 throttle - possible visual hitch; skipped for confidence.
- WearNTear / Clutter / Heightmap - smaller share; no clear redundant early-out found this pass.

---

## Phase C - Shipped 0.5.0

**New:** `Character_DistantNonOwnerLite` - Prefix on `Character.CustomFixedUpdate`:
- Owner / near (<=64 m) / no local player -> vanilla
- Distant non-owner -> `SetVisible(HasOwner)` only, skip rest

Preserved: ZSync owner/distant+very-distant, WaterVolume 48 m, Smoke distant lite, Fish non-owner early-out.

Deployed: `BepInEx/plugins/ValheimCpuPerf.dll` (0.5.0) + `ValheimCpuPerf.Profile.dll` (for verify).

**Requires Valheim restart** to load 0.5.0 (process still has 0.4 + old Profile in memory).

---

## Phase D - Verify

Pending restart. Targets vs post-0.4: CPU cores vs ~3.06, FPS vs ~73, Character/Humanoid managed % drop, GPU util.

---

## 0.5.1 follow-up (Humanoid + ZNetScene re-check)

### Humanoid.CustomFixedUpdate — DEFINITIVE fix shipped

IL (25 ops):
1. `IsValid` early-out
2. If owner: `UpdateAttack` / `UpdateEquipment` / `UpdateBlock`
3. **Always** `UpdateUseVisual` (equip effect + hand visual — cosmetic)
4. Non-virtual `Character.CustomFixedUpdate`

0.5.0 Character Prefix already short-circuits step 4 for distant non-owners after restart, but Humanoid still paid for `UpdateUseVisual` + inclusive sampler cost. **0.5.1** Prefix on `Humanoid.CustomFixedUpdate` mirrors Character: distant >64 m non-owner → `SetVisible(HasOwner)` only (skips UseVisual + Character call).

### ZNetScene.CreateDestroyObjects — still no safe fix (harder pass)

- `Update` hardcodes `0.03333334` (30 Hz); `m_createDestroyFps` field is **never read**.
- `CreateObjects` hardcodes max 10/frame (`m_maxCreatedPerFrame` unused).
- `RemoveObjects`: earmark current sector ZDOs with `TempRemoveEarmark(frame&255)`, then walk **all** `m_instances` Values — no incremental earmark API.
- `m_clientChangeQueue` / `GetClientChangeQueue` used only by `SendZDOs` / `CreateSyncList` (network sync), not create/destroy membership.
- Skipping when zone unchanged would delay spawn/despawn for ZDOs that arrive while the player stands still (MP buildings, drops, players) — not safe.

### StaticPhysics.SUpdate — no patch

Unchanged conclusion: already `ShouldUpdate` + `OutsideActiveArea` via SlowUpdater.

### Shipped 0.5.1

Version bump **0.5.1** (Humanoid only; ZNetScene not shipped). Deploy DLL + keep Profile.dll if present for verify. **Requires Valheim restart.**
