using System.Collections.Generic;
using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float LookHour = 15.5f;

        static System.Collections.IEnumerator WaterLookTour(Camera cam, string dir)
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
            if (sys == null) { GameStatus.Lines["~waterlook"] = "waterlook: no map"; yield break; }
            GameStatus.HoldCamera = true;
            var map = sys.Map;
            int n = map.Config.Size;
            float cm = map.Config.CellMeters;
            cam.fieldOfView = 60f;
            Vector2 Cell(int i) => new Vector2(i % n, i / n) * cm;
            float HomeD(int i) => (Cell(i) - new Vector2(home.x, home.z)).magnitude;

            Lake lake = null;
            foreach (var l in map.Water.Lakes)
                if (l.Cells.Count >= Stabby.Viewer.WaterGeometry.LakeSwellCells && (lake == null || HomeD(l.Outlet) < HomeD(lake.Outlet))) lake = l;
            if (lake == null)
                foreach (var l in map.Water.Lakes)
                    if (lake == null || l.Cells.Count > lake.Cells.Count) lake = l;

            Vector3 eye, target;
            for (var w = ShowHour(LookHour); w.MoveNext();) yield return null;
            if (lake != null)
            {
                var mid = Vector2.zero;
                foreach (int i in lake.Cells) mid += Cell(i);
                mid /= lake.Cells.Count;
                var sun = SunXZ();
                int edge = FarCell(lake.Cells, mid, -sun, Cell);
                var shore = Cell(edge);
                var to = (mid - shore).sqrMagnitude > 1f ? (mid - shore).normalized : Vector2.right;
                eye = new Vector3(shore.x - to.x * 6f, 0, shore.y - to.y * 6f);
                eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z), lake.Level) + 3f;
                target = new Vector3(mid.x + to.x * 60f, lake.Level + 6f, mid.y + to.y * 60f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~waterlook lake"] = $"lake {lake.Id} ({lake.Cells.Count} cells, level {lake.Level:F1}) eye {eye.x:F0},{eye.y:F1},{eye.z:F0}, hour {LookHour}";
                for (var e = LookPair(cam, dir, "waterlook-lake"); e.MoveNext();) yield return null;

                for (var w = ShowHour(DuskHour); w.MoveNext();) yield return null;
                sun = SunXZ();
                edge = FarCell(lake.Cells, mid, sun, Cell);
                shore = Cell(edge);
                to = (mid - shore).sqrMagnitude > 1f ? (mid - shore).normalized : Vector2.right;
                eye = new Vector3(shore.x - to.x * 4f, 0, shore.y - to.y * 4f);
                eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z), lake.Level) + 2.5f;
                target = new Vector3(shore.x + to.x * 200f, lake.Level + 4f, shore.y + to.y * 200f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~waterlook dusk"] = $"dusk lake {lake.Id} eye {eye.x:F0},{eye.y:F1},{eye.z:F0} facing sun {sun.x:F2},{sun.y:F2}, hour {DuskHour}";
                for (var e = LookPair(cam, dir, "waterlook-dusk"); e.MoveNext();) yield return null;

                for (var w = ShowHour(NightHour); w.MoveNext();) yield return null;
                for (var e = LookPair(cam, dir, "waterlook-night"); e.MoveNext();) yield return null;
                for (var w = ShowHour(LookHour); w.MoveNext();) yield return null;
            }
            else GameStatus.Lines["~waterlook lake"] = "no lake";

            int coast = -1;
            float best = float.MaxValue;
            for (int y = 2; y < n - 2; y++)
                for (int x = 2; x < n - 2; x++)
                {
                    int i = y * n + x;
                    if (!map.Sea[i] || map.Sea[i - 1] && map.Sea[i + 1] && map.Sea[i - n] && map.Sea[i + n]) continue;
                    var land = Vector2.zero;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            if (!map.Sea[(y + dy) * n + x + dx]) land += new Vector2(dx, dy);
                    if (land.sqrMagnitude < 0.5f) continue;
                    var off = -land.normalized;
                    int ox = x + Mathf.RoundToInt(off.x * 60f / cm), oy = y + Mathf.RoundToInt(off.y * 60f / cm);
                    if (ox < 0 || oy < 0 || ox >= n || oy >= n || !map.Sea[oy * n + ox]) continue;
                    float shelf = -map.Height[oy * n + ox];
                    if (shelf < 1.5f || shelf > 7f) continue;
                    float d = HomeD(i);
                    if (d < best) { best = d; coast = i; }
                }
            Vector3 benchEye = cam.transform.position;
            Quaternion benchRot = cam.transform.rotation;
            if (coast >= 0)
            {
                int x = coast % n, y = coast / n;
                var land = Vector2.zero;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        if (!map.Sea[(y + dy) * n + x + dx]) land += new Vector2(dx, dy);
                var inland = land.normalized;
                var c = Cell(coast);
                var side = new Vector2(-inland.y, inland.x);
                eye = new Vector3(c.x + inland.x * 25f + side.x * 20f, 0, c.y + inland.y * 25f + side.y * 20f);
                eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z), 0) + 6f;
                target = new Vector3(c.x - inland.x * 40f - side.x * 10f, 0, c.y - inland.y * 40f - side.y * 10f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~waterlook shore"] = $"coast {c.x:F0},{c.y:F0}, inland {inland.x:F2},{inland.y:F2}, eye {eye.x:F0},{eye.y:F1},{eye.z:F0}";
                for (var e = LookPair(cam, dir, "waterlook-shore"); e.MoveNext();) yield return null;
                var high = new Vector3(c.x + inland.x * 70f, 0, c.y + inland.y * 70f);
                high.y = Mathf.Max(ground.HeightAt(high.x, high.z), 0) + 35f;
                cam.transform.SetPositionAndRotation(high, Quaternion.LookRotation(new Vector3(c.x - inland.x * 150f, 0, c.y - inland.y * 150f) - high));
                for (var e = LookPair(cam, dir, "waterlook-sea"); e.MoveNext();) yield return null;
                benchEye = cam.transform.position;
                benchRot = cam.transform.rotation;
            }
            else GameStatus.Lines["~waterlook shore"] = "no shallow coast";

            RiverSegment river = null;
            int at = -1;
            float score = float.MinValue;
            foreach (var r in map.Water.Rivers)
                for (int k = 6; k < r.Cells.Count - 6; k += 3)
                {
                    if (LakeAt(map, r.Cells[k]) || map.Sea[r.Cells[k]]) continue;
                    float drop = map.Water.WaterSurface[r.Cells[k - 6]] - map.Water.WaterSurface[r.Cells[k + 6]];
                    if (drop <= 0.5f) continue;
                    float sc = Mathf.Log(1 + r.Discharge[k]) - HomeD(r.Cells[k]) * 0.0005f;
                    if (sc > score) { score = sc; river = r; at = k; }
                }
            if (river != null)
            {
                int i0 = river.Cells[at - 4], i1 = river.Cells[at + 4], c = river.Cells[at];
                var along = (Cell(i1) - Cell(i0)).normalized;
                var mid = new Vector3(Cell(c).x, map.Water.WaterSurface[c], Cell(c).y);
                eye = mid - new Vector3(along.x, 0, along.y) * 30f + new Vector3(-along.y, 0, along.x) * 8f;
                eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z), mid.y) + 9f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(mid + new Vector3(along.x, 0, along.y) * 15f - eye));
                float drop = map.Water.WaterSurface[river.Cells[at - 6]] - map.Water.WaterSurface[river.Cells[at + 6]];
                GameStatus.Lines["~waterlook river"] = $"river {river.Id} at {mid.x:F0},{mid.z:F0}, Q {river.Discharge[at]:F1}, slope {drop / (12 * cm):F4}";
                for (var e = LookPair(cam, dir, "waterlook-river"); e.MoveNext();) yield return null;
                yield return null;
                Shot(cam, dir, "waterlook-river-later");
            }
            else GameStatus.Lines["~waterlook river"] = "no river";

            cam.transform.SetPositionAndRotation(benchEye, benchRot);
            for (var s = Settle(240); s.MoveNext();) yield return null;
            var sb = new System.Text.StringBuilder("bench waterlook (ms, median of 3, off/on interleaved):");
            foreach (var (bw, bh) in new[] { (1920, 1080), (3840, 2160) })
            {
                var off = new double[3];
                var on = new double[3];
                for (int k = 0; k < 3; k++)
                {
                    Shader.SetGlobalFloat("_StabbyWaterLookOff", 1);
                    off[k] = BenchAt(cam, bw, bh);
                    Shader.SetGlobalFloat("_StabbyWaterLookOff", 0);
                    on[k] = BenchAt(cam, bw, bh);
                }
                System.Array.Sort(off);
                System.Array.Sort(on);
                sb.Append($" {bh}p before {off[1]:F2} ({off[0]:F2}-{off[2]:F2}) after {on[1]:F2} ({on[0]:F2}-{on[2]:F2})");
            }
            GameStatus.Lines["~waterlook bench"] = sb.ToString();
        }

        static Vector2 SunXZ()
        {
            Light sun = null;
            foreach (var lt in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (lt.type == LightType.Directional && lt.isActiveAndEnabled && (sun == null || lt.intensity > sun.intensity)) sun = lt;
            if (sun == null) return Vector2.right;
            var f = -sun.transform.forward;
            var v = new Vector2(f.x, f.z);
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector2.right;
        }

        static int FarCell(List<int> cells, Vector2 mid, Vector2 dir, System.Func<int, Vector2> cell)
        {
            int best = cells[0];
            float far = float.MinValue;
            foreach (int i in cells)
            {
                float d = Vector2.Dot(cell(i) - mid, -dir);
                if (d > far) { far = d; best = i; }
            }
            return best;
        }

        static System.Collections.IEnumerator LookPair(Camera cam, string dir, string name)
        {
            for (var s = Settle(240); s.MoveNext();) yield return null;
            Shader.SetGlobalFloat("_StabbyWaterLookOff", 1);
            Shot(cam, null, "warmup");
            Shot(cam, dir, name + "-before");
            Shader.SetGlobalFloat("_StabbyWaterLookOff", 0);
            Shot(cam, null, "warmup");
            Shot(cam, dir, name);
        }
    }
}
