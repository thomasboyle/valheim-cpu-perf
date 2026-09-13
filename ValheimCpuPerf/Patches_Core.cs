using HarmonyLib;
using UnityEngine;

namespace ValheimCpuPerf.Patches
{
    /// <summary>
    /// Always-on structural early-outs for LIVE managed top bottlenecks
    /// (docs/MANAGED_HOTSPOTS.md). Performance only - no gameplay cheats.
    /// </summary>
    internal static class HotFields
    {
        internal static readonly AccessTools.FieldRef<ZSyncTransform, ZNetView> ZSyncNView =
            AccessTools.FieldRefAccess<ZSyncTransform, ZNetView>("m_nview");

        internal static readonly AccessTools.FieldRef<ZSyncTransform, Character> ZSyncCharacter =
            AccessTools.FieldRefAccess<ZSyncTransform, Character>("m_character");

        internal static readonly AccessTools.FieldRef<ZSyncTransform, Projectile> ZSyncProjectile =
            AccessTools.FieldRefAccess<ZSyncTransform, Projectile>("m_projectile");

        internal static readonly AccessTools.FieldRef<WaterVolume, Collider> WaterCollider =
            AccessTools.FieldRefAccess<WaterVolume, Collider>("m_collider");

        internal static readonly AccessTools.FieldRef<Fish, ZNetView> FishNView =
            AccessTools.FieldRefAccess<Fish, ZNetView>("m_nview");

        internal static readonly AccessTools.FieldRef<Fish, Rigidbody> FishBody =
            AccessTools.FieldRefAccess<Fish, Rigidbody>("m_body");

        internal static readonly AccessTools.FieldRef<Fish, long> FishLastOwner =
            AccessTools.FieldRefAccess<Fish, long>("m_lastOwner");

        internal static readonly AccessTools.FieldRef<Smoke, float> SmokeTime =
            AccessTools.FieldRefAccess<Smoke, float>("m_time");

        internal static readonly AccessTools.FieldRef<Smoke, float> SmokeFadeTimer =
            AccessTools.FieldRefAccess<Smoke, float>("m_fadeTimer");

        internal static readonly System.Action<Fish, bool> FishSetVisible =
            AccessTools.MethodDelegate<System.Action<Fish, bool>>(
                AccessTools.Method(typeof(Fish), "SetVisible", new[] { typeof(bool) }));

        internal static readonly System.Action<Smoke> SmokeStartFadeOut =
            AccessTools.MethodDelegate<System.Action<Smoke>>(
                AccessTools.Method(typeof(Smoke), "StartFadeOut", System.Type.EmptyTypes));

        internal static readonly AccessTools.FieldRef<Character, ZNetView> CharacterNView =
            AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        internal static readonly System.Action<Character, bool> CharacterSetVisible =
            AccessTools.MethodDelegate<System.Action<Character, bool>>(
                AccessTools.Method(typeof(Character), "SetVisible", new[] { typeof(bool) }));

        /// <summary>Beyond this distance, non-character/projectile transforms sync 1/3 frames.</summary>
        internal const float DistantSyncMeters = 64f;

        /// <summary>Beyond this distance, non-character/projectile transforms sync 1/6 frames.</summary>
        internal const float VeryDistantSyncMeters = 128f;

        /// <summary>Beyond this distance from water collider, skip floater liquid updates.</summary>
        internal const float DistantWaterMeters = 48f;

        /// <summary>Beyond this distance, smoke skips Rigidbody force work (timer/fade only).</summary>
        internal const float DistantSmokeMeters = 64f;

        /// <summary>
        /// Beyond this distance, non-owner Character.CustomFixedUpdate keeps SetVisible only
        /// (skip liquid/effects/tilt/look cosmetics). Owners always full-rate.
        /// </summary>
        internal const float DistantCharacterMeters = 64f;

        internal const int DistantSyncPeriod = 3;
        internal const int VeryDistantSyncPeriod = 6;
    }

    /// <summary>
    /// #1 bottleneck: ZSyncTransform.CustomFixedUpdate -> ClientSync.
    /// ClientSync already no-ops for owners; skipping the call is correctness-preserving.
    /// Distant static (no Character/Projectile) objects do not need full-rate client sync.
    /// v0.4: second distance tier at 128 m (1/6 frames) for larger client-sync savings.
    /// Owner path remains on CustomLateUpdate -> OwnerSync (untouched).
    /// </summary>
    [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.CustomFixedUpdate))]
    internal static class ZSyncTransform_ClientGate
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ZSyncTransform __instance)
        {
            if (__instance == null)
                return true;

            ZNetView nv = HotFields.ZSyncNView(__instance);
            if (nv != null && nv.IsOwner())
                return false; // ClientSync would return immediately

            Character ch = HotFields.ZSyncCharacter(__instance);
            Projectile proj = HotFields.ZSyncProjectile(__instance);
            if (ch != null || proj != null)
                return true; // keep full-rate for characters / projectiles

            Player player = Player.m_localPlayer;
            if (player == null)
                return true;

            Vector3 delta = player.transform.position - __instance.transform.position;
            float sqr = delta.sqrMagnitude;
            float veryFar = HotFields.VeryDistantSyncMeters * HotFields.VeryDistantSyncMeters;
            float far = HotFields.DistantSyncMeters * HotFields.DistantSyncMeters;

            if (sqr < far)
                return true;

            int id = __instance.GetInstanceID();
            int period = sqr >= veryFar
                ? HotFields.VeryDistantSyncPeriod
                : HotFields.DistantSyncPeriod;
            return ((Time.frameCount + id) % period) == 0;
        }
    }

    /// <summary>
    /// #3 bottleneck: WaterVolume.UpdateFloaters (GetWaterSurface/CalcWave per floater).
    /// If the closest point on this volume's collider is far from the local player,
    /// floater liquid-level updates cannot affect local gameplay - skip entirely.
    /// Visual water time / wind still update via WaterVolume.StaticUpdate (unpatched).
    /// </summary>
    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.UpdateFloaters))]
    internal static class WaterVolume_DistantGate
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(WaterVolume __instance)
        {
            if (__instance == null)
                return true;

            Player player = Player.m_localPlayer;
            if (player == null)
                return true;

            Vector3 playerPos = player.transform.position;
            Collider col = HotFields.WaterCollider(__instance);
            Vector3 closest;
            if (col != null)
            {
                closest = col.ClosestPoint(playerPos);
            }
            else
            {
                closest = __instance.transform.position;
            }

            float limit = HotFields.DistantWaterMeters * HotFields.DistantWaterMeters;
            return (playerPos - closest).sqrMagnitude <= limit;
        }
    }

    /// <summary>
    /// Rank #4: Smoke.CustomUpdate (~5.2% steady-state). Full path updates Rigidbody mass/forces
    /// every frame. Beyond DistantSmokeMeters, only advance TTL/fade/destroy (same end state as
    /// vanilla) and skip physics forces - distant smoke cannot affect gameplay.
    /// </summary>
    [HarmonyPatch(typeof(Smoke), nameof(Smoke.CustomUpdate))]
    internal static class Smoke_DistantLite
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Smoke __instance, float deltaTime)
        {
            if (__instance == null)
                return true;

            Player player = Player.m_localPlayer;
            if (player == null)
                return true;

            Vector3 delta = player.transform.position - __instance.transform.position;
            float limit = HotFields.DistantSmokeMeters * HotFields.DistantSmokeMeters;
            if (delta.sqrMagnitude <= limit)
                return true;

            // Timer / fade / destroy only (mirrors CustomUpdate without Rigidbody work).
            float time = HotFields.SmokeTime(__instance) + deltaTime;
            HotFields.SmokeTime(__instance) = time;

            if (time > __instance.m_ttl && HotFields.SmokeFadeTimer(__instance) < 0f)
                HotFields.SmokeStartFadeOut(__instance);

            float fadeTimer = HotFields.SmokeFadeTimer(__instance);
            if (fadeTimer >= 0f)
            {
                fadeTimer += deltaTime;
                HotFields.SmokeFadeTimer(__instance) = fadeTimer;
                if (fadeTimer >= __instance.m_fadetime)
                    Object.Destroy(__instance.gameObject);
            }

            return false;
        }
    }

    /// <summary>
    /// Rank #5: Fish.CustomFixedUpdate (~3.7%). Vanilla already returns for non-owners after
    /// paying for water Depth/CalcWave + collision bookkeeping. Move that early-out earlier
    /// while preserving SetVisible, owner-change WakeUp, and hooked spectator jump VFX.
    /// Owner swim AI is untouched.
    /// </summary>
    [HarmonyPatch(typeof(Fish), nameof(Fish.CustomFixedUpdate))]
    internal static class Fish_NonOwnerGate
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Fish __instance)
        {
            if (__instance == null)
                return true;

            ZNetView nv = HotFields.FishNView(__instance);
            if (nv == null || !nv.IsValid())
                return true; // vanilla returns immediately

            if (nv.IsOwner())
                return true; // full owner AI / swim

            // --- non-owner lightweight duties (same side effects as vanilla pre-ret) ---
            HotFields.FishSetVisible(__instance, nv.HasOwner());

            ZDO zdo = nv.GetZDO();
            if (zdo != null)
            {
                long owner = zdo.GetOwner();
                if (HotFields.FishLastOwner(__instance) != owner)
                {
                    HotFields.FishLastOwner(__instance) = owner;
                    Rigidbody body = HotFields.FishBody(__instance);
                    if (body != null)
                        body.WakeUp();
                }

                // Spectator hooked struggle VFX (rare). Fall through to vanilla if active so
                // we do not have to re-implement IsOutOfWater / effect spawn edge cases mid-bite.
                if (zdo.GetInt(ZDOVars.s_hooked, 0) == 1 && zdo.GetFloat(ZDOVars.s_escape, 0f) > 0f)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Post-0.4 top hotspot: Character.CustomFixedUpdate (~11% delta).
    /// IL: owners run full motion/combat sim; non-owners still always run CalculateLiquidDepth,
    /// UpdateContinousEffects, UpdateGroundTilt (client ZDO tilt lerp), SetVisible, look, etc.
    /// Beyond DistantCharacterMeters those cosmetics cannot affect local gameplay - keep only
    /// SetVisible(HasOwner) (LOD ownership bookkeeping), matching vanilla visibility side effect.
    /// Owners (local player + any owned AI) are never gated. Near non-owners unchanged.
    /// Non-humanoid Characters only for the exclusive cost; Humanoids also use
    /// Humanoid_DistantNonOwnerLite (skips UpdateUseVisual + this call). This patch still
    /// covers animals/etc. and remains a safety net if Humanoid falls through.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
    internal static class Character_DistantNonOwnerLite
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Character __instance)
        {
            if (__instance == null)
                return true;

            ZNetView nv = HotFields.CharacterNView(__instance);
            if (nv == null || !nv.IsValid())
                return true;

            if (__instance.IsOwner())
                return true;

            Player player = Player.m_localPlayer;
            if (player == null)
                return true;

            Vector3 delta = player.transform.position - __instance.transform.position;
            float limit = HotFields.DistantCharacterMeters * HotFields.DistantCharacterMeters;
            if (delta.sqrMagnitude <= limit)
                return true;

            HotFields.CharacterSetVisible(__instance, nv.HasOwner());
            return false;
        }
    }

    /// <summary>
    /// Post-0.4 hotspot: Humanoid.CustomFixedUpdate (~10% inclusive).
    /// IL: non-owners skip Attack/Equipment/Block but always run UpdateUseVisual (equip VFX /
    /// hand visual) then non-virtual Character.CustomFixedUpdate. Beyond DistantCharacterMeters
    /// those cosmetics cannot affect local gameplay - keep only SetVisible(HasOwner), matching
    /// Character_DistantNonOwnerLite. Owners (local player) never gated; near non-owners unchanged.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.CustomFixedUpdate))]
    internal static class Humanoid_DistantNonOwnerLite
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Humanoid __instance)
        {
            if (__instance == null)
                return true;

            ZNetView nv = HotFields.CharacterNView(__instance);
            if (nv == null || !nv.IsValid())
                return true;

            if (__instance.IsOwner())
                return true;

            Player player = Player.m_localPlayer;
            if (player == null)
                return true;

            Vector3 delta = player.transform.position - __instance.transform.position;
            float limit = HotFields.DistantCharacterMeters * HotFields.DistantCharacterMeters;
            if (delta.sqrMagnitude <= limit)
                return true;

            HotFields.CharacterSetVisible(__instance, nv.HasOwner());
            return false;
        }
    }

    // ZNetScene.CreateDestroyObjects: re-checked for 0.5.1 - still NO safe fix.
    // Update hardcodes 1/30s (m_createDestroyFps=30 field unused at runtime).
    // CreateObjects hardcodes max 10/frame (m_maxCreatedPerFrame unused).
    // RemoveObjects always earmarks then walks ALL m_instances - no incremental path.
    // ZDOMan.m_dirtyChunks is SAVE-ONLY; m_clientChangeQueue is ZDO sync SendZDOs only,
    // not sector-membership dirty. Zone-unchanged skip would delay MP pop-in/despawn.
    // StaticPhysics already ShouldUpdate + OutsideActiveArea via SlowUpdater (100/frame + 0.1s).
    // See docs/DEEP_PROFILE_NEXT.md.
}
