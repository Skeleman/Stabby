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
        const float WaterLightsHour = 22f, WaterLightsInland = 1f, WaterLightsOut = 9f, WaterLightsSide = 2.5f, WaterLightsEye = 2.2f, WaterLightsClear = 2f;

        struct WaterLightsSpot { public Lake Lake; public int Cell; public Vector2 ToLand; public float Score; }

        static System.Collections.IEnumerator WaterLightsTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 55f;
            var lines = new List<string>();
            var world = ViewWorld();
            if (world == null) { GameStatus.Failure = "waterlights: no client view world"; yield break; }
            var em = world.EntityManager;
            var map = world.GetExistingSystemManaged<ClientMapSystem>().Map;
            var columns = world.GetExistingSystemManaged<ObjectColumnsSystem>();
            for (int k = 0; k < 1800 && columns?.Field == null; k++) yield return null;
            var field = columns?.Field;
            if (field == null) { GameStatus.Failure = "waterlights: no object field"; yield break; }
            int n = map.Config.Size;
            float cm = map.Config.CellMeters, span = field.ChunkMeters;
            Vector2 Cell(int i) => new Vector2(i % n, i / n) * cm;
            var spots = new List<WaterLightsSpot>();
            var steps = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            foreach (var lk in map.Water.Lakes)
            {
                if (lk.Liquid != 0 || lk.Cells.Count < 60) continue;
                foreach (int c in lk.Cells)
                {
                    int x = c % n, z = c / n;
                    if (x < 12 || z < 12 || x >= n - 12 || z >= n - 12) continue;
                    foreach (var o in steps)
                    {
                        int d = o.x + o.y * n, l = c + d;
                        if (LakeAt(map, l) || map.Sea[l] || !LakeAt(map, c - d) || !LakeAt(map, c - 2 * d)) continue;
                        var p = Cell(l);
                        float rise = GroundHeight(p.x, p.y) - lk.Level;
                        if (rise < 0.2f || rise > 1.5f) continue;
                        spots.Add(new WaterLightsSpot { Lake = lk, Cell = c, ToLand = new Vector2(o.x, o.y), Score = (p - new Vector2(home.x, home.z)).magnitude + 200f * Mathf.Abs(rise - 0.5f) });
                    }
                }
            }
            spots.Sort((a, b) => a.Score.CompareTo(b.Score));
            bool Clear(Vector2 from, Vector2 to)
            {
                var lo = Vector2.Min(from, to) - Vector2.one * WaterLightsClear;
                var hi = Vector2.Max(from, to) + Vector2.one * WaterLightsClear;
                var seg = to - from;
                float len2 = Mathf.Max(seg.sqrMagnitude, 1e-4f);
                for (int cz = Mathf.Max(0, (int)(lo.y / span)); cz <= Mathf.Min(field.ChunksAcross - 1, (int)(hi.y / span)); cz++)
                    for (int cx = Mathf.Max(0, (int)(lo.x / span)); cx <= Mathf.Min(field.ChunksAcross - 1, (int)(hi.x / span)); cx++)
                        foreach (var o in field.Chunk(cx, cz))
                        {
                            var q = new Vector2(o.X, o.Z);
                            float u = Mathf.Clamp01(Vector2.Dot(q - from, seg) / len2);
                            if ((from + seg * u - q).magnitude < WaterLightsClear + 0.5f * o.Size) return false;
                        }
                return true;
            }
            WaterLightsSpot spot = default;
            int tried = 0;
            foreach (var s in spots)
            {
                if (++tried > 4000) break;
                var edge = Cell(s.Cell) + s.ToLand * (cm * 0.5f);
                var side = new Vector2(s.ToLand.y, -s.ToLand.x);
                var at = edge + s.ToLand * WaterLightsInland;
                float dry = GroundHeight(at.x, at.y) - s.Lake.Level;
                if (dry < 0.3f || dry > 1.5f) continue;
                if (Clear(edge + s.ToLand * (WaterLightsInland + 1f), edge - s.ToLand * WaterLightsOut + side * WaterLightsSide)) { spot = s; break; }
            }
            if (spot.Lake == null) { GameStatus.Failure = $"waterlights: no water lake shore with a clear view ({spots.Count} shores, {tried} tried)"; yield break; }
            var lake = spot.Lake;
            var shore = Cell(spot.Cell) + spot.ToLand * (cm * 0.5f);
            var fireXZ = shore + spot.ToLand * WaterLightsInland;
            var lamp = new float3(fireXZ.x, GroundHeight(fireXZ.x, fireXZ.y) + 0.4f, fireXZ.y);
            var across = new Vector2(spot.ToLand.y, -spot.ToLand.x);
            var eyeXZ = shore - spot.ToLand * WaterLightsOut + across * WaterLightsSide;
            var eye = new Vector3(eyeXZ.x, lake.Level + WaterLightsEye, eyeXZ.y);
            var aimXZ = shore - spot.ToLand * 1.5f;
            var aim = new Vector3(aimXZ.x, lake.Level + 0.3f, aimXZ.y);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
            for (var w = ShowHour(WaterLightsHour, 0f); w.MoveNext();) yield return null;
            for (var w = Settle(360); w.MoveNext();) yield return null;
            for (int k = 0; k < 1800 && ObjectViewSystem.Pending > 0; k++) yield return null;
            var stateQuery = em.CreateEntityQuery(typeof(LightBudgetState));
            for (int k = 0; k < 600 && stateQuery.IsEmpty; k++) yield return null;
            if (stateQuery.IsEmpty) { GameStatus.Failure = "waterlights: LightPresenterSystem never created the budget"; yield break; }
            var presenter = world.GetExistingSystemManaged<LightPresenterSystem>();
            LightKind fire = null;
            foreach (var l in ContentFiles.Objects(ContentFiles.Biomes()).Lights) if (l.Id == "fire") fire = l;
            if (fire == null) { GameStatus.Failure = "waterlights: light kind fire missing from content"; yield break; }
            var emitter = em.CreateEntity();
            em.AddComponentData(emitter, LightEmitter.Of(fire, lamp, new float3(1f, 0.48f, 0.18f), 12f, 9f));
            lines.Add($"lake {lake.Id} ({lake.Cells.Count} cells, level {lake.Level:F1}), fire {lamp.x:F0},{lamp.y:F1},{lamp.z:F0} ({lamp.y - 0.4f - lake.Level:F2} m above water), eye {eye.x:F0},{eye.y:F1},{eye.z:F0}; clear shore {tried} of {spots.Count}");
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
