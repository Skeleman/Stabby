using System;
using System.Globalization;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float CoastFallHour = 15f, CoastFallMoon = 0.5f;
        const int CoastFallSearchCells = 8;
        const string CoastFallAtVariable = "STABBY_COASTFALL_AT";

        static System.Collections.IEnumerator CoastFallTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
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
            if (sys == null) { GameStatus.Lines["~coastfall"] = GameStatus.Failure = "coastfall: no map"; yield break; }
            var map = sys.Map;
            var inv = CultureInfo.InvariantCulture;
            int n = map.Config.Size, near = 0;
            float cm = map.Config.CellMeters;
            var best = default(FallCurtain);
            int bestD = int.MaxValue;
            foreach (var l in NearField.Lines(map))
                for (int k = 1; k < l.X.Length; k++)
                {
                    if (!WaterGeometry.FallAt(l, k, out var f, out _)) continue;
                    int d = SeaCells(map, Mathf.Clamp((int)(f.BaseX / cm), 0, n - 1), Mathf.Clamp((int)(f.BaseZ / cm), 0, n - 1));
                    if (d <= Hydrology.FallCoastCells) near++;
                    if (d < bestD || d == bestD && f.Discharge > best.Discharge) { best = f; bestD = d; }
                }
            Vector3 foot, fd;
            var at = Environment.GetEnvironmentVariable(CoastFallAtVariable);
            if (!string.IsNullOrEmpty(at))
            {
                var p = at.Split(',');
                foot = new Vector3(float.Parse(p[0], inv), 0, float.Parse(p[1], inv));
                foot.y = Mathf.Max(0f, ground.HeightAt(foot.x, foot.z));
                fd = new Vector3(float.Parse(p[2], inv), 0, float.Parse(p[3], inv)).normalized;
            }
            else if (bestD == int.MaxValue) { GameStatus.Lines["~coastfall"] = GameStatus.Failure = "coastfall: no fall found"; yield break; }
            else
            {
                foot = new Vector3(best.BaseX, best.BaseY, best.BaseZ);
                fd = new Vector3(best.DirX, 0, best.DirZ);
            }
            var side = new Vector3(fd.z, 0, -fd.x);
            cam.fieldOfView = 60f;
            for (var e = ShowHour(CoastFallHour, CoastFallMoon); e.MoveNext();) yield return null;
            var eye = foot + fd * 40f + side * 18f;
            eye.y = Mathf.Max(foot.y + 10f, Mathf.Max(0f, ground.HeightAt(eye.x, eye.z)) + 6f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(foot + Vector3.up * 2f - eye));
            for (var e = Settle(180); e.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "coastfall-foot");
            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~coastfall"] = string.Format(inv, "coastfall: foot {0:F1},{1:F1},{2:F1} dir {3:F3},{4:F3}, nearest fall {5} cells from sea (drop {6:F1} m, width {7:F0} m), {8} fall points within {9} cells of sea, GV {10}, hour {11:F2} moon {12:F2}",
                foot.x, foot.z, foot.y, fd.x, fd.z, bestD == int.MaxValue ? -1 : bestD, best.Drop, best.Width, near, Hydrology.FallCoastCells, WorldConfig.GeneratorVersion, sky.Hour, sky.Phase);
        }

        static int SeaCells(GeneratedWorld map, int x, int y)
        {
            int n = map.Config.Size;
            for (int r = 0; r <= CoastFallSearchCells; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        int cx = x + dx, cy = y + dy;
                        if (cx >= 0 && cy >= 0 && cx < n && cy < n && map.Sea[cy * n + cx]) return r;
                    }
            return CoastFallSearchCells + 1;
        }
    }
}
