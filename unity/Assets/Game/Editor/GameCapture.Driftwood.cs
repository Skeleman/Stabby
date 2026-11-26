using System.Collections.Generic;
using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float DriftHour = 15.5f, DriftMoon = 0.5f, DriftEyeUp = 1.6f, DriftNearM = 8f, DriftSearchM = 700f, DriftCrowdM = 10f, DriftEyeRise = 0.6f;
        const int DriftWantRiver = 40, DriftWantLake = 15;

        static System.Collections.IEnumerator DriftwoodTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            ObjectField field = null;
            foreach (var w in Unity.Entities.World.All)
            {
                var f = w.Name == GameSettings.ViewClient ? w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field : null;
                if (f != null) { field = f; break; }
            }
            if (field == null) { GameStatus.Lines["~drift"] = GameStatus.Failure = "drift: no object field"; yield break; }
            var ids = new[] { "driftwood", "drift-fork", "drift-pile" };
            var defs = ids.Select(id => field.Library.Objects.FindIndex(o => o.Id == id)).ToArray();
            var centre = home;
            var job = System.Threading.Tasks.Task.Run(() =>
            {
                var found = new List<ObjectInstance>();
                float span = field.ChunkMeters;
                int across = field.ChunksAcross;
                var order = new List<(float d, int cx, int cz)>();
                for (int cz = 0; cz < across; cz++)
                    for (int cx = 0; cx < across; cx++)
                    {
                        float mx = (cx + 0.5f) * span - centre.x, mz = (cz + 0.5f) * span - centre.z;
                        order.Add((mx * mx + mz * mz, cx, cz));
                    }
                order.Sort((a, b) => a.d.CompareTo(b.d));
                int onLake = 0;
                foreach (var (d, cx, cz) in order)
                {
                    if (d > DriftSearchM * DriftSearchM && onLake >= DriftWantLake && found.Count - onLake >= DriftWantRiver) break;
                    foreach (var o in field.Chunk(cx, cz))
                        if (System.Array.IndexOf(defs, o.Def) >= 0) { found.Add(o); if (field.OnLakeShore(o.X, o.Z)) onLake++; }
                }
                return found;
            });
            while (!job.IsCompleted) yield return null;
            if (job.IsFaulted) { GameStatus.Lines["~drift"] = GameStatus.Failure = "drift: " + job.Exception; yield break; }
            var all = job.Result;
            var lake = all.Where(o => field.OnLakeShore(o.X, o.Z)).ToList();
            var river = all.Where(o => !field.OnLakeShore(o.X, o.Z)).ToList();
            if (river.Count == 0 || lake.Count == 0) { GameStatus.Lines["~drift"] = GameStatus.Failure = $"drift: {river.Count} river and {lake.Count} lake pieces in the whole world"; yield break; }
            int Crowd(ObjectInstance o) => all.Count(p => (p.X - o.X) * (p.X - o.X) + (p.Z - o.Z) * (p.Z - o.Z) < DriftCrowdM * DriftCrowdM) + (all.Any(p => p.Def != defs[0] && (p.X - o.X) * (p.X - o.X) + (p.Z - o.Z) * (p.Z - o.Z) < DriftCrowdM * DriftCrowdM) ? 4 : 0);
            var bend = river.OrderByDescending(Crowd).ThenBy(o => new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude).First();
            var shore = lake.OrderByDescending(o => o.Def == defs[2] ? 0 : 1).ThenByDescending(Crowd).ThenBy(o => new Vector2(o.X - home.x, o.Z - home.z).sqrMagnitude).First();
            cam.fieldOfView = 60f;
            for (var w = ShowHour(DriftHour, DriftMoon); w.MoveNext();) yield return null;
            var lines = new List<string> { $"drift: GV {WorldConfig.GeneratorVersion}, scanned outward from home: {string.Join(", ", ids.Select((id, k) => id + " " + all.Count(o => o.Def == defs[k])))}; {river.Count} on rivers, {lake.Count} on lake shores" };
            foreach (var (o, name) in new[] { (bend, "driftwood-bend"), (shore, "driftwood-lake") })
            {
                float gy = field.GroundAt(o.X, o.Z);
                var target = new Vector3(o.X, gy + 0.3f, o.Z);
                Vector3 eye = default;
                float best = float.MaxValue;
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    var e = target + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * DriftNearM;
                    float ey = field.GroundAt(e.x, e.z), cost = Mathf.Abs(ey - gy - DriftEyeRise) + (ey < gy ? 10f : 0f);
                    if (cost < best) { best = cost; eye = new Vector3(e.x, ey + DriftEyeUp, e.z); }
                }
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                for (var w = Settle(180); w.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, name);
                lines.Add($"{name}: {ids[System.Array.IndexOf(defs, o.Def)]} at {o.X:F0},{gy:F1},{o.Z:F0}, {Crowd(o)} crowd score, eye {eye.x:F0},{eye.y:F1},{eye.z:F0}, pinned hour {DayNightSystem.Shown.Hour:F2}, moon {DayNightSystem.Shown.Phase:F2}");
            }
            GameStatus.Lines["~drift"] = string.Join("\n", lines);
        }
    }
}
