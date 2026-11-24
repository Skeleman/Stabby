using System.Collections.Generic;
using Stabby.Viewer;
using Stabby.World;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float WaterLightsHour = 22f;

        static System.Collections.IEnumerator WaterLightsTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 55f;
            var lines = new List<string>();
            var world = ViewWorld();
            if (world == null) { GameStatus.Failure = "waterlights: no client view world"; yield break; }
            var em = world.EntityManager;
            var map = world.GetExistingSystemManaged<ClientMapSystem>().Map;
            int n = map.Config.Size;
            float cm = map.Config.CellMeters;
            Vector2 Cell(int i) => new Vector2(i % n, i / n) * cm;
            float best = float.MaxValue;
            Lake lake = null;
            int shoreCell = -1;
            var toLand = Vector2.zero;
            var steps = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            foreach (var lk in map.Water.Lakes)
            {
                if (lk.Liquid != 0 || lk.Cells.Count < 60) continue;
                foreach (int c in lk.Cells)
                {
                    int x = c % n, z = c / n;
                    if (x < 3 || z < 3 || x >= n - 3 || z >= n - 3) continue;
                    foreach (var o in steps)
                    {
                        int d = o.x + o.y * n, l = c + d;
                        if (LakeAt(map, l) || map.Sea[l] || !LakeAt(map, c - d) || !LakeAt(map, c - 2 * d)) continue;
                        var p = Cell(l);
                        float rise = GroundHeight(p.x, p.y) - lk.Level;
                        if (rise < 0.2f || rise > 2.5f) continue;
                        float score = (p - new Vector2(home.x, home.z)).magnitude + 200f * Mathf.Abs(rise - 0.8f);
                        if (score < best) { best = score; lake = lk; shoreCell = c; toLand = new Vector2(o.x, o.y); }
                    }
                }
            }
            if (lake == null) { GameStatus.Failure = "waterlights: no water lake with a low shore"; yield break; }
            var edge = Cell(shoreCell) + toLand * (cm * 0.5f);
            var fireXZ = edge + toLand * 1.5f;
            var lamp = new float3(fireXZ.x, GroundHeight(fireXZ.x, fireXZ.y) + 0.4f, fireXZ.y);
            var side = new Vector2(toLand.y, -toLand.x);
            var eyeXZ = edge - toLand * 9f + side * 2.5f;
            var eye = new Vector3(eyeXZ.x, lake.Level + 1.6f, eyeXZ.y);
            var aimXZ = edge - toLand * 1.5f;
            var aim = new Vector3(aimXZ.x, lake.Level + 0.3f, aimXZ.y);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
            for (var w = ShowHour(WaterLightsHour, 0f); w.MoveNext();) yield return null;
            for (var w = Settle(360); w.MoveNext();) yield return null;
            var stateQuery = em.CreateEntityQuery(typeof(LightBudgetState));
            for (int k = 0; k < 600 && stateQuery.IsEmpty; k++) yield return null;
            if (stateQuery.IsEmpty) { GameStatus.Failure = "waterlights: LightPresenterSystem never created the budget"; yield break; }
            var presenter = world.GetExistingSystemManaged<LightPresenterSystem>();
            LightKind fire = null;
            foreach (var l in ContentFiles.Objects(ContentFiles.Biomes()).Lights) if (l.Id == "fire") fire = l;
            if (fire == null) { GameStatus.Failure = "waterlights: light kind fire missing from content"; yield break; }
            var emitter = em.CreateEntity();
            em.AddComponentData(emitter, LightEmitter.Of(fire, lamp, new float3(1f, 0.48f, 0.18f), 12f, 9f));
            lines.Add($"lake {lake.Id} ({lake.Cells.Count} cells, level {lake.Level:F1}), fire {lamp.x:F0},{lamp.y:F1},{lamp.z:F0}, eye {eye.x:F0},{eye.y:F1},{eye.z:F0}");
            for (var w = SetLights(stateQuery, presenter, true, true); w.MoveNext();) yield return null;
            for (var w = Settle(60); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "waterlights-moon0-unlit");
            for (var w = SetLights(stateQuery, presenter, false, true); w.MoveNext();) yield return null;
            foreach (var (phase, tag) in new[] { (0f, "moon0"), (SkyDef.FullMoon, "moon1") })
            {
                for (var w = ShowHour(WaterLightsHour, phase); w.MoveNext();) yield return null;
                for (var w = Settle(120); w.MoveNext();) yield return null;
                var s = DayNightSystem.Shown;
                lines.Add($"{tag}: hour {s.Hour:F2}, phase {s.Phase:F2}, {(s.Moon ? "moon" : "sun")} light {s.Intensity:F4}, ambient sky {s.AmbientSky.B:F4}; " + TierLine(stateQuery, presenter));
                Shot(cam, null, "warmup");
                Shot(cam, dir, "waterlights-" + tag);
            }
            GameStatus.Lines["~waterlights"] = string.Join("\n", lines);
            em.DestroyEntity(emitter);
        }
    }
}
