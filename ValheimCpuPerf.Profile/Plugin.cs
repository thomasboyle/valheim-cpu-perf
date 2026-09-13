using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ValheimCpuPerf.Profile
{
    /// <summary>
    /// TEMPORARY in-process managed sampler. Does NOT apply soft-cap tweaks.
    /// Deploy as ValheimCpuPerf.Profile.dll; remove after capture.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class ProfilePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.thomasboyle.valheimcpuperf.profile";
        public const string PluginName = "ValheimCpuPerf.Profile";
        public const string PluginVersion = "0.3.0-profile";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;
        private float _nextDump;
        private const float DumpIntervalSec = 18f;
        private string _outDir;

        private void Awake()
        {
            Log = Logger;
            _outDir = @"D:\C++\120fpsvalheim\profile";
            try { Directory.CreateDirectory(_outDir); } catch { /* ignore */ }

            _harmony = new Harmony(PluginGuid);
            int patched = ManagedSampler.ApplyPatches(_harmony);
            _nextDump = Time.realtimeSinceStartup + DumpIntervalSec;

            Log.LogInfo($"{PluginName} {PluginVersion} loaded — PROFILE ONLY (no soft caps). Patched {patched} methods. CSV -> {_outDir}");
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < _nextDump)
                return;
            _nextDump = Time.realtimeSinceStartup + DumpIntervalSec;
            string path = ManagedSampler.DumpCsv(_outDir, "periodic");
            ManagedSampler.LogTop(15);
            bool inWorld = Player.m_localPlayer != null;
            Log.LogInfo($"Sampler dump: {path} | inWorld={inWorld} | frame={Time.frameCount}");
        }

        private void OnDestroy()
        {
            try
            {
                string path = ManagedSampler.DumpCsv(_outDir, "destroy");
                ManagedSampler.LogTop(15);
                Log.LogInfo($"Final sampler dump: {path}");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Final dump failed: {ex.Message}");
            }
            _harmony?.UnpatchSelf();
        }
    }

    internal static class ManagedSampler
    {
        private sealed class Agg
        {
            public long Calls;
            public long TotalTicks;
            public long MaxTicks;
        }

        private static readonly Dictionary<string, Agg> Stats = new Dictionary<string, Agg>(256);
        private static readonly object Gate = new object();
        private static readonly double TickToMs = 1000.0 / Stopwatch.Frequency;

        [ThreadStatic] private static Stack<long> _tsStack;
        [ThreadStatic] private static Stack<string> _keyStack;

        private static readonly string[][] Candidates =
        {
            new[] { "ClutterSystem", "LateUpdate" },
            new[] { "ClutterSystem", "UpdateGrass" },
            new[] { "ClutterSystem", "Awake" },
            new[] { "ParticleMist", "Update" },
            new[] { "Smoke", "CustomUpdate" },
            new[] { "SmokeSpawner", "CustomUpdate" },
            new[] { "BaseAI", "UpdateAI" },
            new[] { "MonsterAI", "UpdateAI" },
            new[] { "MonsterAI", "UpdateTarget" },
            new[] { "AnimalAI", "UpdateAI" },
            new[] { "Character", "CustomFixedUpdate" },
            new[] { "Character", "UpdateWalking" },
            new[] { "Character", "UpdateGroundTilt" },
            new[] { "Character", "UpdateSwimming" },
            new[] { "Character", "UpdateMotion" },
            new[] { "ZoneSystem", "Update" },
            new[] { "Heightmap", "LateUpdate" },
            new[] { "Heightmap", "CustomLateUpdate" },
            new[] { "Heightmap", "Regenerate" },
            new[] { "ZNetScene", "Update" },
            new[] { "ZNetScene", "CreateDestroyObjects" },
            new[] { "ZDOMan", "Update" },
            new[] { "ZNet", "Update" },
            new[] { "WearNTear", "UpdateWear" },
            new[] { "WearNTear", "UpdateSupport" },
            new[] { "Minimap", "Update" },
            new[] { "Minimap", "UpdateMap" },
            new[] { "WaterVolume", "StaticUpdate" },
            new[] { "WaterVolume", "UpdateFloaters" },
            new[] { "Ship", "CustomFixedUpdate" },
            new[] { "FootStep", "CustomUpdate" },
            new[] { "EffectArea", "CustomFixedUpdate" },
            new[] { "FejdStartup", "Update" },
            new[] { "Player", "Update" },
            new[] { "Player", "FixedUpdate" },
            new[] { "Player", "UpdatePlacementGhost" },
            new[] { "Player", "UpdateStats" },
            new[] { "EnvMan", "Update" },
            new[] { "EnvMan", "FixedUpdate" },
            new[] { "SpawnSystem", "UpdateSpawning" },
            new[] { "TerrainComp", "Update" },
            new[] { "VisEquipment", "CustomUpdate" },
            new[] { "VisEquipment", "UpdateEquipmentVisuals" },
            new[] { "ZSyncTransform", "CustomFixedUpdate" },
            new[] { "ZSyncAnimation", "CustomFixedUpdate" },
            new[] { "Humanoid", "CustomFixedUpdate" },
            new[] { "SEMan", "Update" },
            new[] { "StaticPhysics", "SUpdate" },
            new[] { "Fish", "CustomFixedUpdate" },
            new[] { "RandomFlyingBird", "CustomFixedUpdate" },
            new[] { "AudioMan", "Update" },
            new[] { "AudioMan", "FixedUpdate" },
            new[] { "MusicMan", "Update" },
            new[] { "GameCamera", "LateUpdate" },
            new[] { "GameCamera", "UpdateCamera" },
            new[] { "Hud", "Update" },
            new[] { "Chat", "Update" },
            new[] { "MessageHud", "Update" },
            new[] { "InventoryGui", "Update" },
            new[] { "ShieldGenerator", "Update" },
            new[] { "Fireplace", "UpdateState" },
            new[] { "Smelter", "UpdateSmelter" },
            new[] { "CraftingStation", "CustomUpdate" },
            new[] { "Tameable", "Update" },
            new[] { "CharacterAnimEvent", "CustomFixedUpdate" },
            new[] { "CharacterAnimEvent", "UpdateFootIK" },
            new[] { "CharacterAnimEvent", "CustomLateUpdate" },
        };

        public static int ApplyPatches(Harmony harmony)
        {
            var prefix = new HarmonyMethod(typeof(ManagedSampler).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic));
            var postfix = new HarmonyMethod(typeof(ManagedSampler).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic));
            int count = 0;
            var asm = typeof(Player).Assembly;

            foreach (var pair in Candidates)
            {
                string typeName = pair[0];
                string methodName = pair[1];
                Type t = asm.GetType(typeName);
                if (t == null)
                {
                    ProfilePlugin.Log.LogWarning($"Skip missing type: {typeName}");
                    continue;
                }
                MethodInfo[] methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                bool any = false;
                foreach (MethodInfo m in methods)
                {
                    if (m.Name != methodName)
                        continue;
                    if (m.IsAbstract || m.ContainsGenericParameters)
                        continue;
                    try
                    {
                        harmony.Patch(m, prefix: prefix, postfix: postfix);
                        EnsureAgg(KeyFor(m));
                        count++;
                        any = true;
                        ProfilePlugin.Log.LogInfo($"Patched {t.Name}.{m.Name}({ParamSig(m)})");
                    }
                    catch (Exception ex)
                    {
                        ProfilePlugin.Log.LogWarning($"Patch fail {t.Name}.{m.Name}: {ex.Message}");
                    }
                }
                if (!any)
                    ProfilePlugin.Log.LogWarning($"Skip missing method: {typeName}.{methodName}");
            }

            // Lightweight scan: a few more DeclaredOnly Update* on high-instance types already covered.
            return count;
        }

        private static string ParamSig(MethodInfo m)
        {
            var ps = m.GetParameters();
            if (ps.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(ps[i].ParameterType.Name);
            }
            return sb.ToString();
        }

        private static string KeyFor(MethodBase m)
        {
            return m.DeclaringType != null ? (m.DeclaringType.Name + "." + m.Name) : m.Name;
        }

        private static void EnsureAgg(string key)
        {
            lock (Gate)
            {
                if (!Stats.ContainsKey(key))
                    Stats[key] = new Agg();
            }
        }

        private static void Prefix(MethodBase __originalMethod)
        {
            if (_tsStack == null) _tsStack = new Stack<long>(8);
            if (_keyStack == null) _keyStack = new Stack<string>(8);
            _tsStack.Push(Stopwatch.GetTimestamp());
            _keyStack.Push(KeyFor(__originalMethod));
        }

        private static void Postfix()
        {
            if (_tsStack == null || _tsStack.Count == 0 || _keyStack == null || _keyStack.Count == 0)
                return;
            long start = _tsStack.Pop();
            string key = _keyStack.Pop();
            long elapsed = Stopwatch.GetTimestamp() - start;
            if (elapsed < 0) elapsed = 0;

            lock (Gate)
            {
                Agg a;
                if (!Stats.TryGetValue(key, out a))
                {
                    a = new Agg();
                    Stats[key] = a;
                }
                a.Calls++;
                a.TotalTicks += elapsed;
                if (elapsed > a.MaxTicks)
                    a.MaxTicks = elapsed;
            }
        }

        public static string DumpCsv(string outDir, string tag)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(outDir, "managed_hotspots_" + stamp + ".csv");
            List<KeyValuePair<string, Agg>> snapshot;
            lock (Gate)
            {
                snapshot = Stats.Select(kv => new KeyValuePair<string, Agg>(kv.Key, new Agg
                {
                    Calls = kv.Value.Calls,
                    TotalTicks = kv.Value.TotalTicks,
                    MaxTicks = kv.Value.MaxTicks
                })).ToList();
            }

            double totalMsAll = snapshot.Sum(kv => kv.Value.TotalTicks * TickToMs);
            var ranked = snapshot.OrderByDescending(kv => kv.Value.TotalTicks).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("method,calls,total_ms,avg_ms,max_ms,pct_of_sampled");
            foreach (var kv in ranked)
            {
                double totalMs = kv.Value.TotalTicks * TickToMs;
                double avgMs = kv.Value.Calls > 0 ? totalMs / kv.Value.Calls : 0;
                double maxMs = kv.Value.MaxTicks * TickToMs;
                double pct = totalMsAll > 0 ? (100.0 * totalMs / totalMsAll) : 0;
                sb.Append(kv.Key).Append(',')
                  .Append(kv.Value.Calls).Append(',')
                  .Append(totalMs.ToString("F3")).Append(',')
                  .Append(avgMs.ToString("F6")).Append(',')
                  .Append(maxMs.ToString("F3")).Append(',')
                  .Append(pct.ToString("F2"))
                  .AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

            // Also write a stable latest pointer
            try
            {
                File.WriteAllText(Path.Combine(outDir, "managed_hotspots_LATEST.txt"), path + Environment.NewLine + "tag=" + tag + Environment.NewLine + "total_sampled_ms=" + totalMsAll.ToString("F1"), Encoding.UTF8);
            }
            catch { /* ignore */ }

            return path;
        }

        public static void LogTop(int n)
        {
            List<KeyValuePair<string, Agg>> ranked;
            lock (Gate)
            {
                ranked = Stats.OrderByDescending(kv => kv.Value.TotalTicks).Take(n).ToList();
            }
            double totalMsAll;
            lock (Gate)
            {
                totalMsAll = Stats.Values.Sum(a => a.TotalTicks * TickToMs);
            }
            ProfilePlugin.Log.LogInfo($"=== Managed hotspot TOP {n} (sampled total {totalMsAll:F1} ms) ===");
            int i = 1;
            foreach (var kv in ranked)
            {
                double totalMs = kv.Value.TotalTicks * TickToMs;
                double avgMs = kv.Value.Calls > 0 ? totalMs / kv.Value.Calls : 0;
                double pct = totalMsAll > 0 ? (100.0 * totalMs / totalMsAll) : 0;
                ProfilePlugin.Log.LogInfo($"  #{i} {kv.Key}: calls={kv.Value.Calls} total={totalMs:F1}ms avg={avgMs:F4}ms max={kv.Value.MaxTicks * TickToMs:F2}ms pct={pct:F1}%");
                i++;
            }
        }
    }
}
