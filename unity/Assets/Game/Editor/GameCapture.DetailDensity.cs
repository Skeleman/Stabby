using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator DetailTour(Camera cam, string dir)
        {
            if (!FixedRock(cam, 11531, 11475)) { GameStatus.Lines["~detail"] = "detail: no rock found"; yield break; }
            var eye = cam.transform.position;
            var target = eye + cam.transform.forward * 14.1f;
            for (var e = Settle(180); e.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "detail-rock-close");
            GameStatus.Lines["~bench detail close"] = MedianBench(cam, "rock close");
            var mid = target - cam.transform.forward * 45f + Vector3.up * 12f;
            cam.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(target - mid));
            for (var e = Settle(180); e.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "detail-rock-mid");
            GameStatus.Lines["~bench detail mid"] = MedianBench(cam, "rock mid");
            if (AimInn(cam, 1))
            {
                for (var e = Settle(180); e.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, "detail-object");
            }
            if (AimVent(cam, 3))
            {
                for (var e = Settle(180); e.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, "detail-sinter");
                TerrainViewSystem.ClockOverride = null;
            }
        }

        static bool FixedRock(Camera cam, float tx, float tz)
        {
            foreach (var w in Unity.Entities.World.All)
            {
                var sys = w.GetExistingSystemManaged<ClientMapSystem>();
                var map = sys?.Map;
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (map == null || q.CalculateEntityCount() != 1) continue;
                var ground = q.GetSingleton<Ground>();
                int n = map.Config.Size;
                float cm = map.Config.CellMeters;
                int x = Mathf.RoundToInt(tx / cm), z = Mathf.RoundToInt(tz / cm), i = z * n + x;
                if (x < 1 || z < 1 || x >= n - 1 || z >= n - 1) return false;
                float gx = (map.Height[i + 1] - map.Height[i - 1]) / (2 * cm), gz = (map.Height[i + n] - map.Height[i - n]) / (2 * cm);
                var down = new Vector2(-gx, -gz).normalized;
                var target = new Vector3(x * cm, 0, z * cm);
                target.y = ground.HeightAt(target.x, target.z);
                var eye = target + new Vector3(down.x, 0, down.y) * 14f + Vector3.up * 2f;
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.5f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                GameStatus.Lines["~aim close 0"] = $"rock at {target.x:F0},{target.y:F0},{target.z:F0} (fixed)";
                return true;
            }
            return false;
        }

        static string MedianBench(Camera cam, string label)
        {
            var sb = new System.Text.StringBuilder($"bench detail {label} (ms, median of 3):");
            foreach (var (w, h) in new[] { (1920, 1080), (3840, 2160) })
            {
                var ms = new double[3];
                for (int k = 0; k < 3; k++) ms[k] = BenchAt(cam, w, h);
                System.Array.Sort(ms);
                sb.Append($" {h}p {ms[1]:F2} ({ms[0]:F2}-{ms[2]:F2})");
            }
            return sb.ToString();
        }
    }
}
