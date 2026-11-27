using System.Globalization;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float DeltaHour = 15f, DeltaMoon = 0.5f;

        static System.Collections.IEnumerator DeltaTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            ClientMapSystem sys = null;
            Ground ground = default;
            foreach (var w in Unity.Entities.World.All)
            {
                if (w.Name != GameSettings.ViewClient) continue;
                var s = w.GetExistingSystemManaged<ClientMapSystem>();
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (s?.Map == null || q.CalculateEntityCount() != 1) continue;
                sys = s;
                ground = q.GetSingleton<Ground>();
                break;
            }
            if (sys == null) { GameStatus.Lines["~delta"] = GameStatus.Failure = "delta: no map"; yield break; }
            var map = sys.Map;
            var hy = map.Water;
            if (hy.Deltas.Count == 0) { GameStatus.Lines["~delta"] = GameStatus.Failure = "delta: none on seed " + map.Config.Seed; yield break; }
            RiverDelta d = hy.Deltas[0];
            foreach (var c in hy.Deltas) if (c.Heads.Count > d.Heads.Count) d = c;
            int n = map.Config.Size;
            float cm = map.Config.CellMeters;
            var inv = CultureInfo.InvariantCulture;
            var apex = new Vector3(d.Apex % n * cm, 0, d.Apex / n * cm);
            apex.y = ground.HeightAt(apex.x, apex.z);
            var centre = Vector3.zero;
            float reach = 0;
            RiverSegment branch = null;
            foreach (int id in d.Branches)
            {
                var r = hy.Rivers[id];
                int m = r.Cells.Count - 1;
                var end = new Vector3(r.X[m] * cm, 0, r.Y[m] * cm);
                centre += end;
                float dist = (end - apex).magnitude;
                if (dist > reach) reach = dist;
                if (branch == null || r.Cells.Count > branch.Cells.Count) branch = r;
            }
            centre /= d.Branches.Count;
            var fd = centre - apex;
            fd.y = 0;
            float span = Mathf.Max(fd.magnitude, 200f);
            fd /= fd.magnitude;
            cam.fieldOfView = 55f;
            for (var w = ShowHour(DeltaHour, DeltaMoon); w.MoveNext();) yield return null;
            var eye = apex - fd * (0.9f * span + 300f);
            eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z), 0) + 0.8f * span + 150f;
            var aim = Vector3.Lerp(apex, centre, 0.6f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
            for (var w = Settle(240); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~delta"] = string.Format(inv, "delta: apex {0:F0},{1:F0},{2:F0}, {3} distributaries, reach {4:F0} m, eye {5:F0},{6:F0},{7:F0}, GV {8}, pinned hour {9:F2} moon {10:F2}",
                apex.x, apex.y, apex.z, d.Heads.Count + 1, reach, eye.x, eye.y, eye.z, WorldConfig.GeneratorVersion, sky.Hour, sky.Phase);
            Shot(cam, dir, "delta-far");
            int k = branch.Cells.Count - 1;
            var mouth = new Vector3(branch.X[k] * cm, 0, branch.Y[k] * cm);
            var up = new Vector3(branch.X[Mathf.Max(0, k - 6)] * cm, 0, branch.Y[Mathf.Max(0, k - 6)] * cm);
            var along = (mouth - up).normalized;
            var side = new Vector3(along.z, 0, -along.x);
            mouth.y = hy.WaterSurface[branch.Cells[k]];
            var eye2 = mouth - along * 90f + side * 25f;
            eye2.y = Mathf.Max(ground.HeightAt(eye2.x, eye2.z), mouth.y) + 22f;
            cam.transform.SetPositionAndRotation(eye2, Quaternion.LookRotation(mouth + along * 30f - eye2));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            GameStatus.Lines["~delta mouth"] = string.Format(inv, "delta mouth: river {0} ends {1} at {2:F0},{3:F0},{4:F0}", branch.Id, branch.End, mouth.x, mouth.y, mouth.z);
            Shot(cam, dir, "delta-mouth");
        }
    }
}
