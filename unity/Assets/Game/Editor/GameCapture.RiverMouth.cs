using System.Collections.Generic;
using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator RiverMouthTour(Camera cam, string dir)
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
            if (sys == null) { GameStatus.Lines["~rivermouth"] = "rivermouth: no map"; yield break; }
            var map = sys.Map;
            float cm = map.Config.CellMeters;
            var mouths = new List<RiverSegment>();
            foreach (var r in map.Water.Rivers)
                if (r.End == RiverEnd.Sea && r.Cells.Count >= 8) mouths.Add(r);
            mouths.Sort((a, b) => b.Discharge[b.Cells.Count - 2].CompareTo(a.Discharge[a.Cells.Count - 2]));
            cam.fieldOfView = 60f;
            for (int i = 0; i < Mathf.Min(4, mouths.Count); i++)
            {
                var r = mouths[i];
                int m = r.Cells.Count;
                var end = new Vector3(r.X[m - 1] * cm, 0, r.Y[m - 1] * cm);
                var up = new Vector3(r.X[m - 6] * cm, 0, r.Y[m - 6] * cm);
                var along = (end - up).normalized;
                var side = new Vector3(along.z, 0, -along.x);
                GameStatus.Lines[$"~rivermouth {i}"] = $"river {r.Id} meets the sea at {end.x:F0},{end.z:F0}, Q {r.Discharge[m - 2]:F0}";
                var eye = end - along * 160f + side * 50f;
                eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z), 0) + 60f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(end - eye));
                for (var e = Settle(240); e.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, $"mouth-{i}-wide");
                eye = end - along * 60f + side * 15f;
                eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z), 0) + 16f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(end + along * 20f - eye));
                for (var e = Settle(120); e.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, $"mouth-{i}-close");
            }
            int n = map.Config.Size;
            RiverSegment inflow = null;
            int at = -1;
            float step = 0;
            foreach (var r in map.Water.Rivers)
                for (int k = 1; k < r.Cells.Count; k++)
                {
                    if (LakeAt(map, r.Cells[k - 1]) || !LakeAt(map, r.Cells[k])) continue;
                    var lm = map.Water.Lakes[map.Water.LakeId[r.Cells[k]]].Mix;
                    var p = r.Mix[k - 1];
                    float gap = (Mathf.Abs(p.A - lm.A) + Mathf.Abs(p.B - lm.B) + Mathf.Abs(p.C - lm.C)) * Mathf.Log(1 + r.Discharge[k - 1]);
                    if (gap > step) { step = gap; inflow = r; at = k; }
                    break;
                }
            if (inflow == null) yield break;
            int c = inflow.Cells[at], u = inflow.Cells[Mathf.Max(0, at - 4)];
            var dir2 = new Vector2(c % n - u % n, c / n - u / n).normalized;
            var mid = new Vector3(c % n * cm, map.Water.WaterSurface[c], c / n * cm);
            var lake = map.Water.Lakes[map.Water.LakeId[c]];
            GameStatus.Lines["~rivermouth lagoon"] = $"river {inflow.Id} enters lake {lake.Id} at {mid.x:F0},{mid.z:F0}, outlet {lake.Outlet % n * cm:F0},{lake.Outlet / n * cm:F0}";
            var eye2 = mid + new Vector3(dir2.x, 0, dir2.y) * 50f + Vector3.up * 22f;
            eye2.y = Mathf.Max(eye2.y, ground.HeightAt(eye2.x, eye2.z) + 12f);
            cam.transform.SetPositionAndRotation(eye2, Quaternion.LookRotation(mid - eye2));
            for (var e = Settle(240); e.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "lagoon-inflow-close");
            Lake lagoon = null;
            Vector3 sea = default;
            int hops = int.MaxValue;
            foreach (var lk in map.Water.Lakes)
            {
                if (lk.Outlet < 0) continue;
                int ox = lk.Outlet % n, oz = lk.Outlet / n;
                for (int r = 0; r < map.Water.Rivers.Count; r++)
                {
                    var c0 = map.Water.Rivers[r].Cells[0];
                    if (Mathf.Abs(c0 % n - ox) > 1 || Mathf.Abs(c0 / n - oz) > 1 || LakeAt(map, c0)) continue;
                    int len = 0, q = r;
                    while (map.Water.Rivers[q].End == RiverEnd.Junction && len < 4096) { len += map.Water.Rivers[q].Cells.Count; q = map.Water.Rivers[q].Downstream; }
                    var last = map.Water.Rivers[q];
                    len += last.Cells.Count;
                    if (last.End != RiverEnd.Sea || len >= hops) continue;
                    hops = len; lagoon = lk;
                    int m = last.Cells.Count - 1;
                    sea = new Vector3(last.X[m] * cm, 0, last.Y[m] * cm);
                }
            }
            if (lagoon == null) yield break;
            var outlet = new Vector3(lagoon.Outlet % n * cm, lagoon.Level, lagoon.Outlet / n * cm);
            var flow = (sea - outlet); flow.y = 0;
            var span = Mathf.Max(flow.magnitude, 120f);
            flow = flow.normalized;
            var side2 = new Vector3(flow.z, 0, -flow.x);
            var centre = 0.5f * (outlet + sea);
            eye2 = centre - flow * 0.5f * span + side2 * span + Vector3.up * 0.6f * span;
            eye2.y = Mathf.Max(eye2.y, ground.HeightAt(eye2.x, eye2.z) + 30f);
            centre.y = Mathf.Max(ground.HeightAt(centre.x, centre.z), 0);
            Debug.Log($"rivermouth lagoon outlet {outlet.x:F0},{outlet.z:F0} sea {sea.x:F0},{sea.z:F0} eye {eye2}");
            cam.transform.SetPositionAndRotation(eye2, Quaternion.LookRotation(centre - eye2));
            for (var e = Settle(480); e.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "lagoon-outlet");
        }
    }
}
