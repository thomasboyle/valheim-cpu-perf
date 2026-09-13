using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ValheimCpuPerf
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class ValheimCpuPerfPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.thomasboyle.valheimcpuperf";
        public const string PluginName = "ValheimCpuPerf";
        public const string PluginVersion = "0.1.0";

        internal static ValheimCpuPerfPlugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        // Profiler
        internal static ConfigEntry<bool> EnableProfiler;
        internal static ConfigEntry<KeyCode> DumpKey;
        internal static ConfigEntry<KeyCode> OverlayKey;
        internal static ConfigEntry<float> SampleWindowSeconds;

        // Mitigations (performance only — no cheats)
        internal static ConfigEntry<bool> EnableMitigations;
        internal static ConfigEntry<float> ClutterDensityMultiplier;
        internal static ConfigEntry<float> ClutterDistanceMultiplier;
        internal static ConfigEntry<bool> ThrottleParticleMist;
        internal static ConfigEntry<int> ParticleMistSkipFrames;
        internal static ConfigEntry<bool> ThrottleSmoke;
        internal static ConfigEntry<int> SmokeSkipFrames;
        internal static ConfigEntry<bool> ThrottleDistantAI;
        internal static ConfigEntry<float> DistantAIDistance;
        internal static ConfigEntry<int> DistantAISkipFrames;
        internal static ConfigEntry<bool> ReduceShadowDistance;
        internal static ConfigEntry<float> ShadowDistanceMeters;
        internal static ConfigEntry<bool> ThrottleZNetSceneCreates;
        internal static ConfigEntry<int> MaxCreatesPerFrame;

        private Harmony _harmony;
        private bool _overlayVisible;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            EnableProfiler = Config.Bind("Profiler", "EnableProfiler", true,
                "Sample CPU time on key Valheim systems via Harmony.");
            DumpKey = Config.Bind("Profiler", "DumpKey", KeyCode.F8,
                "Dump top bottlenecks to the BepInEx log and console.");
            OverlayKey = Config.Bind("Profiler", "OverlayKey", KeyCode.F9,
                "Toggle on-screen profiler overlay.");
            SampleWindowSeconds = Config.Bind("Profiler", "SampleWindowSeconds", 2.0f,
                "Rolling window (seconds) used for averages.");

            EnableMitigations = Config.Bind("Mitigations", "EnableMitigations", false,
                "Apply configurable CPU mitigations. Off by default so profiling is honest first.");
            ClutterDensityMultiplier = Config.Bind("Mitigations", "ClutterDensityMultiplier", 0.65f,
                new ConfigDescription("Scale grass/clutter density (1 = vanilla).", new AcceptableValueRange<float>(0.1f, 1f)));
            ClutterDistanceMultiplier = Config.Bind("Mitigations", "ClutterDistanceMultiplier", 0.75f,
                new ConfigDescription("Scale clutter draw/active distance (1 = vanilla).", new AcceptableValueRange<float>(0.25f, 1f)));
            ThrottleParticleMist = Config.Bind("Mitigations", "ThrottleParticleMist", true,
                "Skip ParticleMist updates on some frames when CPU-bound.");
            ParticleMistSkipFrames = Config.Bind("Mitigations", "ParticleMistSkipFrames", 1,
                new ConfigDescription("Frames to skip between ParticleMist updates (0 = never skip).", new AcceptableValueRange<int>(0, 4)));
            ThrottleSmoke = Config.Bind("Mitigations", "ThrottleSmoke", true,
                "Skip Smoke.CustomUpdate on some frames.");
            SmokeSkipFrames = Config.Bind("Mitigations", "SmokeSkipFrames", 1,
                new ConfigDescription("Frames to skip between Smoke updates.", new AcceptableValueRange<int>(0, 4)));
            ThrottleDistantAI = Config.Bind("Mitigations", "ThrottleDistantAI", true,
                "Run distant creature AI less often (visual/sim pacing only; not godmode).");
            DistantAIDistance = Config.Bind("Mitigations", "DistantAIDistance", 40f,
                new ConfigDescription("Beyond this distance from the local player, AI may be throttled.", new AcceptableValueRange<float>(20f, 120f)));
            DistantAISkipFrames = Config.Bind("Mitigations", "DistantAISkipFrames", 2,
                new ConfigDescription("FixedUpdate skips for distant AI.", new AcceptableValueRange<int>(0, 6)));
            ReduceShadowDistance = Config.Bind("Mitigations", "ReduceShadowDistance", false,
                "Cap Unity shadow distance (CPU/GPU cascade cost).");
            ShadowDistanceMeters = Config.Bind("Mitigations", "ShadowDistanceMeters", 60f,
                new ConfigDescription("Max shadow distance in meters when ReduceShadowDistance is on.", new AcceptableValueRange<float>(20f, 150f)));
            ThrottleZNetSceneCreates = Config.Bind("Mitigations", "ThrottleZNetSceneCreates", false,
                "Soft-limit how many ZNetScene create operations run per frame (can smooth spikes; may delay distant object spawn).");
            MaxCreatesPerFrame = Config.Bind("Mitigations", "MaxCreatesPerFrame", 40,
                new ConfigDescription("Approx create budget hint when ThrottleZNetSceneCreates is on.", new AcceptableValueRange<int>(10, 200)));

            CpuProfiler.Configure(SampleWindowSeconds.Value);
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded. F8=dump, F9=overlay. Mitigations default OFF.");
        }

        private void Update()
        {
            if (EnableProfiler.Value)
                CpuProfiler.BeginFrame();

            if (Input.GetKeyDown(DumpKey.Value))
                CpuProfiler.DumpTopBottlenecks(Log);

            if (Input.GetKeyDown(OverlayKey.Value))
                _overlayVisible = !_overlayVisible;

            if (EnableMitigations.Value && ReduceShadowDistance.Value)
            {
                if (QualitySettings.shadowDistance > ShadowDistanceMeters.Value)
                    QualitySettings.shadowDistance = ShadowDistanceMeters.Value;
            }
        }

        private void LateUpdate()
        {
            if (EnableProfiler.Value)
                CpuProfiler.EndFrame();
        }

        private void OnGUI()
        {
            if (!_overlayVisible || !EnableProfiler.Value)
                return;
            CpuProfiler.DrawOverlay();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
