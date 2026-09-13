# 09 — Baseline (0.7.1 MEASURED)

Capture: `profile/profile_0_7_1_20260913_210049.csv` (+ nvidia-smi / CPU sample). Scene: in-world outdoors (Black Forest-class). Hardware: GTX 1070 Ti.

| Metric | 0.7.1 MEASURED | Notes |
|--------|----------------|-------|
| FPS avg / p50 | **87.1 / 85.1** | PresentMon |
| MsGPUBusy avg | **13.6** | Still ~full frame → GPU-bound |
| nvidia-smi util | **94.2%** | avg |
| CPU equiv cores | **3.63** | process sample |
| Vanilla FPS (approx) | **~58** | prior reference, not this capture |

Need ~8.3 ms/frame for 120 FPS. 0.8.0 targets managed multipliers only; **2× FPS is not expected** on a saturated 1070 Ti.
