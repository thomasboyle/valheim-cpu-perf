# Rewrite pass 0.8.0 — Tier A + B structural rewrites

Date: 2026-09-13 (Europe/London). Shipping plugin **0.8.0**.

Keeps all **0.5.1 CPU** gates and **0.7.1 foliage-safe** rules (AO enabled; ReflectionUpdate probes live).

## Baseline (0.7.1 MEASURED)

| Metric | Value |
|--------|-------|
| FPS avg / p50 | **87.1 / 85.1** |
| MsGPUBusy | **13.6** |
| GPU util | **94.2%** |
| CPU equiv cores | **3.63** |
| Vanilla FPS (ref) | **~58** |

## Tier A

1. **AmplifyOcclusionEffect** — ENABLED; Low/Downsample; Filter+Blur off; Intensity≤0.4; **OnPreRender every 2nd frame**.
2. **ZSyncTransform ClientSync** — owners skip; distant static 1/3 & 1/6; **characters >80 m at 1/3**; projectiles full-rate.
3. **Near-field shadows** — closest **3** Soft lights; veg/clutter MeshRenderer `shadowCastingMode=Off`; distant LightLod/Heightmap kept.

## Tier B

4. **GenerateVegPatch** — per-call temporary `m_amountScale` ×0.85 near / ×0.5 mid + early-out/checkerboard.
5. **ReflectionUpdate** — `m_interval`≥2.5 s; probe resolution 128; IndividualFaces; **probes stay live**.
6. **Water path** — water MeshRenderer shadows Off; disable Depth/Reflect/WaterCam/Planar/Mirror cams; best-effort SSR off.

## Not rewritten

- `ZNetScene.CreateDestroyObjects`
- Full `StaticPhysics`
- Unity native GfxDevice / full Valheim engine

## Deploy

```bat
dotnet build ValheimCpuPerf\ValheimCpuPerf.csproj -c Release
```

**Restart Valheim** after DLL replace. PresentMon vs 0.7.1 after restart.
