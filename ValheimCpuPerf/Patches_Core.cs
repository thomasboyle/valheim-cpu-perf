using HarmonyLib;
using UnityEngine;

namespace ValheimCpuPerf.Patches
{
    /// <summary>
    /// Always-on structural early-outs for LIVE managed top bottlenecks
    /// (docs/MANAGED_HOTSPOTS.md). Performance only — no gameplay cheats.
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

        /// <summary>Beyond this distance, non-character/projectile transforms sync 1/3 frames.</summary>
        internal const float DistantSyncMeters = 64f;

        /// <summary>Beyond this distance from water collider, skip floater liquid updates.</summary>
        internal const float DistantWaterMeters = 48f;

        internal const int DistantSyncPeriod = 3;
    }

    /// <summary>
    /// #1 bottleneck: ZSyncTransform.CustomFixedUpdate → ClientSync.
    /// ClientSync already no-ops for owners; skipping the call is correctness-preserving.
    /// Distant static (no Character/Projectile) objects do not need full-rate client sync.
    /// Owner path remains on CustomLateUpdate → OwnerSync (untouched).
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
            float limit = HotFields.DistantSyncMeters * HotFields.DistantSyncMeters;
            if (delta.sqrMagnitude < limit)
                return true;

            int id = __instance.GetInstanceID();
            return ((Time.frameCount + id) % HotFields.DistantSyncPeriod) == 0;
        }
    }

    /// <summary>
    /// #3 bottleneck: WaterVolume.UpdateFloaters (GetWaterSurface/CalcWave per floater).
    /// If the closest point on this volume's collider is far from the local player,
    /// floater liquid-level updates cannot affect local gameplay — skip entirely.
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

    // ZNetScene.CreateDestroyObjects (#2): no safe definitive Harmony fix shipped —
    // reducing create/destroy rate risks multiplayer pop-in / despawn lag.
    // See docs/MANAGED_HOTSPOTS.md.
}
