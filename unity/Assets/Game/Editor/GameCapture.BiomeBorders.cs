using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const int BorderWindow = 12, BorderReach = 400;
        const float BorderFov = 50f;

        static System.Collections.IEnumerator BiomeBordersTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            cam.fieldOfView = BorderFov;
            var map = ShownMap();
            if (map == null) { GameStatus.Lines["~biomeborders"] = GameStatus.Failure = "biomeborders: no map"; yield break; }
            for (var w = ShowHour(PinnedMidday); w.MoveNext();) yield return null;
            int n = map.Config.Size, hx = (int)(home.x / map.Config.CellMeters), hz = (int)(home.z / map.Config.CellMeters);
            float cm = map.Config.CellMeters, best = float.MinValue;
            int bx = -1, bz = -1, pickA = -1, pickB = -1;
            for (int z0 = Mathf.Max(0, hz - BorderReach); z0 < Mathf.Min(n - BorderWindow, hz + BorderReach); z0 += 2)
                for (int x0 = Mathf.Max(0, hx - BorderReach); x0 < Mathf.Min(n - BorderWindow, hx + BorderReach); x0 += 2)
                {
                    int a = -1, b = -1, h = 0, v = 0;
                    float lo = float.MaxValue, hi = float.MinValue;
                    bool ok = true;
                    for (int z = z0; z < z0 + BorderWindow && ok; z++)
                        for (int x = x0; x < x0 + BorderWindow; x++)
                        {
                            int i = z * n + x, c = map.Biome[i];
                            if (map.Sea[i] || c < 0 || map.Water.WaterSurface[i] > map.Height[i] + 0.05f) { ok = false; break; }
                            if (a < 0) a = c; else if (c != a && b < 0) b = c; else if (c != a && c != b) { ok = false; break; }
                            lo = Mathf.Min(lo, map.Height[i]);
                            hi = Mathf.Max(hi, map.Height[i]);
                            if (x + 1 < x0 + BorderWindow && map.Biome[i + 1] != c) h++;
                            if (z + 1 < z0 + BorderWindow && map.Biome[i + n] != c) v++;
                        }
                    if (!ok || b < 0 || h < 3 || v < 3) continue;
                    float score = Mathf.Min(h, v) - 0.5f * Mathf.Abs(h - v) - 0.005f * Mathf.Max(Mathf.Abs(x0 - hx), Mathf.Abs(z0 - hz)) - 0.5f * (hi - lo) / cm;
                    if (score > best) { best = score; bx = x0; bz = z0; pickA = a; pickB = b; }
                }
            if (bx < 0) { GameStatus.Lines["~biomeborders"] = GameStatus.Failure = "biomeborders: no diagonal border near home"; yield break; }
            float cx = (bx + 0.5f * BorderWindow) * cm, cz = (bz + 0.5f * BorderWindow) * cm;
            float up = 0.5f * BorderWindow * cm / Mathf.Tan(BorderFov * 0.5f * Mathf.Deg2Rad);
            var eye = new Vector3(cx, GroundHeight(cx, cz) + up, cz);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            for (var e = Settle(400); e.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "biome-border");
            GameStatus.Lines["~biomeborders"] = $"biomeborders: {map.BiomeIds[pickA]} | {map.BiomeIds[pickB]} at cells {bx},{bz} ({cx:F0},{cz:F0}), eye {up:F0} m up, score {best:F1}";
        }
    }
}
