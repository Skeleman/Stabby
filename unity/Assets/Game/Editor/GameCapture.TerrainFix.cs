using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const int ProbeW = 192, ProbeH = 108;

        static System.Collections.IEnumerator TerrainFixTour(Camera cam, string dir)
        {
            var server = ServerWorld();
            var view = ViewWorld();
            Ground ground = default;
            bool hasGround = false;
            foreach (var gw in Unity.Entities.World.All)
            {
                using var gq = gw.EntityManager.CreateEntityQuery(typeof(Ground));
                if (gq.CalculateEntityCount() == 1) { ground = gq.GetSingleton<Ground>(); hasGround = true; break; }
            }
            var terrain = view?.GetExistingSystemManaged<TerrainViewSystem>();
            if (server == null || view == null || !hasGround || terrain == null) { GameStatus.Lines["~terrainfix"] = "terrainfix: no worlds, ground or terrain"; yield break; }
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 60f;
            var lines = new List<string> { SystemOrder(view) };
            var rt = new RenderTexture(ProbeW, ProbeH, 24);
            var tex = new Texture2D(ProbeW, ProbeH, TextureFormat.RGB24, false);
            try
            {
                var sun = RenderSettings.sun != null ? RenderSettings.sun : Object.FindObjectsByType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
                Vector3 f = sun != null ? sun.transform.forward : new Vector3(0, -1, 0);
                var upsun = -new Vector2(f.x, f.z).normalized;
                float tanE = -f.y / Mathf.Max(0.01f, new Vector2(f.x, f.z).magnitude);
                var spot = ShadowSpot(ground, new Vector2(home.x, home.z), upsun, tanE, out float occ, out float back);
                lines.Add($"shadow spot {spot.x:F0},{spot.y:F0}: occluder {occ:F1} m above the sun ray, {back:F0} m upsun; sun elevation {Mathf.Atan(tanE) * Mathf.Rad2Deg:F0} deg");
                var target = new Vector3(spot.x, GroundHeight(spot.x, spot.y) + 1f, spot.y);
                PlaceServerPlayers(server, spot - upsun * 3f);
                foreach (float dist in new[] { 25f, 55f })
                {
                    ShadowPose(cam, ground, spot, upsun, dist, target);
                    for (var w = Settle(240); w.MoveNext();) yield return null;
                    Shot(cam, dir, $"terrainfix-shadow-{dist:F0}m");
                }
                int jumps = 0;
                float worst = 0, prev = -1;
                const int ApproachFrames = 160;
                ShadowPose(cam, ground, spot, upsun, 100f, target);
                for (var w = Settle(240); w.MoveNext();) yield return null;
                for (int k = 0; k <= ApproachFrames; k++)
                {
                    ShadowPose(cam, ground, spot, upsun, Mathf.Lerp(100f, 20f, k / (float)ApproachFrames), target);
                    yield return null;
                    var lum = Probe(cam, rt, tex);
                    float patch = Patch(lum, cam.WorldToViewportPoint(target - Vector3.up * 1f));
                    if (prev >= 0) { float j = Mathf.Abs(patch - prev); worst = Mathf.Max(worst, j); if (j > 0.04f) jumps++; }
                    prev = patch;
                }
                lines.Add($"shadow approach 100->20 m over {ApproachFrames} frames: {jumps} frames with target patch jump > 0.04, worst {worst:F3}");

                cam.fieldOfView = 60f;
                var foot = new Vector3(spot.x, GroundHeight(spot.x, spot.y), spot.y);
                cam.transform.SetPositionAndRotation(foot + Vector3.up * 12f, Quaternion.LookRotation(new Vector3(upsun.x, -0.3f, upsun.y)));
                for (var w = Settle(240); w.MoveNext();) yield return null;
                float3 eye = cam.transform.position;
                if (terrain.Seam(eye, 15f, 160f, out float3 at, out float3 across))
                {
                    at.y = GroundHeight(at.x, at.z);
                    Vector3 seamEye = cam.transform.position;
                    cam.fieldOfView = 30f;
                    cam.transform.rotation = Quaternion.LookRotation((Vector3)at - seamEye);
                    for (var w = Settle(120); w.MoveNext();) yield return null;
                    Shot(cam, dir, "terrainfix-seam");
                    lines.Add($"seam close-up at {at.x:F0},{at.y:F0},{at.z:F0} across {across.x:F0},{across.z:F0}, {Vector3.Distance(at, seamEye):F0} m from the eye, FOV 30");
                }
                else lines.Add("seam close-up: no level 0-2 node with a coarser neighbour in 15-160 m");
                cam.fieldOfView = 60f;

                Vector2 d = WalkLine(ground, new Vector2(home.x, home.z), NormalWalkM + FastWalkM, out float land, out float relief);
                var start = new Vector2(home.x, home.z);
                lines.Add($"blink walk from {start.x:F0},{start.y:F0} along {d.x:F2},{d.y:F2}: {land * 100:F0}% land, relief {relief:F0} m");
                foreach (var (label, from, length, speed) in new[] { ("normal", 0f, NormalWalkM, GameSettings.WalkSpeed), ("fast", NormalWalkM, FastWalkM, FastWalkMps) })
                {
                    var tally = new int[8];
                    int frames = 0, drops = 0, lastShown = -1, spikes = 0, minDeficit = int.MaxValue, maxDeficit = 0;
                    float[] a = null, b = null;
                    int sa = -1, sb = -1, after = 0, lastSplit = -1;
                    var events = new List<string>();
                    float along = from;
                    while (along < from + length)
                    {
                        along = Mathf.Min(from + length, along + speed * Mathf.Min(Time.deltaTime, 0.1f));
                        var p = start + d * along;
                        PlaceServerPlayers(server, p);
                        var ft = new Vector3(p.x, GroundHeight(p.x, p.y), p.y);
                        var fwd = new Vector3(d.x, 0, d.y);
                        var ey = ft - fwd * 6f + Vector3.up * 3f;
                        ey.y = Mathf.Max(ey.y, GroundHeight(ey.x, ey.z) + 1.5f);
                        cam.transform.SetPositionAndRotation(ey, Quaternion.LookRotation(ft + fwd * 20f + Vector3.up * 1.2f - ey));
                        yield return null;
                        var r = terrain.Audit();
                        frames++;
                        for (int i = 1; i < 6; i++) if (r[i] > 0) tally[i]++;
                        if (after > 0 && events.Count > 0) { events[events.Count - 1] += $" | {r[0]} shown, split {r[7]}, drawn {r[9]}"; after--; }
                        if (lastShown > 0 && r[0] < lastShown * 3 / 4) { drops++; if (events.Count < 12) { events.Add($"drop f{frames} shown {lastShown}->{r[0]}, split {lastSplit}->{r[7]}, leaves {r[8]}, drawn {r[9]}, scheduled {r[10]}, roots ready {r[11]}, eye {cam.transform.position.x:F0},{cam.transform.position.y:F0},{cam.transform.position.z:F0}, dt {Time.deltaTime * 1000:F0} ms"); after = 4; } }
                        lastSplit = r[7];
                        lastShown = r[0];
                        minDeficit = Mathf.Min(minDeficit, r[6]);
                        maxDeficit = Mathf.Max(maxDeficit, r[6]);
                        if (r[6] > 0) tally[6]++;
                        var c = Probe(cam, rt, tex);
                        if (a != null && b != null && Spike(a, b, c, out int ab, out int bc))
                        {
                            spikes++;
                            if (events.Count < 12) events.Add($"spike f{frames - 1} px {ab}/{bc} of {c.Length} mean {Mean(a):F2}/{Mean(b):F2}/{Mean(c):F2} shown {sa}/{sb}/{r[0]} terrain {GameStatus.TerrainMs:F1} ms");
                        }
                        sa = sb;
                        sb = r[0];
                        a = b;
                        b = c;
                    }
                    lines.Add($"blink {label} walk {frames} frames: shown-count drops {drops}, image one-frame spikes {spikes}; frames with chunks missing {tally[1]}, wrong mesh {tally[2]}, stale RenderBounds {tally[3]}, stale WorldRenderBounds {tally[4]}, ghost entities {tally[5]}, coverage gap {tally[6]} (gap m2 {(minDeficit == int.MaxValue ? 0 : minDeficit)}..{maxDeficit})");
                    foreach (var ev in events) lines.Add($"  {label} {ev}");
                    for (var w = Settle(60); w.MoveNext();) yield return null;
                }
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
            GameStatus.Line("terrain", out var t);
            lines.Add(t);
            GameStatus.Lines["~terrainfix"] = string.Join("\n", lines);
        }

        static string SystemOrder(Unity.Entities.World view)
        {
            var group = view.GetExistingSystemManaged<PresentationSystemGroup>();
            if (group == null) return "order: no presentation group";
            using var all = group.GetAllSystems(Allocator.Temp);
            int Find(SystemHandle h) { for (int i = 0; i < all.Length; i++) if (all[i] == h) return i; return -1; }
            int terrain = Find(view.GetExistingSystem<TerrainViewSystem>());
            int bounds = Find(view.GetExistingSystem<Unity.Rendering.UpdatePresentationSystemGroup>());
            int graphics = Find(view.GetExistingSystem<Unity.Rendering.EntitiesGraphicsSystem>());
            return $"presentation order: terrain {terrain}, UpdatePresentationSystemGroup {bounds}, EntitiesGraphicsSystem {graphics} of {all.Length}";
        }

        static Vector2 ShadowSpot(Ground ground, Vector2 home, Vector2 upsun, float tanE, out float occ, out float back)
        {
            var best = home;
            occ = back = 0;
            float top = ground.Top, bestScore = float.MinValue;
            for (int r = 0; r <= 800; r += 40)
                for (int k = 0; k < (r == 0 ? 1 : 32); k++)
                {
                    float ang = k * Mathf.PI / 16;
                    var x = home + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                    if (x.x < 100 || x.y < 100 || x.x > top - 100 || x.y > top - 100) continue;
                    float h = ground.Surface(x.x, x.y);
                    var cam = x - upsun * 55f;
                    if (h <= 2f || ground.Surface(cam.x, cam.y) <= 1f) continue;
                    var tgt = new Vector3(x.x, h + 1f, x.y);
                    if (!Clear(ground, PoseEye(ground, x, upsun, 25f, tgt), tgt) || !Clear(ground, PoseEye(ground, x, upsun, 55f, tgt), tgt)) continue;
                    for (float s = 20; s <= 60; s += 5)
                    {
                        var u = x + upsun * s;
                        float o = ground.Surface(u.x, u.y) - h - s * tanE;
                        float score = Mathf.Min(o, 25f) - r * 0.005f;
                        if (o > 3f && score > bestScore) { bestScore = score; best = x; occ = o; back = s; }
                    }
                }
            return best;
        }

        static Vector3 PoseEye(Ground ground, Vector2 spot, Vector2 upsun, float dist, Vector3 target)
        {
            var e = spot - upsun * dist;
            return new Vector3(e.x, Mathf.Max(ground.HeightAt(e.x, e.y) + 2f, target.y + 4f + dist * 0.3f), e.y);
        }

        static bool Clear(Ground ground, Vector3 a, Vector3 b)
        {
            for (int i = 1; i < 32; i++)
            {
                var p = Vector3.Lerp(a, b, i / 32f);
                if (ground.HeightAt(p.x, p.z) > p.y - 0.5f) return false;
            }
            return true;
        }

        static void ShadowPose(Camera cam, Ground ground, Vector2 spot, Vector2 upsun, float dist, Vector3 target)
        {
            var eye = PoseEye(ground, spot, upsun, dist, target);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        }

        static float[] Probe(Camera cam, RenderTexture rt, Texture2D tex)
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, ProbeW, ProbeH), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            var px = tex.GetPixels32();
            var lum = new float[px.Length];
            for (int i = 0; i < px.Length; i++) lum[i] = (0.3f * px[i].r + 0.59f * px[i].g + 0.11f * px[i].b) / 255f;
            return lum;
        }

        static float Patch(float[] lum, Vector3 vp)
        {
            int cx = Mathf.Clamp((int)(vp.x * ProbeW), 4, ProbeW - 5), cy = Mathf.Clamp((int)(vp.y * ProbeH), 4, ProbeH - 5);
            float sum = 0;
            for (int y = cy - 4; y <= cy + 4; y++)
                for (int x = cx - 4; x <= cx + 4; x++) sum += lum[y * ProbeW + x];
            return sum / 81f;
        }

        static float Mean(float[] v) { float s = 0; foreach (var x in v) s += x; return s / v.Length; }

        static bool Spike(float[] a, float[] b, float[] c, out int ab, out int bc)
        {
            ab = bc = 0;
            int ac = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (Mathf.Abs(b[i] - a[i]) > 0.1f) ab++;
                if (Mathf.Abs(c[i] - b[i]) > 0.1f) bc++;
                if (Mathf.Abs(c[i] - a[i]) > 0.1f) ac++;
            }
            int n = a.Length / 100;
            return ab > n && bc > n && ac * 3 < System.Math.Min(ab, bc);
        }
    }
}
