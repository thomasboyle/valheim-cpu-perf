# 00 — Inventory (ValheimCpuPerf mod repo)

Date: 2026-09-13 (Europe/London). Scope: **this BepInEx mod**, not a rewrite of Valheim.

## Repo

| Path | Role |
|------|------|
| `ValheimCpuPerf/` | Shipping plugin (`Plugin.cs`, `Patches_Core.cs`, `Patches_Gpu.cs`) |
| `ValheimCpuPerf.Profile/` | Temporary managed hotspot sampler (not shipped) |
| `docs/` | Profile / renderer pass notes |
| `profile/` | PresentMon / nvidia-smi / managed CSV captures |
| `Environment.props` | Valheim + BepInEx paths |
| `.system-analysis/` | Efficiency/speed pass notes (this folder) |

## Patch targets (Valheim assemblies)

Harmony patches land on types inside `assembly_valheim.dll` (and related managed). The mod does **not** rewrite Unity native GfxDevice or ship a Valheim fork.

## Shipping artifact

`BepInEx/plugins/ValheimCpuPerf.dll` — restart Valheim required after replace.
