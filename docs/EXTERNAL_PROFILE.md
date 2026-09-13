# External / managed profile findings (ValheimCpuPerf)

## Primary: live managed sampler (2026-09-13)

See **[`docs/MANAGED_HOTSPOTS.md`](MANAGED_HOTSPOTS.md)** for the in-process Harmony Stopwatch capture, steady-state top 15, and v0.3.0 fix decisions.

Summary of steady-state top 3:

1. **ZSyncTransform.CustomFixedUpdate** (~30.6% of sampled delta) — fix shipped  
2. **ZNetScene.CreateDestroyObjects** (~8.8%) — no safe definitive fix  
3. **WaterVolume.UpdateFloaters** (~7.7%) — fix shipped  

v0.2 clutter / mist / BaseAI / shadow knobs did **not** match live top-3 and were removed.

---

## Earlier external ETW pass (same day, pre-sampler)

Profiling outside the shipping mod with WPR/xperf. Mono JIT stacks rarely name managed methods; ETW was used for process/module pressure context only.

| Item | Value |
|------|-------|
| Process | `valheim.exe` (PID changed across launches) |
| ETW | `profile/valheim_cpu_20260913_115248.etl` |
| Analysis | `xperf -a profile -detail` → `profile/cpu_detail.txt` |
| Managed IL (static) | Mono.Cecil → `profile/cecil_hotspots.csv` |

### ETW module evidence

| Module | Role |
|--------|------|
| **UnityPlayer.dll** | Dominates process CPU (engine update/render/cull/jobs) |
| mono-2.0-bdwgc.dll | Mono GC / runtime under managed `assembly_valheim` |
| nvoglv64.dll | OpenGL driver |

Butterfly stacks without Unity/Mono PDBs collapse to unknown — **managed method names required the Harmony sampler**, not ETW alone.

### Why v0.2 targets were wrong

Cecil IL size + community lore pointed at ClutterSystem / ParticleMist / BaseAI. Live inclusive timing showed those are minor in this session’s busy multiplayer coastal area; **ZSyncTransform**, **ZNetScene streaming**, and **WaterVolume floaters** dominated instead.

Re-profile after installing a new DLL; **restart Valheim** so BepInEx reloads the plugin.
