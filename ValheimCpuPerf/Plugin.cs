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
        public const string PluginVersion = "0.8.1";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Log.LogInfo(PluginName + " " + PluginVersion + " loaded - CPU 0.5.1 gates kept; 0.8.1: ReflectionUpdate fully VANILLA (fixes ~3s foliage white flash from 0.8.0 probe interval/128/IndividualFaces). AO cheap every-frame OnPreRender (ENABLED). Soft-shadow cap + veg Off, Clutter thin, water/extra-cam harden kept. Restart Valheim after replacing the DLL.");
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
