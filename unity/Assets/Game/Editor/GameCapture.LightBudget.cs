using System.Collections.Generic;
using Stabby.Viewer;
using Stabby.World;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const int BudgetLights = 40, BudgetRuns = 5;

        static System.Collections.IEnumerator LightBudgetTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 60f;
            var lines = new List<string>();
            var ahead = Vector3.forward;
            var site = home;
            float flattest = float.MaxValue;
            for (float gx = -600f; gx <= 600f; gx += 100f)
                for (float gz = -600f; gz <= 600f; gz += 100f)
                {
                    float x0 = home.x + gx, z0 = home.z + gz, h0 = GroundHeight(x0, z0);
                    if (h0 < 2f) continue;
                    for (int a = 0; a < 16; a++)
                    {
                        var d = Quaternion.Euler(0, a * 22.5f, 0) * Vector3.forward;
                        var sd = Vector3.Cross(Vector3.up, d);
                        float rough = 0f;
                        for (float r = 10f; r <= 80f; r += 10f)
                            for (float sx = -0.6f; sx <= 0.61f; sx += 0.6f)
                            {
                                float h = GroundHeight(x0 + (d.x + sd.x * sx) * r, z0 + (d.z + sd.z * sx) * r);
                                rough += Mathf.Abs(h - h0 + 0.05f * r) + (h < 1f ? 100f : 0f);
                            }
                        if (rough < flattest) { flattest = rough; ahead = d; site = new Vector3(x0, h0, z0); }
                    }
                }
            var eye = new Vector3(site.x, GroundHeight(site.x, site.z) + 2.5f, site.z);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(ahead + Vector3.down * 0.12f));
            for (var w = ShowHour(NightHour, 0f); w.MoveNext();) yield return null;
            for (var w = Settle(360); w.MoveNext();) yield return null;
            var world = ViewWorld();
            if (world == null) { GameStatus.Failure = "lightbudget: no client view world"; yield break; }
            var em = world.EntityManager;
            var stateQuery = em.CreateEntityQuery(typeof(LightBudgetState));
            for (int k = 0; k < 600 && stateQuery.IsEmpty; k++) yield return null;
            if (stateQuery.IsEmpty) { GameStatus.Failure = "lightbudget: LightPresenterSystem never created the budget"; yield break; }
            var presenter = world.GetExistingSystemManaged<LightPresenterSystem>();
            var objects = ContentFiles.Objects(ContentFiles.Biomes());
            var caps = stateQuery.GetSingleton<LightBudgetState>().Caps;
            var right = Vector3.Cross(Vector3.up, ahead).normalized;
            var kinds = new[] { "point", "fire", "candle" };
            for (int k = 0; k < BudgetLights; k++)
            {
                float t = k / (float)(BudgetLights - 1);
                float r = 5f + 75f * Mathf.Pow(t, 1.3f);
                float side = (Mathf.Repeat(k * 0.618034f, 1f) - 0.5f) * 1.4f * r;
                var at = eye + ahead * r + right * side;
                LightKind kind = null;
                foreach (var l in objects.Lights) if (l.Id == kinds[k % 3]) kind = l;
                if (kind == null) { GameStatus.Failure = "lightbudget: light kind " + kinds[k % 3] + " missing from content"; yield break; }
                float lift = k % 3 == 0 ? 1.6f : k % 3 == 1 ? 0.4f : 0.9f;
                var pos = new float3(at.x, GroundHeight(at.x, at.z) + lift, at.z);
                var e = k % 3 == 0 ? LightEmitter.Of(kind, pos, new float3(1f, 0.72f, 0.42f), 9f, 6f)
                    : k % 3 == 1 ? LightEmitter.Of(kind, pos, new float3(1f, 0.48f, 0.18f), 12f, 9f)
                    : LightEmitter.Of(kind, pos, new float3(1f, 0.78f, 0.5f), 5f, 2f);
                var entity = em.CreateEntity();
                em.AddComponentData(entity, e);
            }
            var s = DayNightSystem.Shown;
            lines.Add($"new moon night hour {s.Hour:F2}: {(s.Moon ? "moon" : "sun")} light {s.Intensity:F4}, ambient sky {s.AmbientSky.B:F4}; {BudgetLights} lights 5-80 m ahead of eye {eye.x:F0},{eye.y:F0},{eye.z:F0}; caps shadowed {caps.Shadowed}, unshadowed {caps.Unshadowed}, glow {caps.Glow}");

            for (var w = SetLights(stateQuery, presenter, true, false); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "lightbudget-dark");
            for (var w = SetLights(stateQuery, presenter, false, false); w.MoveNext();) yield return null;
            lines.Add("tiers off: " + TierLine(stateQuery, presenter));
            Shot(cam, null, "warmup");
            Shot(cam, dir, "lightbudget-off");
            for (var w = SetLights(stateQuery, presenter, false, true); w.MoveNext();) yield return null;
            lines.Add("tiers on: " + TierLine(stateQuery, presenter));
            Shot(cam, null, "warmup");
            Shot(cam, dir, "lightbudget-on");

            int peakS = 0, peakR = 0, peakG = 0, frames = 0;
            var start = cam.transform.position;
            for (int k = 0; k <= 240; k++)
            {
                float u = k / 240f;
                cam.transform.position = start + ahead * (40f * u) + right * (12f * Mathf.Sin(u * 6.2831853f));
                cam.transform.position = new Vector3(cam.transform.position.x, GroundHeight(cam.transform.position.x, cam.transform.position.z) + 2.5f, cam.transform.position.z);
                yield return null;
                var c = stateQuery.GetSingleton<LightBudgetState>().Counts;
                peakS = Mathf.Max(peakS, c.CastingShadow);
                peakR = Mathf.Max(peakR, c.Lit);
                peakG = Mathf.Max(peakG, c.Glowing);
                frames++;
            }
            lines.Add($"walk 40 m through the lights over {frames} frames: peak lights casting shadow {peakS} (cap {caps.Shadowed}), lit {peakR} (cap {caps.Real}), glowing {peakG} (cap {caps.Glow}); end " + TierLine(stateQuery, presenter));
            cam.transform.position = start;
            for (var w = SetLights(stateQuery, presenter, false, true); w.MoveNext();) yield return null;

            var rows = new double[3, BenchSizes.Length, BudgetRuns];
            for (int run = 0; run < BudgetRuns; run++)
                for (int cfg = 0; cfg < 3; cfg++)
                {
                    for (var w = SetLights(stateQuery, presenter, cfg == 0, cfg == 2); w.MoveNext();) yield return null;
                    for (int r = 0; r < BenchSizes.Length; r++) rows[cfg, r, run] = BenchAt(cam, BenchSizes[r].w, BenchSizes[r].h);
                }
            var names = new[] { "no lights", "40 lights, tiers off", "40 lights, tiers on" };
            for (int cfg = 0; cfg < 3; cfg++)
            {
                var sb = new System.Text.StringBuilder($"bench {names[cfg]}, median of {BudgetRuns} interleaved (ms):");
                for (int r = 0; r < BenchSizes.Length; r++)
                {
                    var v = new double[BudgetRuns];
                    for (int k = 0; k < BudgetRuns; k++) v[k] = rows[cfg, r, k];
                    System.Array.Sort(v);
                    sb.Append($" {BenchSizes[r].h}p {v[BudgetRuns / 2]:F2}");
                }
                lines.Add(sb.ToString());
            }
            GameStatus.Lines["~lightbudget"] = string.Join("\n", lines);
        }

        static System.Collections.IEnumerator SetLights(EntityQuery stateQuery, LightPresenterSystem presenter, bool hidden, bool budgeted)
        {
            var st = stateQuery.GetSingleton<LightBudgetState>();
            st.Hidden = hidden;
            st.Budgeted = budgeted;
            stateQuery.SetSingleton(st);
            for (int k = 0; k < 4; k++) yield return null;
            for (int k = 0; k < 900 && presenter.Fading; k++) yield return null;
            for (int k = 0; k < 4; k++) yield return null;
        }

        static string TierLine(EntityQuery stateQuery, LightPresenterSystem presenter)
        {
            var c = stateQuery.GetSingleton<LightBudgetState>().Counts;
            return $"tiers shadowed {c.Shadowed}, unshadowed {c.Unshadowed}, glow {c.Glow}, emissive {c.Emissive}; real Unity lights {presenter.Real} ({presenter.Shadowed} shadowed), glow sprites {presenter.Glows}, emissive cores {presenter.Cores}";
        }
    }
}
