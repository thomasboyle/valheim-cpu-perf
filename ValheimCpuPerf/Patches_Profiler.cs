using HarmonyLib;
using UnityEngine;

namespace ValheimCpuPerf.Patches
{
    // Method names verified via Mono.Cecil against assembly_valheim.dll

    [HarmonyPatch(typeof(ZoneSystem), "Update")]
    internal static class ZoneSystem_Profiler
    {
        [HarmonyPrefix] private static void Prefix() => CpuProfiler.Enter("ZoneSystem.Update");
        [HarmonyPostfix] private static void Postfix() => CpuProfiler.Exit("ZoneSystem.Update");
    }

    [HarmonyPatch(typeof(ZNetScene), "Update")]
    internal static class ZNetScene_Update_Profiler
    {
        [HarmonyPrefix] private static void Prefix() => CpuProfiler.Enter("ZNetScene.Update");
        [HarmonyPostfix] private static void Postfix() => CpuProfiler.Exit("ZNetScene.Update");
    }

    [HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
    internal static class ZNetScene_CDO_Profiler
    {
        [HarmonyPrefix] private static void Prefix() => CpuProfiler.Enter("ZNetScene.CreateDestroyObjects");
        [HarmonyPostfix] private static void Postfix() => CpuProfiler.Exit("ZNetScene.CreateDestroyObjects");
    }

    [HarmonyPatch(typeof(ClutterSystem), "LateUpdate")]
    internal static class ClutterSystem_Profiler
    {
        [HarmonyPrefix] private static void Prefix() => CpuProfiler.Enter("ClutterSystem.LateUpdate");
        [HarmonyPostfix] private static void Postfix() => CpuProfiler.Exit("ClutterSystem.LateUpdate");
    }

    [HarmonyPatch(typeof(ClutterSystem), "UpdateGrass")]
    internal static class ClutterSystem_UpdateGrass_Profiler
    {
        [HarmonyPrefix] private static void Prefix() => CpuProfiler.Enter("ClutterSystem.UpdateGrass");
        [HarmonyPostfix] private static void Postfix() => CpuProfiler.Exit("ClutterSystem.UpdateGrass");
    }

    // Smoke uses CustomUpdate(dt, time), not Unity Update
    [HarmonyPatch(typeof(Smoke), "CustomUpdate")]
    internal static class Smoke_Profiler
    {
        [HarmonyPrefix] private static void Prefix() => CpuProfiler.Enter("Smoke.CustomUpdate");
        [HarmonyPostfix] private static void Postfix() => CpuProfiler.Exit("Smoke.CustomUpdate");
    }

    [HarmonyPatch(typeof(ParticleMist), "Update")]
    internal static class ParticleMist_Profiler
    {
        [HarmonyPrefix] private static void Prefix() => CpuProfiler.Enter("ParticleMist.Update");
        [HarmonyPostfix] private static void Postfix() => CpuProfiler.Exit("ParticleMist.Update");
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.UpdateAI))]
    internal static class BaseAI_Profiler
    {
        [HarmonyPrefix] private static void Prefix() => CpuProfiler.Enter("BaseAI.UpdateAI");
        [HarmonyPostfix] private static void Postfix() => CpuProfiler.Exit("BaseAI.UpdateAI");
    }

    [HarmonyPatch(typeof(Heightmap), "LateUpdate")]
    internal static class Heightmap_Profiler
    {
        [HarmonyPrefix] private static void Prefix() => CpuProfiler.Enter("Heightmap.LateUpdate");
        [HarmonyPostfix] private static void Postfix() => CpuProfiler.Exit("Heightmap.LateUpdate");
    }

    [HarmonyPatch(typeof(Heightmap), "UpdateShadowSettings")]
    internal static class Heightmap_Shadow_Profiler
    {
        [HarmonyPrefix] private static void Prefix() => CpuProfiler.Enter("Heightmap.UpdateShadowSettings");
        [HarmonyPostfix] private static void Postfix() => CpuProfiler.Exit("Heightmap.UpdateShadowSettings");
    }

    [HarmonyPatch(typeof(EnvMan), "Update")]
    internal static class EnvMan_Profiler
    {
        [HarmonyPrefix] private static void Prefix() => CpuProfiler.Enter("EnvMan.Update");
        [HarmonyPostfix] private static void Postfix() => CpuProfiler.Exit("EnvMan.Update");
    }

    [HarmonyPatch(typeof(SpawnSystem), "UpdateSpawning")]
    internal static class SpawnSystem_Profiler
    {
        [HarmonyPrefix] private static void Prefix() => CpuProfiler.Enter("SpawnSystem.UpdateSpawning");
        [HarmonyPostfix] private static void Postfix() => CpuProfiler.Exit("SpawnSystem.UpdateSpawning");
    }

    [HarmonyPatch(typeof(TerrainComp), "Update")]
    internal static class TerrainComp_Profiler
    {
        [HarmonyPrefix] private static void Prefix() => CpuProfiler.Enter("TerrainComp.Update");
        [HarmonyPostfix] private static void Postfix() => CpuProfiler.Exit("TerrainComp.Update");
    }
}
