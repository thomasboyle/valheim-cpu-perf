using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimCpuPerf.Patches
{
    /// <summary>
    /// v0.7.x structural GPU/renderer pass. Replaces the v0.6.0 QualitySettings /
    /// graphics-menu caps. Harmony hooks land on Valheim managed render paths:
    /// LightLod per-instance shadows, Heightmap MeshRenderer shadow mode,
    /// ParticleMist emit, ClutterSystem patch build, AmplifyOcclusionEffect
    /// cheap settings (kept ENABLED — 0.7.1). ReflectionUpdate left vanilla
    /// (0.7.1). No QualitySettings.shadowDistance / softParticles /
    /// pixelLightCount / lodBias writes. No SetSSAO(0) wrapper.
    /// </summary>
    internal static class RenderFields
    {
        internal static readonly AccessTools.FieldRef<LightLod, Light> LightLodLight =
            AccessTools.FieldRefAccess<LightLod, Light>("m_light");

        internal static readonly AccessTools.FieldRef<Heightmap, bool> HeightmapDistant =
            AccessTools.FieldRefAccess<Heightmap, bool>("m_isDistantLod");

        internal static readonly AccessTools.FieldRef<Heightmap, MeshRenderer> HeightmapRenderer =
            AccessTools.FieldRefAccess<Heightmap, MeshRenderer>("m_meshRenderer");

        internal static readonly AccessTools.FieldRef<ParticleMist, ParticleSystem> MistParticles =
            AccessTools.FieldRefAccess<ParticleMist, ParticleSystem>("m_ps");

        internal static readonly AccessTools.FieldRef<CameraEffects, AmplifyOcclusionEffect> FxAO =
            AccessTools.FieldRefAccess<CameraEffects, AmplifyOcclusionEffect>("m_amplifyOcclusion");

        internal static readonly AccessTools.FieldRef<GameCamera, Camera> GameSkyCam =
            AccessTools.FieldRefAccess<GameCamera, Camera>("m_skyCamera");

        internal static readonly AccessTools.FieldRef<GameCamera, Camera> GameMainCam =
            AccessTools.FieldRefAccess<GameCamera, Camera>("m_camera");


        /// <summary>Beyond this, LightLod point lights drop shadow maps (hysteresis restore below).</summary>
        internal const float ShadowOffMeters = 28f;
        internal const float ShadowOnMeters = 22f;

        /// <summary>Clutter GeneratePatch / GenerateVegPatch skip beyond this XZ distance.</summary>
        internal const float ClutterPatchMeters = 24f;

        /// <summary>Outer-ring patches: skip odd (x+y) so spawn loop never runs (50% thin).</summary>
        internal const float ClutterCheckerMeters = 14f;

        /// <summary>ParticleMist.Emit hard cap on toEmit (vanilla can burst dozens per tick).</summary>
        internal const int MaxMistEmit = 6;

        /// <summary>Beyond this, ParticleMist.MisterEmit is skipped.</summary>
        internal const float DistantMistMeters = 40f;

        /// <summary>Throttled scan: stop / hide distant particle systems.</summary>
        internal const float DistantParticleMeters = 48f;

        internal const int ScanPeriodFrames = 30;
    }

    /// <summary>
    /// Cached per-instance decisions so we do not flicker shadows/particles every frame.
    /// </summary>
    internal static class RenderCache
    {
        internal static readonly HashSet<int> ShadowsForcedOff = new HashSet<int>();
        internal static readonly HashSet<int> ParticlesStopped = new HashSet<int>();
        internal static readonly HashSet<int> ExtraCamsDisabled = new HashSet<int>();
        internal static bool LoggedAo;
        internal static int ShadowTouches;
        internal static int PatchSkips;
        internal static int MistClamps;
    }

    /// <summary>
    /// Bottleneck 1: LightLod point-light shadow maps. Vanilla UpdateLights sorts
    /// and assigns m_lightPrio; UpdateLoop then may keep shadows on within
    /// m_shadowDistance. We structurally force Light.shadows = None on instances
    /// beyond ShadowOffMeters (restore inside ShadowOnMeters). Not QualitySettings.shadowDistance.
    /// </summary>
    [HarmonyPatch(typeof(LightLod), "UpdateLights")]
    internal static class Gpu_LightLodDistantShadows
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            ApplyDistantLightShadows();
        }

        internal static void ApplyDistantLightShadows()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            Vector3 pos = player.transform.position;
            float offSqr = RenderFields.ShadowOffMeters * RenderFields.ShadowOffMeters;
            float onSqr = RenderFields.ShadowOnMeters * RenderFields.ShadowOnMeters;

            HashSet<LightLod> lights = Traverse.Create(typeof(LightLod)).Field("m_lights").GetValue<HashSet<LightLod>>();
            if (lights == null)
                return;

            foreach (LightLod lod in lights)
            {
                if (lod == null)
                    continue;

                Light light = RenderFields.LightLodLight(lod);
                if (light == null)
                    continue;

                int id = lod.GetInstanceID();
                float sqr = (lod.transform.position - pos).sqrMagnitude;
                bool forced = RenderCache.ShadowsForcedOff.Contains(id);

                if (!forced && sqr > offSqr)
                {
                    if (light.shadows != LightShadows.None)
                    {
                        light.shadows = LightShadows.None;
                        RenderCache.ShadowTouches++;
                    }
                    RenderCache.ShadowsForcedOff.Add(id);
                }
                else if (forced && sqr < onSqr)
                {
                    RenderCache.ShadowsForcedOff.Remove(id);
                    // Vanilla UpdateLoop restores Soft/Hard on its 1s tick; do not snap Hard here.
                }
            }
        }
    }

    /// <summary>
    /// Bottleneck 1b: Heightmap.UpdateShadowSettings already writes
    /// MeshRenderer.shadowCastingMode. Distant LOD chunks still cast when
    /// GraphicsSettingsState.m_distantShadows is true. Force Off on distant LOD
    /// renderers — per-mesh structural, not a global shadowDistance cap.
    /// </summary>
    [HarmonyPatch(typeof(Heightmap), "UpdateShadowSettings")]
    internal static class Gpu_HeightmapDistantShadowsOff
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Heightmap __instance)
        {
            if (__instance == null)
                return;
            if (!RenderFields.HeightmapDistant(__instance))
                return;

            MeshRenderer mr = RenderFields.HeightmapRenderer(__instance);
            if (mr == null)
                return;

            if (mr.shadowCastingMode != ShadowCastingMode.Off)
                mr.shadowCastingMode = ShadowCastingMode.Off;
            if (mr.receiveShadows)
                mr.receiveShadows = false;
        }
    }

    // Bottleneck 2 (0.7.1): ReflectionUpdate left vanilla.
    // 0.7.0 prefix-skipped Update and forced probes Custom/disabled, which blew
    // out ambient/specular on vegetation (white bushes). Prefer correct lighting
    // over that GPU win. Extra Depth/Reflect cameras still disabled in RendererScan.

    /// <summary>
    /// Bottleneck 3a: ParticleMist.Emit is the GPU particle-buffer fill. Clamp
    /// toEmit so the spawn loop cannot burst a full mist cloud every 0.1s.
    /// </summary>
    [HarmonyPatch(typeof(ParticleMist), "Emit")]
    internal static class Gpu_ParticleMistEmitClamp
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ref int toEmit)
        {
            if (toEmit > RenderFields.MaxMistEmit)
            {
                toEmit = RenderFields.MaxMistEmit;
                RenderCache.MistClamps++;
            }
        }
    }

    /// <summary>
    /// Bottleneck 3b: ParticleMist.MisterEmit — skip when the mister is far from
    /// the local player (GPU overdraw, not CPU Smoke-style).
    /// </summary>
    [HarmonyPatch(typeof(ParticleMist), "MisterEmit")]
    internal static class Gpu_ParticleMistMisterDistant
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Mister mister)
        {
            if (mister == null)
                return true;

            Player player = Player.m_localPlayer;
            if (player == null)
                return true;

            float limit = RenderFields.DistantMistMeters * RenderFields.DistantMistMeters;
            Vector3 d = player.transform.position - mister.transform.position;
            return d.sqrMagnitude <= limit;
        }
    }

    /// <summary>
    /// Bottleneck 3c: ParticleMist.Awake — disable soft-particle material path on
    /// the mist ParticleSystem renderer (per-instance, not QualitySettings.softParticles).
    /// </summary>
    [HarmonyPatch(typeof(ParticleMist), "Awake")]
    internal static class Gpu_ParticleMistAwakeSoftOff
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ParticleMist __instance)
        {
            if (__instance == null)
                return;
            ParticleSystem ps = RenderFields.MistParticles(__instance);
            HardenParticleRenderer(ps);
        }

        internal static void HardenParticleRenderer(ParticleSystem ps)
        {
            if (ps == null)
                return;
            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            if (r == null)
                return;
            // Soft particles are a material keyword on Valheim mist/smoke shaders.
            Material mat = r.sharedMaterial;
            if (mat != null && mat.IsKeywordEnabled("_SOFTPARTICLES_ON"))
            {
                // sharedMaterial is the asset — do not mutate. Use instance material once.
                Material inst = r.material;
                if (inst != null)
                    inst.DisableKeyword("_SOFTPARTICLES_ON");
            }
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    /// <summary>
    /// Bottleneck 4a: ClutterSystem.GeneratePatch — vanilla already DistanceXZ-gates
    /// against m_distance (default 40). Prefix a tighter early-out so GenerateVegPatch
    /// (the instance spawn + mesh-build hot method) never runs for outer patches.
    /// </summary>
    [HarmonyPatch(typeof(ClutterSystem), "GeneratePatch")]
    internal static class Gpu_ClutterGeneratePatchEarlyOut
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ClutterSystem __instance, Vector3 camPos, Vector2Int p)
        {
            if (__instance == null)
                return true;

            Vector3 center = __instance.GetVegPatchCenter(p);
            float dist = Utils.DistanceXZ(center, camPos);
            if (dist > RenderFields.ClutterPatchMeters)
            {
                RenderCache.PatchSkips++;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Bottleneck 4b: ClutterSystem.GenerateVegPatch — the actual spawn loop
    /// (m_amount / quality * m_amountScale, then Random point + Instantiate).
    /// Skip odd (x+y) patches beyond ClutterCheckerMeters so half of the outer
    /// ring never enters the per-instance loop. Deeper than writing m_amountScale.
    /// Returning false leaves PatchData null; GeneratePatch already treats that as skip.
    /// </summary>
    [HarmonyPatch(typeof(ClutterSystem), "GenerateVegPatch")]
    internal static class Gpu_ClutterGenerateVegPatchThin
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ClutterSystem __instance, Vector2Int patchID)
        {
            if (__instance == null)
                return true;

            Player player = Player.m_localPlayer;
            Vector3 origin;
            if (player != null)
                origin = player.transform.position;
            else
            {
                Camera cam = Utils.GetMainCamera();
                if (cam == null)
                    return true;
                origin = cam.transform.position;
            }

            Vector3 center = __instance.GetVegPatchCenter(patchID);
            float dist = Utils.DistanceXZ(center, origin);
            if (dist > RenderFields.ClutterPatchMeters)
            {
                RenderCache.PatchSkips++;
                return false;
            }
            if (dist > RenderFields.ClutterCheckerMeters && ((patchID.x + patchID.y) & 1) == 1)
            {
                RenderCache.PatchSkips++;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Bottleneck 5 (0.7.1): AmplifyOcclusionEffect fullscreen CB pass.
    /// Keep the component ENABLED (foliage contact AO) but force cheap settings:
    /// SampleCount=Low, Downsample, FilterDownsample, Blur off, Intensity cap.
    /// Structural on the component — not CameraEffects.SetSSAO(0).
    /// </summary>
    [HarmonyPatch(typeof(AmplifyOcclusionEffect), "OnEnable")]
    internal static class Gpu_AmplifyOcclusionCheap
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(AmplifyOcclusionEffect __instance)
        {
            Apply(__instance);
        }

        internal static void Apply(AmplifyOcclusionEffect ao)
        {
            if (ao == null)
                return;

            // 0.7.1: keep the component ENABLED so foliage retains contact AO.
            // Only cut structural cost: Low samples, downsample, no blur, intensity cap.
            ao.SampleCount = AmplifyOcclusion.SampleCountLevel.Low;
            ao.Downsample = true;
            ao.FilterDownsample = true;
            ao.BlurEnabled = false;
            if (ao.Intensity > 0.45f)
                ao.Intensity = 0.45f;

            if (!RenderCache.LoggedAo)
            {
                RenderCache.LoggedAo = true;
                ValheimCpuPerfPlugin.Log?.LogInfo("0.7.1 renderer: AmplifyOcclusionEffect kept ENABLED (SampleCount=Low, Downsample, Blur off, Intensity<=0.45).");
            }
        }
    }

    /// <summary>
    /// CameraEffects.ApplySettings may reset AO via SetSSAO(int). After that
    /// runs, re-apply cheap Low/Downsample settings but leave the effect ENABLED
    /// (0.7.1). Do not call SetSSAO — component story, not graphics-menu wrapper.
    /// </summary>
    [HarmonyPatch(typeof(CameraEffects), "ApplySettings")]
    internal static class Gpu_CameraEffectsReassertAo
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(CameraEffects __instance)
        {
            if (__instance == null)
                return;
            Gpu_AmplifyOcclusionCheap.Apply(RenderFields.FxAO(__instance));

            // Sun shafts: disable the image-effect behaviour if present (no SetSunShafts API).
            DisableBehaviourByName(__instance.gameObject, "UnityStandardAssets.ImageEffects.SunShafts");
        }

        internal static void DisableBehaviourByName(GameObject go, string typeName)
        {
            if (go == null)
                return;
            Type t = AccessTools.TypeByName(typeName);
            if (t == null)
                return;
            Component c = go.GetComponent(t);
            Behaviour b = c as Behaviour;
            if (b != null && b.enabled)
                b.enabled = false;
        }
    }

    /// <summary>
    /// Throttled (every 30 frames) scene scan: extra cameras, distant particle
    /// systems. Cached by instance id — no per-frame flicker.
    /// </summary>
    internal static class RendererScan
    {
        internal static void Tick()
        {
            if ((Time.frameCount % RenderFields.ScanPeriodFrames) != 0)
                return;
            if (Player.m_localPlayer == null)
                return;

            try
            {
                ScanExtraCameras();
                ScanDistantParticles();
                Gpu_LightLodDistantShadows.ApplyDistantLightShadows();
            }
            catch (Exception ex)
            {
                ValheimCpuPerfPlugin.Log?.LogWarning("0.7 renderer scan: " + ex.Message);
            }
        }

        private static void ScanExtraCameras()
        {
            Camera sky = null;
            Camera main = Utils.GetMainCamera();
            if (GameCamera.instance != null)
            {
                try { sky = RenderFields.GameSkyCam(GameCamera.instance); }
                catch { sky = null; }
            }

            Camera[] cams = Camera.allCameras;
            if (cams == null)
                return;

            for (int i = 0; i < cams.Length; i++)
            {
                Camera c = cams[i];
                if (c == null || c == main || c == sky)
                    continue;

                string n = c.gameObject.name;
                if (n == null)
                    continue;

                bool extra =
                    n.IndexOf("Depth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Reflect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("WaterCam", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Planar", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!extra)
                    continue;

                int id = c.GetInstanceID();
                if (RenderCache.ExtraCamsDisabled.Contains(id))
                    continue;

                c.enabled = false;
                RenderCache.ExtraCamsDisabled.Add(id);
                ValheimCpuPerfPlugin.Log?.LogInfo("0.7 renderer: disabled extra camera '" + n + "'");
            }
        }

        private static void ScanDistantParticles()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            Vector3 pos = player.transform.position;
            float offSqr = RenderFields.DistantParticleMeters * RenderFields.DistantParticleMeters;
            float onSqr = (RenderFields.DistantParticleMeters - 8f) * (RenderFields.DistantParticleMeters - 8f);

            ParticleSystem[] systems = UnityEngine.Object.FindObjectsOfType<ParticleSystem>();
            if (systems == null)
                return;

            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem ps = systems[i];
                if (ps == null)
                    continue;

                int id = ps.GetInstanceID();
                float sqr = (ps.transform.position - pos).sqrMagnitude;
                bool stopped = RenderCache.ParticlesStopped.Contains(id);

                if (!stopped && sqr > offSqr)
                {
                    if (ps.isPlaying)
                        ps.Pause(true);
                    Gpu_ParticleMistAwakeSoftOff.HardenParticleRenderer(ps);
                    RenderCache.ParticlesStopped.Add(id);
                }
                else if (stopped && sqr < onSqr)
                {
                    if (!ps.isPlaying)
                        ps.Play(true);
                    RenderCache.ParticlesStopped.Remove(id);
                }
            }
        }
    }
}
