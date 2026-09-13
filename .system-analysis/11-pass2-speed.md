# 11 — Pass 2: Speed tighten (hot-path constants)

Objective: tighten constants after Pass 1 architecture without regressing foliage.

## Constants tightened

| Knob | Value | Rationale |
|------|-------|-----------|
| `AoPreRenderPeriod` | 2 | Half CB fills; last occlusion persists |
| AO Intensity / Radius | ≤0.4 / ≤1.2 | Cheaper sample footprint |
| `FilterEnabled` | false | Drop temporal history path |
| `MaxSoftShadowLights` | 3 | Cap concurrent Soft maps |
| `ClutterNearScale` / Mid | 0.85 / 0.5 | Near look, mid density cut |
| `ReflectMinInterval` | 2.5 s | Fewer RenderProbe calls |
| `ReflectProbeResolution` | 128 | Smaller cubemap |
| `DistantCharacterSyncMeters` | 80 m @ 1/3 | Safer character throttle |

## What we did **not** do (honesty)

- No 2× claim vs 0.7.1 — GPU still native-bound.
- No ZNetScene / StaticPhysics rewrite.
- No AO disable, no probe Custom mode.
