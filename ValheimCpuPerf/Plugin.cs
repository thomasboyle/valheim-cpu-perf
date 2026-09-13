using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ValheimCpuPerf
{
    /// <summary>
    /// Always-on CPU performance plugin. Profiling is external / temporary Profile DLL
    /// (see docs/MANAGED_HOTSPOTS.md). No cheats.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class ValheimCpuPerfPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.thomasboyle.valheimcpuperf";
        public const string PluginName = "ValheimCpuPerf";
        public const string PluginVersion = "0.5.1";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded - ZSync owner/distant+very-distant gate, WaterVolume floater distance gate, Smoke distant lite, Fish non-owner early-out, Character+Humanoid distant non-owner lite (64m SetVisible-only). Restart Valheim after replacing the DLL.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
