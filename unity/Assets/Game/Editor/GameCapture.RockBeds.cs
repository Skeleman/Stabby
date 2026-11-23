using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator RockBedsTour(Camera cam, string dir)
        {
            ClientMapSystem sys = null;
            Ground ground = default;
            foreach (var w in Unity.Entities.World.All)
            {
                var s = w.GetExistingSystemManaged<ClientMapSystem>();
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (s?.Map == null || s.Materials == null || q.CalculateEntityCount() != 1) continue;
                sys = s;
                ground = q.GetSingleton<Ground>();
                break;
            }
            if (sys == null) { GameStatus.Lines["~rockbeds"] = "rockbeds: no map"; yield break; }
            var map = sys.Map;
            var offs = sys.Materials.Offsets(map.Config);
            int n = map.Config.Size;
            float cm = map.Config.CellMeters;
            int pick = -1;
            Vector2 down = Vector2.zero;
            float best = float.MinValue, wall = 0;
            for (int z = 2; z < n - 2; z++)
                for (int x = 2; x < n - 2; x++)
                {
                    int i = z * n + x;
                    if (map.Sea[i] || map.Height[i] < 3 || Sedimentary(offs[i]) < 0.9f) continue;
                    float gx = (map.Height[i + 1] - map.Height[i - 1]) / (2 * cm), gz = (map.Height[i + n] - map.Height[i - n]) / (2 * cm), s = Mathf.Sqrt(gx * gx + gz * gz);
                    if (s < 1.2f) continue;
                    var p = new Vector2(x * cm, z * cm);
                    var dn = new Vector2(-gx, -gz) / s;
                    var lo = p + dn * 15f;
                    var hi = p - dn * 15f;
                    float hgt = ground.HeightAt(hi.x, hi.y) - ground.HeightAt(lo.x, lo.y);
                    float d = (p - new Vector2(home.x, home.z)).magnitude;
                    float score = Mathf.Min(hgt, 40f) - d * 0.01f;
                    if (score > best) { best = score; pick = i; down = dn; wall = hgt; }
                }
            if (pick < 0) { GameStatus.Lines["~rockbeds"] = "rockbeds: no sedimentary wall"; yield break; }
            var target = new Vector3(pick % n * cm, 0, pick / n * cm);
            target.y = ground.HeightAt(target.x, target.z);
            var dn3 = new Vector3(down.x, 0, down.y);
            GameStatus.Lines["~rockbeds"] = $"rockbeds wall at {target.x:F0},{target.y:F0},{target.z:F0}, {wall:F0} m over 30 m, offset {offs[pick]:F1}";
            cam.fieldOfView = 60f;
            foreach (float back in new[] { 10f, 40f })
            {
                var eye = target + dn3 * back + Vector3.up * (back * 0.15f);
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.7f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target + Vector3.up * 2f - eye));
                for (var w = Settle(240); w.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, $"rockbeds-{back:F0}m");
            }
        }
    }
}
