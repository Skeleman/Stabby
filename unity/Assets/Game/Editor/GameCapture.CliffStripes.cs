using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator CliffStripesTour(Camera cam, string dir)
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
            if (sys == null) { GameStatus.Lines["~cliffstripes"] = "cliffstripes: no map"; yield break; }
            GameStatus.HoldCamera = true;
            for (var h = ShowHour(11f); h.MoveNext();) yield return null;
            var map = sys.Map;
            int n = map.Config.Size;
            float cm = map.Config.CellMeters;
            float Slope(Vector2 p, float r)
            {
                float gx = (ground.HeightAt(p.x + r, p.y) - ground.HeightAt(p.x - r, p.y)) / (2 * r);
                float gz = (ground.HeightAt(p.x, p.y + r) - ground.HeightAt(p.x, p.y - r)) / (2 * r);
                return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
            }
            var picks = new Vector2[2];
            var downs = new Vector2[2];
            var found = new bool[2];
            var dist = new float[] { float.MaxValue, float.MaxValue };
            var angle = new float[2];
            for (int z = 2; z < n - 2; z++)
                for (int x = 2; x < n - 2; x++)
                {
                    int i = z * n + x;
                    if (map.Sea[i] || map.Height[i] < 3) continue;
                    var p = new Vector2(x * cm, z * cm);
                    float d = (p - new Vector2(home.x, home.z)).sqrMagnitude;
                    if (d > 2000f * 2000f) continue;
                    float gx = (map.Height[i + 1] - map.Height[i - 1]) / (2 * cm), gz = (map.Height[i + n] - map.Height[i - n]) / (2 * cm);
                    if (gx * gx + gz * gz < 0.5f) continue;
                    float a = Slope(p, 2f);
                    int k = a > 40f && a < 60f && Mathf.Abs(Slope(p, 6f) - 50f) < 8f ? 0 : a > 70f ? 1 : -1;
                    if (k < 0) continue;
                    if (k == 0 && d >= dist[0]) continue;
                    if (k == 1)
                    {
                        a = Mathf.Min(a, Slope(p, 1f));
                        if (a <= angle[1]) continue;
                    }
                    float ex = ground.HeightAt(p.x + 4f, p.y) - ground.HeightAt(p.x - 4f, p.y), ez = ground.HeightAt(p.x, p.y + 4f) - ground.HeightAt(p.x, p.y - 4f);
                    dist[k] = d; picks[k] = p; downs[k] = new Vector2(-ex, -ez).normalized; found[k] = true; angle[k] = a;
                }
            string[] kinds = { "moderate", "sheer" };
            cam.fieldOfView = 60f;
            for (int k = 0; k < 2; k++)
            {
                if (!found[k]) { GameStatus.Lines["~cliffstripes " + kinds[k]] = "cliffstripes: no " + kinds[k] + " slope found"; continue; }
                var target = new Vector3(picks[k].x, 0, picks[k].y);
                target.y = ground.HeightAt(target.x, target.z);
                var dn = new Vector3(downs[k].x, 0, downs[k].y);
                GameStatus.Lines["~cliffstripes " + kinds[k]] = $"cliffstripes {kinds[k]} at {target.x:F0},{target.y:F0},{target.z:F0}, {angle[k]:F0} deg, {Mathf.Sqrt(dist[k]):F0} m from the player";
                var eye = target + dn * 22f + Vector3.up * 10f;
                eye.y = Mathf.Max(eye.y, ground.HeightAt(eye.x, eye.z) + 1.7f);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target + Vector3.up * 2f - eye));
                for (var w = Settle(240); w.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, "cliffstripes-" + kinds[k]);
            }
        }
    }
}
