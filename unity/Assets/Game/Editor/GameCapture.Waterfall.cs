using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator WaterfallTour(Camera cam, string dir)
        {
            ClientMapSystem sys = null;
            Ground ground = default;
            foreach (var w in Unity.Entities.World.All)
            {
                var s = w.GetExistingSystemManaged<ClientMapSystem>();
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (s?.Map == null || q.CalculateEntityCount() != 1) continue;
                sys = s;
                ground = q.GetSingleton<Ground>();
                break;
            }
            if (sys == null) { GameStatus.Lines["~waterfall"] = "waterfall: no map"; yield break; }
            var best = default(FallCurtain);
            bool found = false;
            foreach (var l in NearField.Lines(sys.Map))
                for (int k = 1; k < l.X.Length; k++)
                    if (WaterGeometry.FallAt(l, k, out var f, out _) && f.Drop >= 3f && (!found || f.Discharge > best.Discharge)) { best = f; found = true; }
            if (!found) { GameStatus.Lines["~waterfall"] = "waterfall: no fall found"; yield break; }
            var fd = new Vector3(best.DirX, 0, best.DirZ);
            var side = new Vector3(fd.z, 0, -fd.x);
            var lip = new Vector3(best.LipX, best.LipY, best.LipZ);
            var foot = new Vector3(best.BaseX, best.BaseY, best.BaseZ);
            float h = best.Drop, wd = best.Width;
            int seams = FallSeams(sys.Map, best, out int flipped, out int unshared);
            GameStatus.Lines["~waterfall"] = $"fall at {lip.x:F0},{lip.y:F0},{lip.z:F0}, drop {h:F1} m, width {wd:F0} m, discharge {best.Discharge:F0}, pool {best.PoolRadius:F1} m, open seams {seams}, flipped foot faces {flipped}, unshared foot verts {unshared}";
            cam.fieldOfView = 60f;
            var a = lip + fd * (h + 0.4f * wd + 6f) + side * (0.45f * wd);
            var b = lip + fd * (h + 0.4f * wd + 6f) - side * (0.45f * wd);
            var eye = ground.HeightAt(a.x, a.z) <= ground.HeightAt(b.x, b.z) ? a : b;
            eye.y = Mathf.Max(lip.y + 1f + 0.3f * h, ground.HeightAt(eye.x, eye.z) + 1.8f);
            var look = lip - Vector3.up * (0.2f * h);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            for (var e = Settle(180); e.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "waterfall-lip");
            var fside = eye.x == a.x && eye.z == a.z ? side : -side;
            eye = foot + fd * (5f + 0.5f * h) + fside * (0.25f * wd + 3f);
            eye.y = Mathf.Max(foot.y + 1.5f + 0.1f * h, ground.HeightAt(eye.x, eye.z) + 1.8f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(foot + Vector3.up * 0.5f - eye));
            for (var e = Settle(120); e.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "waterfall-foot");
        }

        static int FallSeams(GeneratedWorld map, FallCurtain f, out int flipped, out int unshared)
        {
            flipped = 0;
            unshared = 0;
            var fd = new Vector3(f.DirX, 0, f.DirZ);
            var side = new Vector3(fd.z, 0, -fd.x);
            var lip = new Vector3(f.LipX, f.LipY, f.LipZ);
            float run = Vector3.Dot(new Vector3(f.BaseX, f.LipY, f.BaseZ) - lip, fd) + 2f, reach = 0.5f * f.Width + 2f;
            int open = 0;
            foreach (var m in WaterGeometry.Rivers(map, new TerrainDetail(map), null))
            {
                var ids = new int[m.Verts.Length];
                var weld = new System.Collections.Generic.Dictionary<Vector3Int, int>();
                for (int v = 0; v < ids.Length; v++)
                {
                    var p = m.Verts[v];
                    var key = new Vector3Int(Mathf.RoundToInt(p.x * 1000), Mathf.RoundToInt(p.y * 1000), Mathf.RoundToInt(p.z * 1000));
                    if (!weld.TryGetValue(key, out ids[v])) weld[key] = ids[v] = v;
                    float tu = Vector3.Dot(p - lip, fd), su = Mathf.Abs(Vector3.Dot(p - lip, side));
                    if (ids[v] != v && Mathf.Abs(p.y - f.BaseY) < 0.05f && tu > -2f && tu < run && su < reach) unshared++;
                }
                var faces = new System.Collections.Generic.HashSet<(int, int, int)>();
                var edges = new System.Collections.Generic.Dictionary<(int, int), int>();
                for (int t = 0; t + 2 < m.Tris.Length; t += 3)
                {
                    int a = ids[m.Tris[t]], b = ids[m.Tris[t + 1]], c = ids[m.Tris[t + 2]];
                    int lo = Mathf.Min(a, Mathf.Min(b, c)), hi = Mathf.Max(a, Mathf.Max(b, c)), mid = a + b + c - lo - hi;
                    if (lo == mid || mid == hi || !faces.Add((lo, mid, hi))) continue;
                    var pa = m.Verts[a];
                    var pb = m.Verts[b];
                    var pc = m.Verts[c];
                    float t0 = Vector3.Dot(pa - lip, fd), s0 = Mathf.Abs(Vector3.Dot(pa - lip, side));
                    if (Mathf.Abs(pa.y - f.BaseY) < 0.05f && Mathf.Abs(pb.y - f.BaseY) < 0.05f && Mathf.Abs(pc.y - f.BaseY) < 0.05f && t0 > -2f && t0 < run + 4f && s0 < reach && Vector3.Cross(pb - pa, pc - pa).y < 0) flipped++;
                    foreach (var e in new[] { (lo, mid), (lo, hi), (mid, hi) }) edges[e] = edges.TryGetValue(e, out int k) ? k + 1 : 1;
                }
                foreach (var e in edges)
                {
                    if (e.Value != 1) continue;
                    var p = m.Verts[e.Key.Item1];
                    var q = m.Verts[e.Key.Item2];
                    if (Mathf.Abs(m.Uvs[e.Key.Item1].x) > 0.999f && Mathf.Abs(m.Uvs[e.Key.Item2].x) > 0.999f) continue;
                    if (Mathf.Max(p.y, q.y) < f.BaseY - 0.25f) continue;
                    bool inside = true;
                    foreach (var r in new[] { p, q })
                    {
                        float t = Vector3.Dot(r - lip, fd), s = Mathf.Abs(Vector3.Dot(r - lip, side));
                        inside &= t > -2f && t < run && s < reach && r.y < f.LipY + 1f;
                    }
                    if (inside) open++;
                }
            }
            return open;
        }
    }
}
