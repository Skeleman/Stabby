using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float AncientHour = 15.5f, AncientMoon = 0.5f, AncientEyeUp = 3f, AncientNearM = 60f, AncientFrame = 1.45f;
        static readonly float[] AncientYaws = { 0, -20, 20, -35, 35, -50, 50 };

        static System.Collections.IEnumerator AncientTreeTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            ObjectField field = null;
            Ground ground = default;
            foreach (var w in Unity.Entities.World.All)
            {
                var f = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                var q = f == null ? default : w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (f == null || q.CalculateEntityCount() != 1) continue;
                field = f;
                ground = q.GetSingleton<Ground>();
                break;
            }
            if (field == null) { GameStatus.Lines["~ancienttree"] = GameStatus.Failure = "ancient tree: no object field"; yield break; }
            int def = field.Library.Objects.FindIndex(o => o.Id == "ancient-tree");
            var job = System.Threading.Tasks.Task.Run(() => field.Discoveries());
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~ancienttree"] = GameStatus.Failure = "ancient tree: " + job.Exception; yield break; }
            var all = job.Result.Where(o => o.Def == def).ToArray();
            if (all.Length == 0) { GameStatus.Lines["~ancienttree"] = GameStatus.Failure = "ancient tree: none placed in this world"; yield break; }
            var s = all.OrderBy(o => new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude).First();
            float baseY = ground.HeightAt(s.X, s.Z);
            var centre = new Vector3(s.X, baseY, s.Z);
            cam.fieldOfView = 60f;
            for (var w = ShowHour(AncientHour, AncientMoon); w.MoveNext();) yield return null;
            var sky = DayNightSystem.Shown;
            var sun = new Vector3(sky.SunX, 0, sky.SunZ).normalized;
            var near = new System.Collections.Generic.List<ObjectInstance>();
            var chunk = new System.Collections.Generic.List<ObjectInstance>();
            int across = field.ChunksAcross, cx = (int)(s.X / field.ChunkMeters), cz = (int)(s.Z / field.ChunkMeters);
            for (int z = Mathf.Max(0, cz - 1); z <= Mathf.Min(across - 1, cz + 1); z++)
                for (int x = Mathf.Max(0, cx - 1); x <= Mathf.Min(across - 1, cx + 1); x++) { field.SolidInto(x, z, chunk); near.AddRange(chunk); }
            near.RemoveAll(o => o.Def == def || !field.Library.Objects[o.Def].Parts.Exists(p => ObjectPart.IsFoliage(p.Shape)) || new Vector2(o.X - s.X, o.Z - s.Z).magnitude > AncientNearM + 20f);
            var tops = new System.Collections.Generic.Dictionary<(int, int), float>();
            float Top(ObjectInstance o)
            {
                if (tops.TryGetValue((o.Def, o.Variant), out float t)) return t * o.Size;
                var d = field.Library.Objects[o.Def];
                t = 0;
                for (int p = 0; p < d.Parts.Count; p++)
                {
                    var m = ObjectMeshes.Build(d.Parts[p], ObjectMeshes.Key(d.Id, p, o.Variant), 2);
                    for (int k = 1; k < m.Positions.Length; k += 3) t = Mathf.Max(t, d.Parts[p].OffsetY + d.Parts[p].ScaleY * m.Positions[k]);
                }
                tops[(o.Def, o.Variant)] = t;
                return t * o.Size;
            }
            float height = Top(s);
            var canopy = near.Where(o => new Vector2(o.X - s.X, o.Z - s.Z).magnitude <= AncientNearM).Select(o => Top(o)).OrderBy(h => h).ToArray();
            float median = canopy.Length > 0 ? canopy[canopy.Length / 2] : 0, tallest = canopy.Length > 0 ? canopy[canopy.Length - 1] : 0;
            float dist = AncientFrame * height, best = float.MaxValue, yawBest = 0;
            var sill = centre + Vector3.up * 1.5f;
            var eye = centre;
            foreach (float yaw in AncientYaws)
            {
                var way = Quaternion.Euler(0, yaw, 0) * sun;
                var e = centre + way * dist;
                e.y = ground.HeightAt(e.x, e.z) + AncientEyeUp;
                float score = Clear(ground, e, sill) ? Mathf.Abs(yaw) * 0.01f : 1000f;
                foreach (var o in near)
                {
                    var q = new Vector2(o.X - e.x, o.Z - e.z);
                    var seg = new Vector2(centre.x - e.x, centre.z - e.z);
                    float t = Mathf.Clamp01(Vector2.Dot(q, seg) / seg.sqrMagnitude);
                    if (t > 0.85f) continue;
                    if ((q - seg * t).magnitude < 0.22f * o.Size + 1f) score += 2f - t;
                }
                if (score < best) { best = score; yawBest = yaw; eye = e; }
            }
            for (int i = 0; i < 40 && !Clear(ground, eye, sill); i++) eye.y += 0.25f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(centre + Vector3.up * (0.45f * height) - eye));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            GameStatus.Lines["~ancienttree"] = $"ancient tree: {all.Length} placed, shown {s.X:F0},{s.Z:F0} size {s.Size:F1} variant {s.Variant}, ground {baseY:F2} woods {field.Woods(s.X, s.Z):F2}, GV {WorldConfig.GeneratorVersion}, pinned hour {sky.Hour:F2}, height {height:F1} m vs neighbour canopy median {median:F1} max {tallest:F1} ({canopy.Length} trees within {AncientNearM:F0} m), eye yaw {yawBest:F0} from sun at {dist:F0} m, blockers {best:F1}";
            Shot(cam, dir, "ancient-tree");
        }
    }
}
