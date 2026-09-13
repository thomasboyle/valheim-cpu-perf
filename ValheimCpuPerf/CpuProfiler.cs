using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ValheimCpuPerf
{
    /// <summary>
    /// Lightweight rolling CPU sampler. Times Harmony-instrumented systems and
    /// compares against an 8.33ms frame budget (~120 FPS). Honest: does not guarantee 120 FPS.
    /// </summary>
    public static class CpuProfiler
    {
        public const double BudgetMs = 8.333;

        private class Bucket
        {
            public readonly string Name;
            public double AccumMs;
            public int Hits;
            public double WindowMs;
            public int WindowHits;
            public readonly Stopwatch Watch = new Stopwatch();

            public Bucket(string name) { Name = name; }
        }

        private static readonly Dictionary<string, Bucket> Buckets =
            new Dictionary<string, Bucket>(StringComparer.Ordinal);

        private static readonly object Gate = new object();
        private static float _windowSeconds = 2f;
        private static float _windowElapsed;
        private static double _frameTotalMs;
        private static readonly Stopwatch FrameWatch = new Stopwatch();

        // Cached last dump for overlay
        private static string[] _overlayLines = Array.Empty<string>();
        private static float _overlayRefreshAt;

        public static void Configure(float windowSeconds)
        {
            _windowSeconds = Mathf.Max(0.5f, windowSeconds);
        }

        public static void BeginFrame()
        {
            FrameWatch.Reset();
            FrameWatch.Start();
            _frameTotalMs = 0;
        }

        public static void EndFrame()
        {
            if (FrameWatch.IsRunning)
            {
                FrameWatch.Stop();
                _frameTotalMs = FrameWatch.Elapsed.TotalMilliseconds;
            }

            _windowElapsed += Time.unscaledDeltaTime;
            if (_windowElapsed >= _windowSeconds)
                RotateWindow();
        }

        private static Bucket Get(string name)
        {
            if (!Buckets.TryGetValue(name, out var b))
            {
                b = new Bucket(name);
                Buckets[name] = b;
            }
            return b;
        }

        public static void Enter(string name)
        {
            if (!ValheimCpuPerfPlugin.EnableProfiler.Value)
                return;
            lock (Gate)
            {
                var b = Get(name);
                b.Watch.Restart();
            }
        }

        public static void Exit(string name)
        {
            if (!ValheimCpuPerfPlugin.EnableProfiler.Value)
                return;
            lock (Gate)
            {
                if (!Buckets.TryGetValue(name, out var b))
                    return;
                if (!b.Watch.IsRunning)
                    return;
                b.Watch.Stop();
                var ms = b.Watch.Elapsed.TotalMilliseconds;
                b.AccumMs += ms;
                b.Hits++;
            }
        }

        private static void RotateWindow()
        {
            lock (Gate)
            {
                foreach (var b in Buckets.Values)
                {
                    b.WindowMs = b.AccumMs;
                    b.WindowHits = b.Hits;
                    b.AccumMs = 0;
                    b.Hits = 0;
                }
            }
            _windowElapsed = 0f;
            RefreshOverlayCache();
        }

        public static List<(string Name, double TotalMs, int Hits, double AvgMs)> Top(int n)
        {
            lock (Gate)
            {
                return Buckets.Values
                    .Where(b => b.WindowHits > 0 || b.Hits > 0)
                    .Select(b =>
                    {
                        var total = b.WindowHits > 0 ? b.WindowMs : b.AccumMs;
                        var hits = b.WindowHits > 0 ? b.WindowHits : b.Hits;
                        var avg = hits > 0 ? total / hits : 0;
                        return (b.Name, total, hits, avg);
                    })
                    .OrderByDescending(x => x.total)
                    .Take(n)
                    .ToList();
            }
        }

        public static void DumpTopBottlenecks(ManualLogSource log)
        {
            // Force a window snapshot from current accum if needed
            List<(string Name, double TotalMs, int Hits, double AvgMs)> top;
            lock (Gate)
            {
                var anyWindow = Buckets.Values.Any(b => b.WindowHits > 0);
                if (!anyWindow)
                {
                    foreach (var b in Buckets.Values)
                    {
                        b.WindowMs = b.AccumMs;
                        b.WindowHits = b.Hits;
                    }
                }
            }
            top = Top(3);
            var sb = new StringBuilder();
            sb.AppendLine($"[ValheimCpuPerf] Top CPU bottlenecks vs {BudgetMs:F2}ms budget (~120 FPS). Window≈{_windowSeconds:F1}s. NOT a hard FPS guarantee.");
            if (top.Count == 0)
            {
                sb.AppendLine("  (no samples yet — play in-world for a few seconds)");
            }
            else
            {
                var i = 1;
                foreach (var t in top)
                {
                    var pct = BudgetMs > 0 ? (t.TotalMs / (_windowSeconds * 1000.0 / (1.0 / (_windowSeconds))) ) : 0;
                    // Better: average per-frame contribution approx = totalMs / frames
                    var framesApprox = Mathf.Max(1f, _windowSeconds * 60f); // rough if uncapped
                    // Use Unity frame count proxy via delta
                    var perFrame = t.Hits > 0 ? t.AvgMs : 0;
                    sb.AppendLine($"  #{i} {t.Name}: total={t.TotalMs:F2}ms hits={t.Hits} avg/call={t.AvgMs:F3}ms | call-avg is {(t.AvgMs / BudgetMs * 100):F1}% of 8.33ms budget");
                    i++;
                }
            }
            sb.AppendLine($"  Tip: enable [Mitigations] EnableMitigations=true in BepInEx/config/{ValheimCpuPerfPlugin.PluginGuid}.cfg after identifying hotspots.");
            var msg = sb.ToString();
            log.LogInfo(msg);
            try { System.Console.WriteLine(msg); } catch { /* ignore */ }
            RefreshOverlayCache();
        }

        private static void RefreshOverlayCache()
        {
            var top = Top(3);
            var lines = new List<string>
            {
                $"ValheimCpuPerf — budget {BudgetMs:F2}ms (~120 FPS target, no guarantee)",
                $"Window {_windowSeconds:F1}s | F8 dump | F9 hide"
            };
            if (top.Count == 0)
            {
                lines.Add("(sampling… enter world)");
            }
            else
            {
                var i = 1;
                foreach (var t in top)
                {
                    lines.Add($"#{i} {t.Name}  avg {t.AvgMs:F3}ms/call  n={t.Hits}  tot {t.TotalMs:F1}ms");
                    i++;
                }
            }
            if (ValheimCpuPerfPlugin.EnableMitigations != null && ValheimCpuPerfPlugin.EnableMitigations.Value)
                lines.Add("Mitigations: ON");
            else
                lines.Add("Mitigations: OFF (config)");
            _overlayLines = lines.ToArray();
        }

        public static void DrawOverlay()
        {
            if (Time.unscaledTime >= _overlayRefreshAt)
            {
                RefreshOverlayCache();
                _overlayRefreshAt = Time.unscaledTime + 0.5f;
            }

            const float pad = 8f;
            float width = 520f;
            float lineH = 18f;
            float height = pad * 2 + lineH * Mathf.Max(1, _overlayLines.Length);
            var rect = new Rect(12f, 12f, width, height);
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.green }
            };
            for (int i = 0; i < _overlayLines.Length; i++)
            {
                GUI.Label(new Rect(rect.x + pad, rect.y + pad + i * lineH, width - pad * 2, lineH), _overlayLines[i], style);
            }
        }
    }

    /// <summary>RAII-ish helper for try/finally timing in patches.</summary>
    public struct ProfileScope : IDisposable
    {
        private readonly string _name;
        public ProfileScope(string name)
        {
            _name = name;
            CpuProfiler.Enter(name);
        }
        public void Dispose() => CpuProfiler.Exit(_name);
    }
}
