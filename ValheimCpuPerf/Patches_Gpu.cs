using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimCpuPerf.Patches
{
    /// <summary>
    /// v0.8.0 Tier A/B structural GPU/renderer rewrites on Valheim managed paths.
    /// Keeps CPU 0.5.1 gates and 0.7.1 foliage-safe constraints:
    /// AmplifyOcclusionEffect stays ENABLED; ReflectionUpdate probes stay live
    /// (no Custom/disabled probes). No QualitySettings.* / SetSSAO(0) wrappers.
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

        internal static readonly AccessTools.FieldRef<ClutterSystem, float> ClutterAmountScale =
            AccessTools.FieldRefAccess<ClutterSystem, float>("m_amountScale");

        internal static readonly AccessTools.FieldRef<ReflectionUpdate, float> ReflectInterval =
            AccessTools.FieldRefAccess<ReflectionUpdate, float>("m_interval");

        internal static readonly AccessTools.FieldRef<ReflectionUpdate, ReflectionProbe> ReflectProbe1 =
            AccessTools.FieldRefAccess<ReflectionUpdate, ReflectionProbe>("m_probe1");

        internal static readonly AccessTools.FieldRef<ReflectionUpdate, ReflectionProbe> ReflectProbe2 =
            AccessTools.FieldRefAccess<ReflectionUpdate, ReflectionProbe>("m_probe2");

        internal static readonly AccessTools.FieldRef<WaterVolume, MeshRenderer> WaterSurface =
            AccessTools.FieldRefAccess<WaterVolume, MeshRenderer>("m_waterSurface");

        /// <summary>Beyond this, LightLod point lights drop shadow maps (hysteresis restore below).</summary>
        internal const float ShadowOffMeters = 28f;
        internal const float ShadowOnMeters = 22f;

        /// <summary>Max concurrent Soft/Hard shadow-casting lights near the player (closest N).</summary>
        internal const int MaxSoftShadowLights = 3;

        /// <summary>Clutter GeneratePatch / GenerateVegPatch skip beyond this XZ distance.</summary>
        internal const float ClutterPatchMeters = 24f;

        /// <summary>Outer-ring patches: skip odd (x+y) so spawn loop never runs (50% thin).</summary>
        internal const float ClutterCheckerMeters = 14f;

        /// <summary>Near-field amountScale multiplier (look preserved).</summary>
        internal const float ClutterNearScale = 0.85f;

        /// <summary>Mid-ring amountScale multiplier (structural density cut).</summary>
        internal const float ClutterMidScale = 0.5f;

        /// <summary>ParticleMist.Emit hard cap on toEmit (vanilla can burst dozens per tick).</summary>
        internal const int MaxMistEmit = 6;

        /// <summary>Beyond this, ParticleMist.MisterEmit is skipped.</summary>
        internal const float DistantMistMeters = 40f;

        /// <summary>Throttled scan: stop / hide distant particle systems.</summary>
        internal const float DistantParticleMeters = 48f;

        /// <summary>ReflectionUpdate: floor for m_interval (seconds between RenderProbe).</summary>
        internal const float ReflectMinInterval = 2.5f;

        /// <summary>ReflectionProbe.resolution (power-of-two cube face). 128 keeps lighting, cuts GPU.</summary>
        internal const int ReflectProbeResolution = 128;

        /// <summary>AO OnPreRender period: run full CB every N frames (1 = every frame).</summary>
        internal const int AoPreRenderPeriod = 2;

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
        internal static readonly HashSet<int> VegShadowsOff = new HashSet<int>();
        internal static readonly Dictionary<int, float> ClutterScaleRestore = new Dictionary<int, float>();
        internal static bool LoggedAo;
        internal static bool LoggedReflect;
        internal static bool LoggedShadowCap;
        internal static int ShadowTouches;
        internal static int PatchSkips;
        internal static int MistClamps;
        internal static int AoSkips;
        internal static int SoftCaps;
    }

    // -------------------------------------------------------------------------
    // Tier A3: Near-field soft-shadow cap + distant LightLod (kept) + veg Off
    // -------------------------------------------------------------------------

    /// <summary>
    /// Bottleneck 1: LightLod point-light shadow maps. Distant Soft->None (0.7),
    /// plus 0.8.0 rewrite: among remaining Soft/Hard lights, keep only the closest
    /// MaxSoftShadowLights; force the rest to None. Not QualitySettings.shadowDistance.
    /// </summary>
    [HarmonyPatch(typeof(LightLod), "UpdateLights")]
    internal static class Gpu_LightLodDistantShadows
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            ApplyDistantLightShadows();
            CapNearSoftShadows();
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

        /// <summary>
        /// Cap concurrent Soft/Hard shadow casters to the closest N near the player.
        /// Important player-area lights keep looking OK; excess Soft maps are dropped.
        /// </summary>
        internal static void CapNearSoftShadows()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;

            HashSet<LightLod> lights = Traverse.Create(typeof(LightLod)).Field("m_lights").GetValue<HashSet<LightLod>>();
            if (lights == null)
                return;

            Vector3 pos = player.transform.position;
            List<KeyValuePair<float, Light>> soft = new List<KeyValuePair<float, Light>>(16);

            foreach (LightLod lod in lights)
            {
                if (lod == null)
                    continue;
                Light light = RenderFields.LightLodLight(lod);
                if (light == null || light.shadows == LightShadows.None)
                    continue;
                if (RenderCache.ShadowsForcedOff.Contains(lod.GetInstanceID()))
                    continue;

                float sqr = (lod.transform.position - pos).sqrMagnitude;
                soft.Add(new KeyValuePair<float, Light>(sqr, light));
            }

            if (soft.Count <= RenderFields.MaxSoftShadowLights)
                return;

            soft.Sort((a, b) => a.Key.CompareTo(b.Key));
            for (int i = RenderFields.MaxSoftShadowLights; i < soft.Count; i++)
            {
                if (soft[i].Value.shadows != LightShadows.None)
                {
                    soft[i].Value.shadows = LightShadows.None;
                    RenderCache.SoftCaps++;
                }
            }

            if (!RenderCache.LoggedShadowCap)
            {
                RenderCache.LoggedShadowCap = true;
                ValheimCpuPerfPlugin.Log?.LogInfo("0.8.0 renderer: Soft shadow cap = closest " + RenderFields.MaxSoftShadowLights + " LightLod lights.");
            }
        }
    }

    /// <summary>
    /// Bottleneck 1b: Heightmap.UpdateShadowSettings — Force Off on distant LOD
    /// renderers (kept from 0.7).
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

    // -------------------------------------------------------------------------
    // Tier B5: ReflectionUpdate — cheap probes, NOT disabled (white-bush safe)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rewrite ReflectionUpdate: raise m_interval, clamp probe.resolution to 128,
    /// keep probes enabled and Realtime (never Custom/disabled — 0.7.0 white bushes).
    /// </summary>
    [HarmonyPatch(typeof(ReflectionUpdate), "Start")]
    internal static class Gpu_ReflectionUpdateCheap
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ReflectionUpdate __instance)
        {
            Apply(__instance);
        }

        internal static void Apply(ReflectionUpdate ru)
        {
            if (ru == null)
                return;

            float interval = RenderFields.ReflectInterval(ru);
            if (interval < RenderFields.ReflectMinInterval)
                RenderFields.ReflectInterval(ru) = RenderFields.ReflectMinInterval;

            HardenProbe(RenderFields.ReflectProbe1(ru));
            HardenProbe(RenderFields.ReflectProbe2(ru));

            if (!RenderCache.LoggedReflect)
            {
                RenderCache.LoggedReflect = true;
                ValheimCpuPerfPlugin.Log?.LogInfo("0.8.0 renderer: ReflectionUpdate m_interval>=" + RenderFields.ReflectMinInterval + "s, probe resolution=" + RenderFields.ReflectProbeResolution + " (probes stay live).");
            }
        }

        internal static void HardenProbe(ReflectionProbe probe)
        {
            if (probe == null)
                return;
            // Keep enabled + realtime refresh — only cut cube resolution.
            if (probe.resolution > RenderFields.ReflectProbeResolution)
                probe.resolution = RenderFields.ReflectProbeResolution;
            // Prefer once-per-scripted RenderProbe (already driven by ReflectionUpdate).
            if (probe.timeSlicingMode != ReflectionProbeTimeSlicingMode.IndividualFaces)
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
        }
    }

    /// <summary>
    /// Re-assert cheap probe settings each Update without skipping RenderProbe entirely.
    /// Prefix never returns false (probes must keep contributing ambient/specular).
    /// </summary>
    [HarmonyPatch(typeof(ReflectionUpdate), "Update")]
    internal static class Gpu_ReflectionUpdateReassert
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ReflectionUpdate __instance)
        {
            // Periodic re-assert in case graphics settings reset probe resolution.
            if ((Time.frameCount % 120) == 0)
                Gpu_ReflectionUpdateCheap.Apply(__instance);
        }
    }

    // -------------------------------------------------------------------------
    // ParticleMist (kept from 0.7)
    // -------------------------------------------------------------------------

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
            Material mat = r.sharedMaterial;
            if (mat != null && mat.IsKeywordEnabled("_SOFTPARTICLES_ON"))
            {
                Material inst = r.material;
                if (inst != null)
                    inst.DisableKeyword("_SOFTPARTICLES_ON");
            }
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    // -------------------------------------------------------------------------
    // Tier B4: ClutterSystem.GenerateVegPatch structural density rewrite
    // -------------------------------------------------------------------------

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
    /// 0.8.0 rewrite: per-call temporary m_amountScale reduction (restore in Postfix)
    /// plus outer early-out / checkerboard. Structural density cut without permanent
    /// QualitySettings / field caps.
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

            // Per-call structural rewrite: thin instance count via m_amountScale, restore after.
            int key = __instance.GetInstanceID();
            if (!RenderCache.ClutterScaleRestore.ContainsKey(key))
            {
                float original = RenderFields.ClutterAmountScale(__instance);
                RenderCache.ClutterScaleRestore[key] = original;
                float mul = dist <= RenderFields.ClutterCheckerMeters
                    ? RenderFields.ClutterNearScale
                    : RenderFields.ClutterMidScale;
                RenderFields.ClutterAmountScale(__instance) = original * mul;
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ClutterSystem __instance)
        {
            if (__instance == null)
                return;
            int key = __instance.GetInstanceID();
            float original;
            if (RenderCache.ClutterScaleRestore.TryGetValue(key, out original))
            {
                RenderFields.ClutterAmountScale(__instance) = original;
                RenderCache.ClutterScaleRestore.Remove(key);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Tier A1: AmplifyOcclusionEffect — ENABLED, cheaper path, every-2nd OnPreRender
    // -------------------------------------------------------------------------

    /// <summary>
    /// Keep AO ENABLED (foliage contact). Force Low/Downsample/no blur/no temporal filter.
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

            // MUST stay enabled — 0.7.0 full disable caused white bushes.
            if (!ao.enabled)
                ao.enabled = true;

            ao.SampleCount = AmplifyOcclusion.SampleCountLevel.Low;
            ao.Downsample = true;
            ao.FilterDownsample = true;
            ao.BlurEnabled = false;
            // Temporal filter is the heavy history path; disable for single-pass cheap AO.
            ao.FilterEnabled = false;
            if (ao.Intensity > 0.4f)
                ao.Intensity = 0.4f;
            if (ao.Radius > 1.2f)
                ao.Radius = 1.2f;

            if (!RenderCache.LoggedAo)
            {
                RenderCache.LoggedAo = true;
                ValheimCpuPerfPlugin.Log?.LogInfo("0.8.0 renderer: AmplifyOcclusionEffect ENABLED cheap (Low, Downsample, Filter off, Blur off, Intensity<=0.4); OnPreRender every " + RenderFields.AoPreRenderPeriod + " frames.");
            }
        }
    }

    /// <summary>
    /// Skip expensive OnPreRender CB fill on alternate frames. Component stays enabled
    /// so the last occlusion contribution remains; avoids white-bush full disable.
    /// </summary>
    [HarmonyPatch(typeof(AmplifyOcclusionEffect), "OnPreRender")]
    internal static class Gpu_AmplifyOcclusionPreRenderGate
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(AmplifyOcclusionEffect __instance)
        {
            if (__instance == null)
                return true;

            // Re-assert cheap settings cheaply (fields only).
            if ((Time.frameCount & 63) == 0)
                Gpu_AmplifyOcclusionCheap.Apply(__instance);

            if ((Time.frameCount % RenderFields.AoPreRenderPeriod) != 0)
            {
                RenderCache.AoSkips++;
                return false;
            }
            return true;
        }
    }

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
            DisableBehaviourByName(__instance.gameObject, "UnityStandardAssets.ImageEffects.SunShafts");
            // Water / SSR path: disable ScreenSpaceReflection component if present (planar-ish extra pass).
            DisablePostProcessSSR(__instance);
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

        internal static void DisablePostProcessSSR(CameraEffects fx)
        {
            if (fx == null)
                return;
            try
            {
                var trav = Traverse.Create(fx);
                var pp = trav.Field("m_postProcessing").GetValue();
                if (pp == null)
                    return;
                // UnityEngine.PostProcessing.PostProcessingBehaviour profile.screenSpaceReflection.enabled
                var profile = Traverse.Create(pp).Field("profile").GetValue();
                if (profile == null)
                    profile = Traverse.Create(pp).Property("profile").GetValue();
                if (profile == null)
                    return;
                var ssr = Traverse.Create(profile).Field("screenSpaceReflection").GetValue();
                if (ssr == null)
                    ssr = Traverse.Create(profile).Property("screenSpaceReflection").GetValue();
                if (ssr == null)
                    return;
                Traverse.Create(ssr).Field("enabled").SetValue(false);
                // Some builds use m_Enabled / property
                try { Traverse.Create(ssr).Property("enabled").SetValue(false); } catch { }
            }
            catch
            {
                // Optional path — ignore if profile shape differs.
            }
        }
    }

    // -------------------------------------------------------------------------
    // Tier B6: Water surface harden + extra reflection cameras
    // -------------------------------------------------------------------------

    [HarmonyPatch(typeof(WaterVolume), "Awake")]
    internal static class Gpu_WaterVolumeSurfaceCheap
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(WaterVolume __instance)
        {
            Harden(__instance);
        }

        internal static void Harden(WaterVolume wv)
        {
            if (wv == null)
                return;
            MeshRenderer mr = RenderFields.WaterSurface(wv);
            if (mr == null)
                return;
            if (mr.shadowCastingMode != ShadowCastingMode.Off)
                mr.shadowCastingMode = ShadowCastingMode.Off;
            if (mr.receiveShadows)
                mr.receiveShadows = false;
        }
    }

    [HarmonyPatch(typeof(Water), "ApplySettings")]
    internal static class Gpu_WaterMeshCheap
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Water __instance)
        {
            if (__instance == null)
                return;
            MeshRenderer mr = __instance.GetComponent<MeshRenderer>();
            if (mr == null)
                return;
            if (mr.shadowCastingMode != ShadowCastingMode.Off)
                mr.shadowCastingMode = ShadowCastingMode.Off;
            if (mr.receiveShadows)
                mr.receiveShadows = false;
        }
    }

    /// <summary>
    /// Throttled scene scan: extra cameras, distant particles, veg shadow Off, soft-shadow reassert.
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
                ScanVegetationShadowsOff();
                Gpu_LightLodDistantShadows.ApplyDistantLightShadows();
                Gpu_LightLodDistantShadows.CapNearSoftShadows();
            }
            catch (Exception ex)
            {
                ValheimCpuPerfPlugin.Log?.LogWarning("0.8 renderer scan: " + ex.Message);
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
                    n.IndexOf("Planar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Water Reflection", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!extra)
                    continue;

                int id = c.GetInstanceID();
                if (RenderCache.ExtraCamsDisabled.Contains(id))
                    continue;

                c.enabled = false;
                RenderCache.ExtraCamsDisabled.Add(id);
                ValheimCpuPerfPlugin.Log?.LogInfo("0.8 renderer: disabled extra camera '" + n + "'");
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

        /// <summary>
        /// Force ShadowCastingMode.Off on clutter/vegetation MeshRenderers under grassroot
        /// and common veg name prefixes. Keeps player/building lights looking OK.
        /// </summary>
        private static void ScanVegetationShadowsOff()
        {
            ClutterSystem cs = ClutterSystem.instance;
            if (cs != null)
            {
                Transform root = null;
                try
                {
                    var go = Traverse.Create(cs).Field("m_grassRoot").GetValue<GameObject>();
                    if (go != null)
                        root = go.transform;
                }
                catch { root = null; }

                if (root != null)
                {
                    MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(true);
                    for (int i = 0; i < mrs.Length; i++)
                    {
                        MeshRenderer mr = mrs[i];
                        if (mr == null)
                            continue;
                        int id = mr.GetInstanceID();
                        if (RenderCache.VegShadowsOff.Contains(id))
                            continue;
                        if (mr.shadowCastingMode != ShadowCastingMode.Off)
                            mr.shadowCastingMode = ShadowCastingMode.Off;
                        mr.receiveShadows = false;
                        RenderCache.VegShadowsOff.Add(id);
                    }
                }
            }
        }
    }
}
