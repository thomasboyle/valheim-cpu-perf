# External profile findings (ValheimCpuPerf)

Profiling is performed **outside** the shipping mod. The DLL only applies baked always-on core tweaks.

## Capture (2026-09-13, Europe/London)

| Item | Value |
|------|-------|
| Process | `valheim.exe` PID **19912** (PID may change across launches) |
| Wall sample window | ~25 s process CPU sampling (`profile/cpu_samples.csv`) |
| ETW | WPR `CPU` filemode → `profile/valheim_cpu_20260913_115248.etl` (~1005 MB) |
| ETW duration | ~47 s of sampled intervals in `xperf -a profile` output |
| Analysis | `xperf -a profile -detail` → `profile/cpu_detail.txt` / `profile/module_summary.txt` |
| Managed IL | Mono.Cecil on `assembly_valheim.dll` → `profile/cecil_hotspots.csv` |
| Reflect dump | `_reflect.txt` (Update/FixedUpdate surface) |

### Process CPU (sampling)

During the 25 s window, `valheim.exe` held ~**5.9 GB** working set and consumed roughly **~3–5 cores** of CPU (delta CPU seconds / wall ≈ 300–500% of one logical core). Thread 19932 alone had multi-hour cumulative CPU — consistent with a heavily CPU-bound Unity main/job mix, not an idle menu.

### ETW module evidence (symbols for Mono JIT poorly resolve)

Top modules attributed to `valheim.exe` by sample weight (`profile -detail`):

| Module | Weight | Share of system samples | Role |
|--------|--------|-------------------------|------|
| **UnityPlayer.dll** | 98,339,060 | **13.09%** | Engine update/render/cull/job dispatch — dominates process CPU |
| ntoskrnl.exe | 39,661,359 | 5.28% | Kernel time while process is runnable / wait / DPC mix |
| ntdll.dll | 17,292,000 | 2.30% | User/kernel transitions |
| nvoglv64.dll | 10,183,134 | 1.36% | OpenGL driver (GPU-related, not patched here) |
| *(blank / JIT)* | 6,085,146 | 0.81% | Typical unresolved JIT/anonymous ranges |
| **mono-2.0-bdwgc.dll** | 2,255,498 | 0.30% | Mono GC / runtime supporting managed `assembly_valheim` |

Butterfly stacks without Unity/Mono PDBs collapse to `***unknown***` — **managed method names are not recoverable from this ETL alone**. That is expected for Unity Mono. Bottleneck *classes* below combine ETW (engine+mono pressure), Cecil IL complexity of per-frame systems, and well-known Valheim client CPU offenders verified against `_reflect.txt`.

---

## Top 3 CPU bottleneck classes

### 1. ClutterSystem / grass & clutter generation

**Evidence**

- `_reflect.txt`: `ClutterSystem.LateUpdate`, `UpdateGrass(Single,Boolean,Vector3)` present.
- Cecil: `GenerateVegPatch` **389 IL**, `GeneratePatches` 115 IL, `LateUpdate` 92 IL — largest recurring clutter work; public fields `m_amountScale`, `m_distance`, `m_grassPatchSize`.
- Community / prior Valheim client profiling consistently lists grass/clutter as a top CPU cost when looking around bases or meadows.
- Fits under **UnityPlayer** pressure (transform/mesh/particle instance churn driven by managed clutter rebuilds).

**Baked fix:** always scale `m_amountScale` (~0.65×) and `m_distance` (~0.75×) on `ClutterSystem.Awake` / first `UpdateGrass`.

### 2. ParticleMist + Smoke (per-frame VFX / mist simulation)

**Evidence**

- Cecil: `ParticleMist.Update` **241 IL** (heavy per-frame; heightmap/sort lists in fields); `Smoke.CustomUpdate` 86 IL with static `List<Smoke> s_smoke` (cost scales with instance count); `SmokeSpawner` interval/max globals exist.
- `_reflect.txt`: `ParticleMist.Update`, `Smoke.CustomUpdate(Single,Single)` confirmed.
- Mist/smoke are classic Valheim frame-time spikes near waterfalls, mead halls, and forges — managed work that feeds Unity particle systems (shows up as UnityPlayer + mono, not named managed frames in ETW).

**Baked fix:** rate-limit — run `ParticleMist.Update` and `Smoke.CustomUpdate` every other frame (structural early-out prefixes).

### 3. Distant BaseAI (creature AI tick fan-out)

**Evidence**

- Cecil: `BaseAI.UpdateAI` is the per-agent tick; many expensive helpers (`MoveAndAvoid` 204 IL, `FindClosestCreature` 140, `CanSeeTarget` 101, etc.). Instance count scales with nearby fauna.
- `_reflect.txt`: `BaseAI` / `IUpdateAI` / `CustomFixedUpdate` surface.
- Far agents still ticking full AI is a known Valheim CPU amplifier in forests / plains; safe to distance-gate on client/SP without combat cheats.

**Baked fix:** if agent is farther than **40 m** from `Player.m_localPlayer`, skip `BaseAI.UpdateAI` on 2 of every 3 frames (instance-stable stagger).

### Supporting always-on render CPU soft-cap

Because **UnityPlayer.dll** alone is ~54% of valheim-attributed sample weight, also soft-cap `QualitySettings.shadowDistance` to **60 m** once after load (shadow cascade CPU/GPU). Not counted as a separate “class” in the top 3; it is a cheap engine-side companion to class #1/#2 world density costs.

---

## What the shipping mod does *not* do

- No F8/F9 overlay, no in-process Harmony CPU sampler, no mitigation `ConfigEntry` knobs.
- No godmode / stamina / damage / teleport cheats.

Re-profile with WPR / WPA / your tool of choice after installing a new DLL; **restart Valheim** so BepInEx reloads the plugin.
