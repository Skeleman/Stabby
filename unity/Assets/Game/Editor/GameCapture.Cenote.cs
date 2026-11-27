using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float CenoteHour = 12.5f, CenoteMoon = 0.5f;

        static System.Collections.IEnumerator CenoteTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            Ground ground = default;
            bool found = false;
            foreach (var w in Unity.Entities.World.All)
            {
                if (w.Name != GameSettings.ViewClient) continue;
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (q.CalculateEntityCount() != 1) continue;
                ground = q.GetSingleton<Ground>();
                found = true;
                break;
            }
            if (!found) { GameStatus.Lines["~cenote"] = GameStatus.Failure = "cenote: no ground"; yield break; }
            int at = WidestCenote(ground, out var c);
            if (at < 0) { GameStatus.Lines["~cenote"] = GameStatus.Failure = "cenote: none placed"; yield break; }
            float len = c.CenoteAxis(out float ux, out float uz), back = c.D + 3f;
            var eye = new Vector3(c.Ax - ux * back + uz * 0.35f * back, 0f, c.Az - uz * back - ux * 0.35f * back);
            eye.y = ground.HeightAt(eye.x, eye.z) + 5f;
            var look = new Vector3(c.Ax + ux * 0.6f * c.D, c.P, c.Az + uz * 0.6f * c.D);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(CenoteHour, CenoteMoon); w.MoveNext();) yield return null;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            for (var w = Settle(240); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~cenote"] = $"cenote: edit {at} radius {c.D:F1} rim {c.F:F1} water {c.P:F1} floor {c.By:F1} gully {len:F0} at {c.Ax:F0},{c.Az:F0} eye {eye.x:F0},{eye.y:F0},{eye.z:F0}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2} moon {sky.Phase:F2}";
            Shot(cam, dir, "cenote-rim");
            for (var w = LakeShoreShot(cam, dir, ground); w.MoveNext();) yield return null;
            for (var w = SeaShoreShot(cam, dir, ground, 12f, 80f, "sea-cliff"); w.MoveNext();) yield return null;
            for (var w = SeaShoreShot(cam, dir, ground, 0.2f, 1.5f, "beach"); w.MoveNext();) yield return null;
            for (var w = RiverMouthShot(cam, dir, ground); w.MoveNext();) yield return null;
            for (var w = RiverBankShot(cam, dir, ground); w.MoveNext();) yield return null;
        }

        static GeneratedWorld ShownMap()
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var s = w.GetExistingSystemManaged<ClientMapSystem>();
                if (s?.Map != null) return s.Map;
            }
            return null;
        }

        static System.Collections.IEnumerator SeaShoreShot(Camera cam, string dir, Ground ground, float lo, float hi, string name)
        {
            var map = ShownMap();
            if (map == null) { GameStatus.Lines["~" + name] = name + ": no map"; yield break; }
            int n = map.Config.Size;
            float cm = map.Config.CellMeters, best = float.MaxValue;
            int wet = -1, dry = -1;
            for (int y = 2; y < n - 2; y++)
                for (int x = 2; x < n - 2; x++)
                {
                    int c = y * n + x;
                    if (!map.Sea[c]) continue;
                    foreach (int o in new[] { c - 1, c + 1, c - n, c + n })
                    {
                        int far = 2 * o - c;
                        if (map.Sea[o] || map.Sea[far] || map.Height[o] < lo || map.Height[o] > hi || map.Height[far] < map.Height[o]) continue;
                        if (hi < 2f && map.Height[far] > 3f) continue;
                        float d = (new Vector2(x, y) * cm - new Vector2(home.x, home.z)).sqrMagnitude;
                        if (d < best) { best = d; wet = c; dry = o; }
                    }
                }
            if (wet < 0) { GameStatus.Lines["~" + name] = name + ": none found"; yield break; }
            var outward = new Vector3(wet % n - dry % n, 0, wet / n - dry / n);
            var edge = new Vector3(dry % n * cm, 0, dry / n * cm);
            var side = new Vector3(outward.z, 0, -outward.x);
            Vector3 eye, look;
            if (hi > 2f)
            {
                eye = edge + outward * 45f + side * 25f + Vector3.up * 8f;
                look = edge + outward * 4f;
            }
            else
            {
                eye = edge - outward * 12f;
                eye.y = ground.HeightAt(eye.x, eye.z) + 4f;
                look = edge + outward * 30f;
            }
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            for (var w = Settle(240); w.MoveNext();) yield return null;
            GameStatus.Lines["~" + name] = $"{name}: land {map.Height[dry]:F1} m at {edge.x:F0},{edge.z:F0} eye {eye.x:F0},{eye.y:F0},{eye.z:F0}";
            Shot(cam, dir, name);
        }

        static System.Collections.IEnumerator RiverMouthShot(Camera cam, string dir, Ground ground)
        {
            var map = ShownMap();
            if (map == null) { GameStatus.Lines["~mouth"] = "mouth: no map"; yield break; }
            RiverLine pick = null;
            int at = -1;
            float best = float.MaxValue;
            foreach (var l in NearField.Lines(map))
                for (int k = 1; k < l.X.Length - 1; k++)
                {
                    if (l.W[k] < 5f || l.D[k] < 1.5f || l.Liquid != 0) continue;
                    float d = (new Vector2(l.X[k], l.Z[k]) - new Vector2(home.x, home.z)).sqrMagnitude;
                    if (d < best) { best = d; pick = l; at = k; }
                }
            if (pick == null) { GameStatus.Lines["~mouth"] = "mouth: none found"; yield break; }
            var along = new Vector3(pick.X[at + 1] - pick.X[at - 1], 0, pick.Z[at + 1] - pick.Z[at - 1]).normalized;
            var across = new Vector3(along.z, 0, -along.x);
            var mid = new Vector3(pick.X[at], pick.S[at], pick.Z[at]);
            var eye = mid - across * (pick.W[at] + 8f) - along * 10f;
            eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z), pick.S[at]) + 4f;
            var look = mid + along * 12f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            for (var w = Settle(240); w.MoveNext();) yield return null;
            GameStatus.Lines["~mouth"] = $"mouth: half width {pick.W[at]:F1} depth {pick.D[at]:F1} at {mid.x:F0},{mid.z:F0} eye {eye.x:F0},{eye.y:F0},{eye.z:F0}";
            Shot(cam, dir, "river-mouth");
        }

        static System.Collections.IEnumerator LakeShoreShot(Camera cam, string dir, Ground ground)
        {
            var map = ShownMap();
            if (map == null || map.Water.LakeId == null) { GameStatus.Lines["~shore"] = "shore: no map"; yield break; }
            int n = map.Config.Size;
            float cm = map.Config.CellMeters, best = float.MaxValue;
            int wet = -1, dry = -1;
            foreach (var lake in map.Water.Lakes)
            {
                if (lake.Cells.Count < 400) continue;
                foreach (int c in lake.Cells)
                {
                    int x = c % n, y = c / n;
                    if (x < 1 || y < 1 || x >= n - 1 || y >= n - 1) continue;
                    foreach (int o in new[] { c - 1, c + 1, c - n, c + n })
                    {
                        float rise = map.Height[o] - lake.Level;
                        if (map.Water.LakeId[o] >= 0 || map.Sea[o] || rise < 0.3f || rise > 3f) continue;
                        float d = (new Vector2(x, y) * cm - new Vector2(home.x, home.z)).sqrMagnitude;
                        if (d < best) { best = d; wet = c; dry = o; }
                    }
                }
            }
            if (wet < 0) { GameStatus.Lines["~shore"] = "shore: none found"; yield break; }
            var inward = new Vector3(wet % n - dry % n, 0, wet / n - dry / n);
            var edge = new Vector3(dry % n * cm, 0, dry / n * cm);
            var eye = edge - inward * 12f;
            eye.y = ground.HeightAt(eye.x, eye.z) + 4f;
            var look = edge + inward * 30f;
            look.y = map.Water.Lakes[map.Water.LakeId[wet]].Level;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            for (var w = Settle(240); w.MoveNext();) yield return null;
            GameStatus.Lines["~shore"] = $"shore: lake {map.Water.LakeId[wet]} at {edge.x:F0},{edge.z:F0} eye {eye.x:F0},{eye.y:F0},{eye.z:F0}";
            Shot(cam, dir, "lake-shore");
        }

        static System.Collections.IEnumerator RiverBankShot(Camera cam, string dir, Ground ground)
        {
            var map = ShownMap();
            if (map == null) { GameStatus.Lines["~bank"] = "bank: no map"; yield break; }
            int n = map.Config.Size;
            float cm = map.Config.CellMeters, best = 0f;
            RiverLine pick = null;
            int at = -1;
            foreach (var l in NearField.Lines(map))
                for (int k = 4; k < l.X.Length - 4; k++)
                {
                    if (l.S[k - 4] - l.S[k + 4] > 1.5f) continue;
                    if (l.W[k] < 2f || l.W[k] > 10f || l.D[k] < 1f || l.Liquid != 0) continue;
                    int cx = Mathf.Clamp((int)(l.X[k] / cm), 0, n - 1), cy = Mathf.Clamp((int)(l.Z[k] / cm), 0, n - 1);
                    if (SeaCells(map, cx, cy) <= CoastFallSearchCells) continue;
                    float ax = l.X[k + 1] - l.X[k - 1], az = l.Z[k + 1] - l.Z[k - 1], al = Mathf.Sqrt(ax * ax + az * az);
                    if (al < 1e-3f) continue;
                    float sx = az / al, sz = -ax / al, r = l.W[k] + 4f;
                    float rise = Mathf.Min(ground.HeightAt(l.X[k] + sx * r, l.Z[k] + sz * r), ground.HeightAt(l.X[k] - sx * r, l.Z[k] - sz * r)) - l.S[k];
                    if (rise > best) { best = rise; pick = l; at = k; }
                }
            if (pick == null) { GameStatus.Lines["~bank"] = "bank: none found"; yield break; }
            var along = new Vector3(pick.X[at + 1] - pick.X[at - 1], 0, pick.Z[at + 1] - pick.Z[at - 1]).normalized;
            var across = new Vector3(along.z, 0, -along.x);
            var mid = new Vector3(pick.X[at], pick.S[at], pick.Z[at]);
            var eye = mid - along * 14f - across * (0.4f * pick.W[at]) + Vector3.up * 2.5f;
            var look = mid + along * 6f + across * pick.W[at];
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            for (var w = Settle(240); w.MoveNext();) yield return null;
            GameStatus.Lines["~bank"] = $"bank: half width {pick.W[at]:F1} depth {pick.D[at]:F1} banks {best:F1} m at {mid.x:F0},{mid.z:F0} eye {eye.x:F0},{eye.y:F0},{eye.z:F0}";
            Shot(cam, dir, "river-bank");
        }

        static int WidestCenote(Ground ground, out FeatureEdit c)
        {
            ref var g = ref ground.Blob.Value;
            int at = -1;
            for (int k = 0; k < g.Edits.Length; k++)
                if (g.Edits[k].Shape == FeatureEdit.Cenote && (at < 0 || g.Edits[k].D > g.Edits[at].D)) at = k;
            c = at < 0 ? default : g.Edits[at];
            return at;
        }
    }
}
