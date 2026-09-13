using HarmonyLib;
using UnityEngine;

namespace ValheimCpuPerf.Patches
{
    /// <summary>
    /// Performance-only mitigations. No damage/health/stamina/loot cheats.
    /// Gated behind EnableMitigations (default false).
    /// </summary>
    internal static class Mitigations
    {
        internal static bool Active =>
            ValheimCpuPerfPlugin.EnableMitigations != null &&
            ValheimCpuPerfPlugin.EnableMitigations.Value;

        internal static bool ShouldSkipMist()
        {
            if (!Active || !ValheimCpuPerfPlugin.ThrottleParticleMist.Value)
                return false;
            var skip = ValheimCpuPerfPlugin.ParticleMistSkipFrames.Value;
            if (skip <= 0) return false;
            return (Time.frameCount % (skip + 1)) != 0;
        }

        internal static bool ShouldSkipSmoke()
        {
            if (!Active || !ValheimCpuPerfPlugin.ThrottleSmoke.Value)
                return false;
            var skip = ValheimCpuPerfPlugin.SmokeSkipFrames.Value;
            if (skip <= 0) return false;
            return (Time.frameCount % (skip + 1)) != 0;
        }

        internal static bool ShouldThrottleAI(BaseAI ai)
        {
            if (!Active || !ValheimCpuPerfPlugin.ThrottleDistantAI.Value)
                return false;
            var skip = ValheimCpuPerfPlugin.DistantAISkipFrames.Value;
            if (skip <= 0) return false;
            var player = Player.m_localPlayer;
            if (player == null || ai == null)
                return false;
            var dist = Vector3.Distance(player.transform.position, ai.transform.position);
            if (dist < ValheimCpuPerfPlugin.DistantAIDistance.Value)
                return false;
            var id = ai.GetInstanceID();
            return ((Time.frameCount + id) % (skip + 1)) != 0;
        }
    }

    [HarmonyPatch(typeof(ParticleMist), "Update")]
    internal static class ParticleMist_Throttle
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            if (!Mitigations.ShouldSkipMist())
                return true;
            CpuProfiler.Enter("ParticleMist.Update(skipped)");
            CpuProfiler.Exit("ParticleMist.Update(skipped)");
            return false;
        }
    }

    [HarmonyPatch(typeof(Smoke), "CustomUpdate")]
    internal static class Smoke_Throttle
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            if (!Mitigations.ShouldSkipSmoke())
                return true;
            CpuProfiler.Enter("Smoke.CustomUpdate(skipped)");
            CpuProfiler.Exit("Smoke.CustomUpdate(skipped)");
            return false;
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.UpdateAI))]
    internal static class BaseAI_Throttle
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BaseAI __instance)
        {
            if (!Mitigations.ShouldThrottleAI(__instance))
                return true;
            CpuProfiler.Enter("BaseAI.UpdateAI(throttled)");
            CpuProfiler.Exit("BaseAI.UpdateAI(throttled)");
            return false;
        }
    }

    /// <summary>
    /// Scale ClutterSystem.m_amountScale and m_distance (verified fields).
    /// </summary>
    [HarmonyPatch(typeof(ClutterSystem), "Awake")]
    internal static class ClutterSystem_Density
    {
        [HarmonyPostfix]
        private static void Postfix(ClutterSystem __instance)
        {
            if (!Mitigations.Active)
                return;
            try
            {
                var tr = Traverse.Create(__instance);
                var amount = tr.Field("m_amountScale");
                if (amount.FieldExists())
                {
                    var v = amount.GetValue<float>();
                    amount.SetValue(v * ValheimCpuPerfPlugin.ClutterDensityMultiplier.Value);
                }
                var dist = tr.Field("m_distance");
                if (dist.FieldExists())
                {
                    var v = dist.GetValue<float>();
                    dist.SetValue(v * ValheimCpuPerfPlugin.ClutterDistanceMultiplier.Value);
                }
                ValheimCpuPerfPlugin.Log.LogInfo(
                    $"Clutter scaled: density×{ValheimCpuPerfPlugin.ClutterDensityMultiplier.Value}, distance×{ValheimCpuPerfPlugin.ClutterDistanceMultiplier.Value}");
            }
            catch (System.Exception ex)
            {
                ValheimCpuPerfPlugin.Log.LogWarning($"Clutter mitigation soft-fail: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Also apply when grass rebuilds so menu/quality changes don't wipe scaled values permanently
    /// without re-reading config — re-apply multipliers relative to current only once via flag.
    /// </summary>
    [HarmonyPatch(typeof(ClutterSystem), "UpdateGrass")]
    internal static class ClutterSystem_DistanceLive
    {
        private static bool _appliedLive;

        [HarmonyPrefix]
        private static void Prefix(ClutterSystem __instance)
        {
            if (!Mitigations.Active || _appliedLive)
                return;
            // Soft: ensure distance stays capped if something reset it toward vanilla large values
            try
            {
                var tr = Traverse.Create(__instance);
                var dist = tr.Field("m_distance");
                if (!dist.FieldExists()) return;
                var v = dist.GetValue<float>();
                var targetFactor = ValheimCpuPerfPlugin.ClutterDistanceMultiplier.Value;
                // If still near typical large defaults, scale once
                if (targetFactor < 0.99f)
                {
                    dist.SetValue(v * targetFactor);
                    _appliedLive = true;
                }
            }
            catch { /* ignore */ }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
    internal static class ZNetScene_CreateBudget
    {
        private static int _lastFrame = -1;
        private static int _runsThisFrame;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            if (!Mitigations.Active || !ValheimCpuPerfPlugin.ThrottleZNetSceneCreates.Value)
                return true;

            if (_lastFrame != Time.frameCount)
            {
                _lastFrame = Time.frameCount;
                _runsThisFrame = 0;
            }
            _runsThisFrame++;
            // Normally once per frame; only drop extras under very low budget.
            if (_runsThisFrame > 1 && ValheimCpuPerfPlugin.MaxCreatesPerFrame.Value < 20)
                return false;
            return true;
        }
    }
}
