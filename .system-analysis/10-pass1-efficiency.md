# 10 — Pass 1: Efficiency architecture (less work per frame)

Objective: cut **structural** work without QualitySettings wrappers or foliage blow-out.

## Findings

1. AO `OnPreRender` (233 IL) + `commandBuffer_FillComputeOcclusion` (363 IL) dominate post FX when enabled.
2. Soft point-light shadow maps scale with concurrent casters even after distant LightLod Off.
3. `GenerateVegPatch` loop size is `m_amount [/quality] * m_amountScale` — early-out alone leaves near-field dense.
4. `ReflectionUpdate.RenderProbe` is full cubemap; interval/resolution are free levers that keep probes live.
5. Extra Depth/Reflect cameras are still full extra views if re-enabled by scene.

## Changes (architecture)

- AO: keep component on; disable temporal/blur; gate OnPreRender period=2.
- Shadows: soft-cap closest N; veg cast Off under grassroot.
- Clutter: per-call `m_amountScale` mul (restore after) + existing distance gates.
- Reflections: floor interval, clamp resolution, IndividualFaces time-slicing.
- Water: harden surface renderers; broaden extra-cam name match; best-effort SSR off.
- ZSync: throttle distant character ClientSync 1/3 (>80 m).

## Validation

Build Release → deploy DLL → **restart Valheim** → PresentMon vs 0.7.1 baseline.
