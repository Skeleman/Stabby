using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator WaterfallTour(Camera cam, string dir)
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
            if (sys == null) { GameStatus.Lines["~waterfall"] = "waterfall: no map"; yield break; }
            var best = default(FallCurtain);
            bool found = false;
            foreach (var l in NearField.Lines(sys.Map))
                for (int k = 1; k < l.X.Length; k++)
                    if (WaterGeometry.FallAt(l, k, out var f, out _) && f.Drop >= 3f && (!found || f.Discharge > best.Discharge)) { best = f; found = true; }
            if (!found) { GameStatus.Lines["~waterfall"] = "waterfall: no fall found"; yield break; }
            var fd = new Vector3(best.DirX, 0, best.DirZ);
            var side = new Vector3(fd.z, 0, -fd.x);
            var lip = new Vector3(best.LipX, best.LipY, best.LipZ);
            var foot = new Vector3(best.BaseX, best.BaseY, best.BaseZ);
            float h = best.Drop, wd = best.Width;
            GameStatus.Lines["~waterfall"] = $"fall at {lip.x:F0},{lip.y:F0},{lip.z:F0}, drop {h:F1} m, width {wd:F0} m, discharge {best.Discharge:F0}, pool {best.PoolRadius:F1} m";
            cam.fieldOfView = 60f;
            float d = Mathf.Max(12f, 0.6f * wd + 1.2f * h);
            var a = lip + side * (0.5f * wd + d) + fd * (0.3f * h);
            var b = lip - side * (0.5f * wd + d) + fd * (0.3f * h);
            var eye = ground.HeightAt(a.x, a.z) <= ground.HeightAt(b.x, b.z) ? a : b;
            eye.y = Mathf.Max(lip.y + 0.25f * h + 2f, ground.HeightAt(eye.x, eye.z) + 1.8f);
            var look = lip + fd * (0.15f * h) - Vector3.up * (0.3f * h);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            for (var e = Settle(180); e.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "waterfall-lip");
            var fside = eye.x == a.x && eye.z == a.z ? side : -side;
            eye = foot + fd * (5f + 0.5f * h) + fside * (0.25f * wd + 3f);
            eye.y = Mathf.Max(foot.y + 1.5f + 0.1f * h, ground.HeightAt(eye.x, eye.z) + 1.8f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(foot + Vector3.up * 0.5f - eye));
            for (var e = Settle(120); e.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "waterfall-foot");
        }
    }
}
