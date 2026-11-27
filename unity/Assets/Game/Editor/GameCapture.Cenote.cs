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
        }

        static System.Collections.IEnumerator LakeShoreShot(Camera cam, string dir, Ground ground)
        {
            GeneratedWorld map = null;
            foreach (var w in Unity.Entities.World.All)
            {
                var s = w.GetExistingSystemManaged<ClientMapSystem>();
                if (s?.Map != null) { map = s.Map; break; }
            }
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
