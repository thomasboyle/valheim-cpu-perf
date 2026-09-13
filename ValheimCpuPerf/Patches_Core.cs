using HarmonyLib;
using UnityEngine;

namespace ValheimCpuPerf.Patches
{
    /// <summary>
    /// Always-on structural early-outs / rate limits for the top CPU bottleneck classes
    /// identified by external profiling (docs/EXTERNAL_PROFILE.md).
    /// Performance only — no damage/health/stamina/loot cheats.
    /// </summary>
    internal static class CoreTweaks
    {
        internal const float ClutterAmountScale = 0.65f;
        internal const float ClutterDistanceScale = 0.75f;
        internal const float DistantAiMeters = 40f;
        // Skip 2 of every 3 UpdateAI ticks when distant (run 1/3).
        internal const int DistantAiPeriod = 3;

        internal static bool ShouldSkipAlternatingFrame()
        {
            return (Time.frameCount & 1) != 0;
        }

        internal static bool ShouldThrottleDistantAi(BaseAI ai)
        {
            var player = Player.m_localPlayer;
            if (player == null || ai == null)
                return false;
            var distSq = (player.transform.position - ai.transform.position).sqrMagnitude;
            var limit = DistantAiMeters * DistantAiMeters;
            if (distSq < limit)
                return false;
            var id = ai.GetInstanceID();
            return ((Time.frameCount + id) % DistantAiPeriod) != 0;
        }
    }

    [HarmonyPatch(typeof(ParticleMist), "Update")]
    internal static class ParticleMist_RateLimit
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            // Every other frame — bottleneck class #2.
            return !CoreTweaks.ShouldSkipAlternatingFrame();
        }
    }

    [HarmonyPatch(typeof(Smoke), "CustomUpdate")]
    internal static class Smoke_RateLimit
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            return !CoreTweaks.ShouldSkipAlternatingFrame();
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.UpdateAI))]
    internal static class BaseAI_DistantGate
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BaseAI __instance)
        {
            // Distant agents — bottleneck class #3.
            return !CoreTweaks.ShouldThrottleDistantAi(__instance);
        }
    }

    [HarmonyPatch(typeof(ClutterSystem), "Awake")]
    internal static class ClutterSystem_ScaleAwake
    {
        [HarmonyPostfix]
        private static void Postfix(ClutterSystem __instance)
        {
            ApplyClutterScales(__instance, "Awake");
        }

        internal static void ApplyClutterScales(ClutterSystem instance, string reason)
        {
            if (instance == null)
                return;
            try
            {
                instance.m_amountScale *= CoreTweaks.ClutterAmountScale;
                instance.m_distance *= CoreTweaks.ClutterDistanceScale;
                ValheimCpuPerfPlugin.Log.LogInfo(
                    $"ClutterSystem scaled ({reason}): amount*={CoreTweaks.ClutterAmountScale}, distance*={CoreTweaks.ClutterDistanceScale} → amount={instance.m_amountScale}, distance={instance.m_distance}");
            }
            catch (System.Exception ex)
            {
                ValheimCpuPerfPlugin.Log.LogWarning($"Clutter scale soft-fail: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ClutterSystem), "UpdateGrass")]
    internal static class ClutterSystem_ScaleLive
    {
        private static bool _applied;

        [HarmonyPrefix]
        private static void Prefix(ClutterSystem __instance)
        {
            if (_applied || __instance == null)
                return;
            // Re-apply once if Awake was missed (scene reload / late spawn).
            ClutterSystem_ScaleAwake.ApplyClutterScales(__instance, "UpdateGrass");
            _applied = true;
        }
    }
}
