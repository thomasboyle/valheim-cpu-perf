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
        public const string PluginVersion = "0.7.1";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Log.LogInfo(PluginName + " " + PluginVersion + " loaded - CPU 0.5.1 gates kept; GPU 0.7.1 STRUCTURAL renderer (hotfix): LightLod distant shadows Off, Heightmap distant ShadowCastingMode.Off, ParticleMist emit clamp + distant stop, Clutter GeneratePatch/VegPatch early-out, AmplifyOcclusionEffect ENABLED cheap (Low/Downsample), ReflectionUpdate vanilla (0.7.0 skip removed — white bush fix). Restart Valheim after replacing the DLL.");
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
