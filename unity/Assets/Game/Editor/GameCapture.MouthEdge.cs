using System.Collections.Generic;
using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float MouthEdgeHour = 13f;

        static System.Collections.IEnumerator MouthEdgeTour(Camera cam, string dir)
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
            if (sys == null) { GameStatus.Lines["~mouthedge"] = GameStatus.Failure = "mouthedge: no map"; yield break; }
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 60f;
            for (var h = ShowHour(MouthEdgeHour); h.MoveNext();) yield return null;
            var map = sys.Map;
            float cm = map.Config.CellMeters;
            var mouths = new List<RiverSegment>();
            foreach (var r in map.Water.Rivers)
                if (r.End == RiverEnd.Sea && r.Cells.Count >= 8) mouths.Add(r);
            mouths.Sort((a, b) => b.Discharge[b.Cells.Count - 2].CompareTo(a.Discharge[a.Cells.Count - 2]));
            var line = $"mouthedge: hour {MouthEdgeHour}, moon full";
            for (int i = 0; i < Mathf.Min(2, mouths.Count); i++)
            {
                var r = mouths[i];
                int m = r.Cells.Count;
                var end = new Vector3(r.X[m - 1] * cm, 0, r.Y[m - 1] * cm);
                var up = new Vector3(r.X[m - 6] * cm, 0, r.Y[m - 6] * cm);
                var along = (end - up).normalized;
                var side = new Vector3(along.z, 0, -along.x);
                var eye = end - along * 140f + side * 70f;
                eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z), 0) + 55f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(end + along * 40f - eye));
                for (var e = Settle(240); e.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, $"mouthedge-{i}-wide");
                var low = end - along * 50f + side * 25f;
                low.y = Mathf.Max(ground.HeightAt(low.x, low.z), 0) + 14f;
                cam.transform.SetPositionAndRotation(low, Quaternion.LookRotation(end + along * 30f - low));
                for (var e = Settle(120); e.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, $"mouthedge-{i}-close");
                line += $"; river {r.Id} end {end.x:F0},{end.z:F0} wide eye {eye.x:F0},{eye.y:F0},{eye.z:F0}";
            }
            GameStatus.Lines["~mouthedge"] = line;
        }
    }
}
