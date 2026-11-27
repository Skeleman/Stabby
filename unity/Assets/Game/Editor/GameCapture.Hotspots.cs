using System;
using System.Globalization;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const string HotspotsAtVariable = "STABBY_HOTSPOTS_AT";
        const float HotspotsHour = 15.5f, HotspotsMoon = 0.5f, StratoAway = 2.2f, StratoUp = 0.55f, IslandAway = 2.6f, IslandUp = 0.45f;

        static System.Collections.IEnumerator HotspotsTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            Ground ground = default;
            bool found = false;
            foreach (var w in Unity.Entities.World.All)
            {
                if (w.Name != GameSettings.ViewClient) continue;
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (q.CalculateEntityCount() != 1) continue;
                ground = q.GetSingleton<Ground>();
                found = true;
                break;
            }
            if (!found) { GameStatus.Lines["~hotspots"] = GameStatus.Failure = "hotspots: no ground"; yield break; }
            var at = Environment.GetEnvironmentVariable(HotspotsAtVariable);
            if (string.IsNullOrEmpty(at)) { GameStatus.Lines["~hotspots"] = GameStatus.Failure = "hotspots: set " + HotspotsAtVariable + " to stratoX,stratoZ,stratoRo,stratoH,stratoBase,islandX,islandZ,islandR"; yield break; }
            var inv = CultureInfo.InvariantCulture;
            var p = Array.ConvertAll(at.Split(','), s => float.Parse(s, inv));
            cam.fieldOfView = 55f;
            for (var w = ShowHour(HotspotsHour, HotspotsMoon); w.MoveNext();) yield return null;
            float ro = p[2], h = p[3], baseY = p[4];
            var mid = new Vector3(p[0], baseY + 0.45f * h, p[1]);
            var eye = ClearEye(ground, mid, StratoAway * ro, baseY + StratoUp * h, out float stratoClear);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(mid - eye));
            for (var w = Settle(300); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            var sky = DayNightSystem.Shown;
            Shot(cam, dir, "hotspots-strato");
            float r = p[7];
            var isle = new Vector3(p[5], 0f, p[6]);
            isle.y = Mathf.Max(0f, ground.HeightAt(isle.x, isle.z)) * 0.5f;
            var eye2 = ClearEye(ground, isle, IslandAway * r, isle.y + IslandUp * r, out float islandClear);
            cam.transform.SetPositionAndRotation(eye2, Quaternion.LookRotation(isle - eye2));
            for (var w = Settle(300); w.MoveNext();) yield return null;
            Shot(cam, dir, "hotspots-island");
            GameStatus.Lines["~hotspots"] = string.Format(inv, "hotspots: strato {0:F0},{1:F0} Ro {2:F0} H {3:F0} eye {4:F0},{5:F0},{6:F0} clear {7:F1}; island {8:F0},{9:F0} r {10:F0} eye {11:F0},{12:F0},{13:F0} clear {14:F1}; GV {15}, pinned hour {16:F2} moon {17:F2}",
                p[0], p[1], ro, h, eye.x, eye.y, eye.z, stratoClear, isle.x, isle.z, r, eye2.x, eye2.y, eye2.z, islandClear, WorldConfig.GeneratorVersion, sky.Hour, sky.Phase);
        }
    }
}
