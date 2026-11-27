using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static readonly string[] FallsMistShots = { "fallsmist-small", "fallsmist-big", "fallsmist-big-far" };

        static System.Collections.IEnumerator FallsMistTour(Camera cam, string dir)
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
            if (sys == null) { GameStatus.Lines["~fallsmist"] = "no map"; yield break; }
            FallCurtain small = default, big = default;
            bool hasSmall = false, hasBig = false;
            foreach (var l in NearField.Lines(sys.Map))
                for (int k = 1; k < l.X.Length; k++)
                    if (WaterGeometry.FallAt(l, k, out var f, out _))
                    {
                        if (f.Drop >= 1.5f && (!hasSmall || f.Drop < small.Drop)) { small = f; hasSmall = true; }
                        if (!hasBig || f.Drop > big.Drop) { big = f; hasBig = true; }
                    }
            if (!hasBig) { GameStatus.Lines["~fallsmist"] = "no falls found"; yield break; }
            cam.fieldOfView = 60f;
            var picks = new[] { small, big, big };
            for (int v = 0; v < picks.Length; v++)
            {
                if (v == 0 && !hasSmall) continue;
                var f = picks[v];
                var fd = new Vector3(f.DirX, 0, f.DirZ);
                var side = new Vector3(-fd.z, 0, fd.x);
                var lip = new Vector3(f.LipX, f.LipY, f.LipZ);
                var foot = new Vector3(f.BaseX, f.BaseY, f.BaseZ);
                float h = f.Drop, wd = f.Width, back = v == 2 ? 4f * h + 60f : 1.6f * h + 0.5f * wd + 6f;
                var look = foot + Vector3.up * (0.35f * h);
                var eye = look;
                float worst = float.MaxValue;
                for (int c = 0; c < 8; c++)
                {
                    float sx = (c & 1) == 0 ? 1 : -1, reach = back * (1 + 0.5f * ((c >> 1) & 1)), lift = (c >> 2) == 0 ? 0.4f * h + 1.5f : 0.8f * h + 4f;
                    var e = foot + fd * reach + side * (sx * (0.3f * wd + 2f)) + Vector3.up * lift;
                    e.y = Mathf.Max(e.y, ground.HeightAt(e.x, e.z) + 1.8f);
                    float block = 0;
                    for (int k = 1; k < 10; k++)
                    {
                        var p = Vector3.Lerp(e, look, k / 10f);
                        block = Mathf.Max(block, ground.HeightAt(p.x, p.z) - p.y);
                    }
                    block += 0.02f * (e.y - look.y);
                    if (block < worst) { worst = block; eye = e; }
                }
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                GameStatus.Lines["~" + FallsMistShots[v]] = $"fall at {lip.x:F0},{lip.y:F0},{lip.z:F0}, drop {h:F1} m, width {wd:F1} m, pool {f.PoolRadius:F1} m, discharge {f.Discharge:F1}";
                for (var s = Settle(180); s.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                Shot(cam, dir, FallsMistShots[v]);
            }
        }
    }
}
