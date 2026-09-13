# 01 — System overview (what the mod patches)

## CPU (kept from 0.5.1)

- `ZSyncTransform.CustomFixedUpdate` — owner skip; distant non-char throttle; **0.8.0** also throttles distant characters (>80 m, 1/3)
- `WaterVolume.UpdateFloaters` — distant skip
- `Smoke.CustomUpdate` — distant timer-only
- `Fish.CustomFixedUpdate` — non-owner early-out
- `Character` / `Humanoid.CustomFixedUpdate` — distant non-owner SetVisible-only
- **Not patched:** `ZNetScene.CreateDestroyObjects`, full `StaticPhysics`

## GPU / renderer

| Tier | Target | 0.8.0 rewrite idea |
|------|--------|--------------------|
| A1 | `AmplifyOcclusionEffect` | ENABLED + cheap fields; OnPreRender every 2nd frame |
| A2 | `ZSyncTransform` client sync | Distant character 1/3 + existing static tiers |
| A3 | Near-field shadows | Soft shadow cap (closest 3); veg MeshRenderer Off; distant LightLod/Heightmap kept |
| B4 | `ClutterSystem.GenerateVegPatch` | Per-call temporary `m_amountScale` thin + early-out |
| B5 | `ReflectionUpdate` | Higher `m_interval`, probe resolution 128; probes stay live |
| B6 | Water / extra cams | Surface shadow Off; disable Depth/Reflect/WaterCam/Planar; optional SSR off |

## Visual hard constraints

- AO must keep contributing (no `enabled=false`)
- Reflection probes must not go Custom/disabled (white bushes in 0.7.0)
