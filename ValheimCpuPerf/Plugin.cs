using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ValheimCpuPerf
{
    /// <summary>
    /// Always-on CPU + GPU performance plugin. Profiling is external / temporary Profile DLL
    /// (see docs/MANAGED_HOTSPOTS.md). No cheats.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class ValheimCpuPerfPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.thomasboyle.valheimcpuperf";
        public const string PluginName = "ValheimCpuPerf";
        public const string PluginVersion = "0.8.0";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Log.LogInfo(PluginName + " " + PluginVersion + " loaded - CPU 0.5.1 gates kept; 0.8.0 Tier A/B rewrites: AO cheap every-2nd-frame OnPreRender (still ENABLED), ZSync distant-char 1/3, soft-shadow cap + veg shadow Off, Clutter per-call amountScale thin, ReflectionUpdate interval/resolution (probes stay live), water/extra-cam harden. No white-bush path (AO on, probes not Custom). Restart Valheim after replacing the DLL.");
        }

        private void Update()
        {
            Patches.RendererScan.Tick();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
