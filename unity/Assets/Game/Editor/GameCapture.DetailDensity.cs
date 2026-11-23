using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator DetailTour(Camera cam, string dir)
        {
            if (!CloseUp(cam, 0)) { GameStatus.Lines["~detail"] = "detail: no rock found"; yield break; }
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
