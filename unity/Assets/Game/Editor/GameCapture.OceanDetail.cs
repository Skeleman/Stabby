using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float OceanNoon = 12f, OceanGlitter = 16.75f, OceanEyeM = 7f, OceanOpenM = 1500f, OceanCoastM = 250f;

        static System.Collections.IEnumerator OceanDetailTour(Camera cam, string dir)
        {
            GeneratedWorld map = null;
            foreach (var w in Unity.Entities.World.All)
            {
                var s = w.GetExistingSystemManaged<ClientMapSystem>();
                if (s?.Map != null) { map = s.Map; break; }
            }
            if (map == null) { GameStatus.Lines["~ocean"] = "ocean: no map"; yield break; }
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 60f;
            int n = map.Config.Size;
            float cm = map.Config.CellMeters;
            bool SeaAt(Vector2 p)
            {
                int x = Mathf.RoundToInt(p.x / cm), y = Mathf.RoundToInt(p.y / cm);
                return x < 0 || y < 0 || x >= n || y >= n || map.Sea[y * n + x];
            }
            bool Spot(Vector2 at, Vector2 d, out Vector2 eye)
            {
                eye = at;
                for (float r = 0; r <= OceanOpenM; r += cm) if (!SeaAt(at + d * r)) return false;
                for (float r = cm; r <= OceanCoastM; r += cm) if (!SeaAt(at - d * r)) return true;
                return false;
            }
            Vector2 Find(Vector2 d)
            {
                var h = new Vector2(home.x, home.z);
                for (float r = 0; r < n * cm; r += cm * 2)
                    for (int a = 0; a < 64; a++)
                    {
                        var p = h + (Vector2)(Quaternion.Euler(0, 0, a * 5.625f) * Vector2.right) * r;
                        if (Spot(p, d, out var e)) return e;
                    }
                return h;
            }
            var shots = new System.Collections.Generic.List<string>();
            Vector3 benchEye = cam.transform.position;
            Quaternion benchRot = cam.transform.rotation;
            foreach (var (hour, tag) in new[] { (OceanNoon, "noon"), (OceanGlitter, "glitter") })
            {
                for (var w = ShowHour(hour); w.MoveNext();) yield return null;
                var sun = SunXZ();
                var d = tag == "noon" ? new Vector2(-sun.y, sun.x) : sun;
                var at = Find(d);
                if (tag == "noon" && !Spot(at, d, out _)) { d = -d; at = Find(d); }
                var eye = new Vector3(at.x, OceanEyeM, at.y);
                var fwd = new Vector3(d.x, 0, d.y);
                foreach (var (pitch, view) in new[] { (9f, "mid"), (1.5f, "far") })
                {
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(fwd) * Quaternion.Euler(pitch, 0, 0));
                    for (var s = Settle(240); s.MoveNext();) yield return null;
                    Shot(cam, null, "warmup");
                    string name = $"ocean-{tag}-{view}";
                    Shot(cam, dir, name);
                    shots.Add($"{name} eye {eye.x:F0},{eye.y:F0},{eye.z:F0} dir {d.x:F2},{d.y:F2} pitch {pitch}");
                    if (tag == "noon" && view == "far") { benchEye = cam.transform.position; benchRot = cam.transform.rotation; }
                }
                shots.Add($"hour {hour} moon full, sun elevation {SunElevation():F1}");
            }
            GameStatus.Lines["~ocean shots"] = string.Join("; ", shots);
            for (var w = ShowHour(OceanNoon); w.MoveNext();) yield return null;
            cam.transform.SetPositionAndRotation(benchEye, benchRot);
            for (var s = Settle(240); s.MoveNext();) yield return null;
            var sb = new System.Text.StringBuilder("bench ocean far noon (ms, median of 5):");
            foreach (var (bw, bh) in new[] { (1920, 1080), (3840, 2160) })
            {
                var ms = new double[5];
                for (int k = 0; k < ms.Length; k++) ms[k] = BenchAt(cam, bw, bh);
                System.Array.Sort(ms);
                sb.Append($" {bh}p {ms[2]:F2} ({ms[0]:F2}-{ms[4]:F2})");
            }
            GameStatus.Lines["~ocean bench"] = sb.ToString();
        }

        static float SunElevation()
        {
            Light sun = null;
            foreach (var lt in Object.FindObjectsByType<Light>())
                if (lt.type == LightType.Directional && lt.isActiveAndEnabled && (sun == null || lt.intensity > sun.intensity)) sun = lt;
            return sun == null ? 90f : Mathf.Asin(Mathf.Clamp(-sun.transform.forward.y, -1, 1)) * Mathf.Rad2Deg;
        }
    }
}
