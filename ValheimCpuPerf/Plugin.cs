using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

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
        public const string PluginVersion = "0.6.0";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded - CPU 0.5.1 gates kept; GPU 0.6.0 caps: shadowDistance/cascades, softParticles off, pixel+point lights, SSAO+sunshafts off, clutter distance/amount/quality. Restart Valheim after replacing the DLL.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
