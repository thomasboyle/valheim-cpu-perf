using HarmonyLib;
using UnityEngine;

namespace ValheimCpuPerf.Patches
{
    /// <summary>
    /// v0.6.0 GPU pass: stable QualitySettings / Valheim graphics caps for fill-rate
    /// limited GPUs (e.g. GTX 1070 Ti). Re-applied after every vanilla Apply* so menu
    /// changes cannot undo caps. Does not write PlayerPrefs (reversible by removing DLL).
    /// </summary>
    internal static class GpuCaps
    {
        /// <summary>Vanilla High uses 150 m / 4 cascades — huge shadow-map cost on 8 GB Pascal.</summary>
        internal const float MaxShadowDistance = 55f;
        internal const int MaxShadowCascades = 2;
        internal const ShadowResolution MaxShadowResolution = ShadowResolution.Medium;

        /// <summary>Vanilla GetLightLimit High = 8 pixel lights.</summary>
        internal const int MaxPixelLights = 3;

        /// <summary>Vanilla GetPointLightLimit Med/High = 15/40; -1 = unlimited.</summary>
        internal const int MaxPointLights = 12;

        /// <summary>Vanilla GetPointLightShadowLimit High = 3 or -1 unlimited.</summary>
        internal const int MaxPointLightShadows = 1;

        /// <summary>Vanilla GetLodBias High = 5 — keeps dense meshes farther; nudge down for GPU.</summary>
        internal const float MaxLodBias = 1.25f;

        /// <summary>ClutterSystem ctor default distance 40; High vegetation is fill-bound.</summary>
        internal const float MaxClutterDistance = 28f;
        internal const float MaxClutterAmountScale = 0.70f;

        internal static readonly System.Action<CameraEffects, int> CameraSetSSAO =
            AccessTools.MethodDelegate<System.Action<CameraEffects, int>>(
                AccessTools.Method(typeof(CameraEffects), "SetSSAO", new[] { typeof(int) }));

        internal static void ApplyQualityCaps()
        {
            // 1) Shadows
            if (QualitySettings.shadowDistance > MaxShadowDistance)
                QualitySettings.shadowDistance = MaxShadowDistance;
            if (QualitySettings.shadowCascades > MaxShadowCascades)
                QualitySettings.shadowCascades = MaxShadowCascades;
            if (QualitySettings.shadowResolution > MaxShadowResolution)
                QualitySettings.shadowResolution = MaxShadowResolution;

            // 2) Soft particles (extra depth fetches / overdraw)
            if (QualitySettings.softParticles)
                QualitySettings.softParticles = false;

            // 3) Pixel lights
            if (QualitySettings.pixelLightCount > MaxPixelLights)
                QualitySettings.pixelLightCount = MaxPixelLights;

            // LOD bias nudge (part of light/geometry budget — applied with quality caps)
            if (QualitySettings.lodBias > MaxLodBias)
                QualitySettings.lodBias = MaxLodBias;
        }

        internal static void ApplyLightLodCaps()
        {
            // LightLod static limits drive point-light activation / shadow casters.
            if (LightLod.m_lightLimit < 0 || LightLod.m_lightLimit > MaxPointLights)
                LightLod.m_lightLimit = MaxPointLights;
            if (LightLod.m_shadowLimit < 0 || LightLod.m_shadowLimit > MaxPointLightShadows)
                LightLod.m_shadowLimit = MaxPointLightShadows;
        }

        internal static void ApplyCameraEffectCaps(CameraEffects fx)
        {
            if (fx == null)
                return;

            // 4) SSAO off — AmplifyOcclusion fullscreen pass
            if (CameraSetSSAO != null)
                CameraSetSSAO(fx, 0);

            // bundled with post stack: sun shafts (public API)
            fx.SetSunShafts(false);
        }

        internal static void ApplyClutterCaps(ClutterSystem clutter)
        {
            if (clutter == null)
                return;

            // 5) Vegetation / clutter GPU fill
            bool rebuilt = false;
            if (clutter.m_quality > ClutterSystem.Quality.Med)
            {
                clutter.m_quality = ClutterSystem.Quality.Med;
                rebuilt = true;
            }

            if (clutter.m_distance > MaxClutterDistance)
                clutter.m_distance = MaxClutterDistance;

            if (clutter.m_amountScale > MaxClutterAmountScale)
                clutter.m_amountScale = MaxClutterAmountScale;

            if (rebuilt)
                clutter.ClearAll();
        }
    }

    /// <summary>
    /// Bottleneck 1+2+3 (partial): after vanilla sets shadowDistance 80/120/150,
    /// softParticles, pixelLightCount, lodBias — re-cap for GPU.
    /// </summary>
    [HarmonyPatch(typeof(GraphicsSettingsManager), "ApplyQualitySettings")]
    internal static class Gpu_ApplyQualitySettings
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            GpuCaps.ApplyQualityCaps();
        }
    }

    /// <summary>
    /// Bottleneck 3: LightLod point-light + shadow caster limits.
    /// </summary>
    [HarmonyPatch(typeof(GraphicsSettingsManager), "ApplyLightLod")]
    internal static class Gpu_ApplyLightLod
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            GpuCaps.ApplyLightLodCaps();
        }
    }

    /// <summary>
    /// Safety net: any full session apply also re-runs quality + light caps.
    /// </summary>
    [HarmonyPatch(typeof(GraphicsSettingsManager), "ApplyGraphicsSettingsToCurrentSession")]
    internal static class Gpu_ApplySession
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            GpuCaps.ApplyQualityCaps();
            GpuCaps.ApplyLightLodCaps();
            if (CameraEffects.instance != null)
                GpuCaps.ApplyCameraEffectCaps(CameraEffects.instance);
            if (ClutterSystem.instance != null)
                GpuCaps.ApplyClutterCaps(ClutterSystem.instance);
        }
    }

    /// <summary>
    /// Bottleneck 4: Amplify SSAO + sun shafts after CameraEffects.ApplySettings.
    /// </summary>
    [HarmonyPatch(typeof(CameraEffects), nameof(CameraEffects.ApplySettings))]
    internal static class Gpu_CameraEffects
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(CameraEffects __instance)
        {
            GpuCaps.ApplyCameraEffectCaps(__instance);
        }
    }

    /// <summary>
    /// Bottleneck 5: clutter / vegetation distance + amount + quality ceiling.
    /// </summary>
    [HarmonyPatch(typeof(ClutterSystem), nameof(ClutterSystem.ApplySettings))]
    internal static class Gpu_ClutterSettings
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ClutterSystem __instance)
        {
            GpuCaps.ApplyClutterCaps(__instance);
        }
    }
}
