# ValheimCpuPerf

Open-source **performance** mod for Valheim (BepInEx 5 / HarmonyX).  
**Not a cheat mod** — no godmode, damage, stamina, teleport, or item exploits.

## Approach

1. **Profile externally** (WPR / WPA / sampling) while `valheim.exe` runs — see [`docs/EXTERNAL_PROFILE.md`](docs/EXTERNAL_PROFILE.md).
2. **Bake fixes** into a thin always-on plugin that targets the top CPU bottleneck classes.
3. **Restart Valheim** after replacing `ValheimCpuPerf.dll` so BepInEx loads the new assembly.

There is **no** in-game F8/F9 profiler overlay and **no** mitigation config toggles. The shipping DLL only applies core tweaks.

**Honest disclaimer:** this does **not** guarantee 120 FPS. GPU limits, sync, and uncapped vs VSync settings still apply.

## Always-on core tweaks (v0.2)

Derived from external ETW + Mono.Cecil + known Valheim offenders:

| Bottleneck class | Baked behaviour |
|------------------|-----------------|
| ClutterSystem / grass | Scale `m_amountScale` ×0.65 and `m_distance` ×0.75 |
| ParticleMist + Smoke | Update / `CustomUpdate` every other frame |
| Distant BaseAI | Beyond 40 m from local player, run `UpdateAI` 1/3 of frames |
| (support) Shadows | Cap `QualitySettings.shadowDistance` to 60 m once at startup |

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

## External profiling (for contributors)

Example WPR capture (run elevated):

```bat
wpr -start CPU -filemode
rem play in-world ~20-30 seconds
wpr -stop D:\C++\120fpsvalheim\profile\capture.etl
```

Then analyze with WPA or `xperf -i capture.etl -o cpu_detail.txt -a profile -detail`.  
Mono managed frames often need Cecil / game knowledge in addition to ETW (JIT stacks rarely name `ClutterSystem` etc.).

## License

MIT
