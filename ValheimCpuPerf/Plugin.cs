using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ValheimCpuPerf
{
    /// <summary>
    /// Thin always-on CPU performance plugin. Profiling is external (see docs/EXTERNAL_PROFILE.md).
    /// No in-game profiler, no ConfigEntry mitigation toggles, no cheats.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class ValheimCpuPerfPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.thomasboyle.valheimcpuperf";
        public const string PluginName = "ValheimCpuPerf";
        public const string PluginVersion = "0.2.0";

        internal static ManualLogSource Log { get; private set; }

        /// <summary>Soft-cap for Unity shadow cascades (meters).</summary>
        internal const float ShadowDistanceCap = 60f;

        private Harmony _harmony;
        private bool _shadowApplied;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded — always-on core tweaks (Clutter, Mist/Smoke, distant BaseAI, shadow cap). Restart Valheim after replacing the DLL.");
        }

        private void Update()
        {
            if (_shadowApplied)
                return;
            try
            {
                if (QualitySettings.shadowDistance > ShadowDistanceCap)
                    QualitySettings.shadowDistance = ShadowDistanceCap;
                _shadowApplied = true;
                Log.LogInfo($"QualitySettings.shadowDistance capped to {ShadowDistanceCap:0}m (was applied once).");
            }
            catch (System.Exception ex)
            {
                _shadowApplied = true;
                Log.LogWarning($"Shadow distance cap soft-fail: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
