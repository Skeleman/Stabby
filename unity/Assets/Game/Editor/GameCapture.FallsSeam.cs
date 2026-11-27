using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float SeamHour = 15.5f, SeamMoon = 0.5f;
        static readonly string[] FallsSeamShots = { "fallsseam-lip", "fallsseam-side", "fallsseam-foot", "fallsseam-wide" };

        static System.Collections.IEnumerator FallsSeamTour(Camera cam, string dir)
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
            if (sys == null) { GameStatus.Lines["~fallsseam"] = "no map"; yield break; }
            var f = default(FallCurtain);
            bool found = false;
            foreach (var l in NearField.Lines(sys.Map))
                for (int k = 1; k < l.X.Length; k++)
                    if (WaterGeometry.FallAt(l, k, out var c, out _) && c.Drop >= 3f && (!found || c.Discharge > f.Discharge)) { f = c; found = true; }
            if (!found) { GameStatus.Lines["~fallsseam"] = "no fall found"; yield break; }
            for (var w = ShowHour(SeamHour, SeamMoon); w.MoveNext();) yield return null;
            cam.fieldOfView = 60f;
            var fd = new Vector3(f.DirX, 0, f.DirZ);
            var lip = new Vector3(f.LipX, f.LipY, f.LipZ);
            float rise = ground.HeightAt(lip.x + fd.x * 8f, lip.z + fd.z * 8f) - ground.HeightAt(lip.x - fd.x * 8f, lip.z - fd.z * 8f);
            if (rise > 0) fd = -fd;
            var side = new Vector3(-fd.z, 0, fd.x);
            var foot = new Vector3(f.BaseX, f.BaseY, f.BaseZ);
            float h = f.Drop, wd = f.Width;
            GameStatus.Lines["~fallsseam"] = $"fall at {lip.x:F0},{lip.y:F0},{lip.z:F0}, drop {h:F1} m, width {wd:F1} m, pool {f.PoolRadius:F1} m, discharge {f.Discharge:F1}, ground rise downstream {(rise > 0 ? -rise : rise):F1} m{(rise > 0 ? " (dir flipped)" : "")}, hour {SeamHour}, moon {SeamMoon}";
            var looks = new[] { lip + fd * (0.3f * h), lip - Vector3.up * (0.5f * h), foot + Vector3.up * 0.3f, foot + Vector3.up * (0.4f * h) };
            for (int v = 0; v < looks.Length; v++)
            {
                var look = looks[v];
                var eye = look;
                float worst = float.MaxValue;
                for (int c = 0; c < 8; c++)
                {
                    float sx = (c & 1) == 0 ? 1 : -1, far = 1 + 0.5f * ((c >> 1) & 1), up = (c >> 2) == 0 ? 1f : 2f;
                    Vector3 e;
                    if (v == 0) e = lip - fd * (0.6f * wd + 6f) * far + side * (sx * 0.4f * wd) + Vector3.up * (1.5f + 0.15f * h) * up;
                    else if (v == 1) e = look + side * (sx * (0.8f * h + 0.5f * wd + 6f) * far) + fd * (0.4f * h) + Vector3.up * (0.1f * h) * up;
                    else if (v == 2) e = foot + fd * (0.6f * h + 4f) * far + side * (sx * (0.3f * wd + 2f)) + Vector3.up * (1.2f + 0.05f * h) * up;
                    else e = foot + fd * (2f * h + 0.6f * wd + 10f) * far + side * (sx * (0.4f * wd + 3f)) + Vector3.up * (0.5f * h + 3f) * up;
                    e.y = Mathf.Max(e.y, ground.HeightAt(e.x, e.z) + 1.8f);
                    float block = 0;
                    for (int k = 1; k < 10; k++)
                    {
                        var p = Vector3.Lerp(e, look, k / 10f);
                        block = Mathf.Max(block, ground.HeightAt(p.x, p.z) - p.y);
                    }
                    block += 0.02f * (e.y - look.y);
                    if (v == 0 && Vector3.Dot(e - lip, fd) >= 0) block += 1000f;
                    if (block < worst) { worst = block; eye = e; }
                }
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                for (var s = Settle(150); s.MoveNext();) yield return null;
                Shot(cam, null, "warmup");
                string name = FallsSeamShots[v];
                if (v == 0 && (worst > 0.5f || Vector3.Dot(eye - lip, fd) >= 0)) name += Vector3.Dot(eye - lip, fd) >= 0 ? "-downstream" : "-upstream-blocked";
                GameStatus.Lines["~fallsseam-" + v] = $"{name} eye {eye.x:F0},{eye.y:F0},{eye.z:F0} along-flow {Vector3.Dot(eye - lip, fd):F1} m, block {worst:F1} m";
                Shot(cam, dir, name);
            }
        }
    }
}
