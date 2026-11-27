using System.Collections.Generic;
using Stabby.Viewer;
using Stabby.World;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator LightReceiversTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 55f;
            var lines = new List<string>();
            var world = ViewWorld();
            if (world == null) { GameStatus.Failure = "lightreceivers: no client view world"; yield break; }
            var em = world.EntityManager;
            var field = world.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field;
            for (int k = 0; k < 1800 && (field == null || ObjectViewSystem.Pending > 0); k++) { yield return null; field = world.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field; }
            if (field == null) { GameStatus.Failure = "lightreceivers: no object field"; yield break; }
            var map = world.GetExistingSystemManaged<ClientMapSystem>().Map;
            float span = WorldChunks.Size * map.Config.CellMeters, best = float.MaxValue;
            int n = field.ChunksAcross;
            var tree = Vector2.zero;
            var toward = Vector2.up;
            bool found = false;
            int cells = map.Config.Size;
            float cm = map.Config.CellMeters;
            var grassy = new HashSet<int>();
            foreach (var id in new[] { "temperate-forest", "steppe", "shrubland", "savanna" }) grassy.Add(System.Array.IndexOf(map.BiomeIds, id));
            for (int cz = 0; cz < n; cz++)
                for (int cx = 0; cx < n; cx++)
                {
                    if (Mathf.Abs((cx + 0.5f) * span - home.x) > span * 4f || Mathf.Abs((cz + 0.5f) * span - home.z) > span * 4f) continue;
                    foreach (var o in field.Chunk(cx, cz))
                    {
                        if (!field.Library.Objects[o.Def].Parts.Exists(p => p.Shape == ObjectPart.Broadleaf || p.Shape == ObjectPart.Conifer)) continue;
                        int gx = Mathf.Clamp((int)(o.X / cm), 0, cells - 1), gz = Mathf.Clamp((int)(o.Z / cm), 0, cells - 1);
                        float h0 = GroundHeight(o.X, o.Z), disk = grassy.Contains(map.Biome[gz * cells + gx]) ? 0f : 30f;
                        if (h0 < 2f) continue;
                        for (int a = 0; a < 8; a++)
                            for (float r = 3f; r <= 9f; r += 3f)
                            {
                                float hd = GroundHeight(o.X + Mathf.Sin(a * 0.785398f) * r, o.Z + Mathf.Cos(a * 0.785398f) * r);
                                disk += Mathf.Abs(hd - h0) + (hd < 1.5f ? 100f : 0f);
                            }
                        for (int a = 0; a < 8; a++)
                        {
                            var d = new Vector2(Mathf.Sin(a * 0.785398f), Mathf.Cos(a * 0.785398f));
                            float rough = 0f;
                            for (float r = 2f; r <= 10f; r += 2f) rough += Mathf.Abs(GroundHeight(o.X - d.x * r, o.Z - d.y * r) - h0);
                            float score = disk + 2f * rough - 0.5f * o.Size + 0.004f * new Vector2(o.X - home.x, o.Z - home.z).magnitude;
                            if (score < best) { best = score; tree = new Vector2(o.X, o.Z); toward = d; found = true; }
                        }
                    }
                }
            if (!found) { GameStatus.Failure = "lightreceivers: no tree near home"; yield break; }
            var side = new Vector2(toward.y, -toward.x);
            var eyeXZ = tree - toward * 8f + side * 1.2f;
            var eye = new Vector3(eyeXZ.x, GroundHeight(eyeXZ.x, eyeXZ.y) + 1.5f, eyeXZ.y);
            var lampXZ = tree - toward * 2.8f - side * 0.8f;
            var lamp = new float3(lampXZ.x, GroundHeight(lampXZ.x, lampXZ.y) + 0.4f, lampXZ.y);
            var aim = new Vector3(tree.x - toward.x * 3f, GroundHeight(tree.x, tree.y) + 1.1f, tree.y - toward.y * 2.5f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
            for (var w = ShowHour(NightHour, 0f); w.MoveNext();) yield return null;
            for (var w = Settle(360); w.MoveNext();) yield return null;
            var stateQuery = em.CreateEntityQuery(typeof(LightBudgetState));
            for (int k = 0; k < 600 && stateQuery.IsEmpty; k++) yield return null;
            if (stateQuery.IsEmpty) { GameStatus.Failure = "lightreceivers: LightPresenterSystem never created the budget"; yield break; }
            var presenter = world.GetExistingSystemManaged<LightPresenterSystem>();
            LightKind fire = null;
            foreach (var l in ContentFiles.Objects(ContentFiles.Biomes()).Lights) if (l.Id == "fire") fire = l;
            if (fire == null) { GameStatus.Failure = "lightreceivers: light kind fire missing from content"; yield break; }
            var emitter = em.CreateEntity();
            em.AddComponentData(emitter, LightEmitter.Of(fire, lamp, new float3(1f, 0.48f, 0.18f), 12f, 9f));
            var lib = ContentFiles.Creatures(ContentFiles.Biomes());
            FreezeCreatures(true);
            CreatureView.Current = new CreatureView { Override = 1, Move = 0, Phase = 0 };
            int baseCount = CreatureBuildSystem.Count, body = lib.Bodies.FindIndex(b => b.Id == "boar");
            var boarAt = (float2)(lampXZ + side * 1.9f - toward * 0.8f);
            var boar = em.CreateEntity(typeof(CreatureState));
            uint hb = math.hash(new uint2(560u, 0x5BEC1u));
            em.SetComponentData(boar, new CreatureState
            {
                Id = -910000, Body = math.max(0, body), BodySeed = ((ulong)hb << 32) | math.hash(new uint2(hb, 5u)), Seed = hb, WalkSpeed = 1f, Age = CreatureBody.AdultAge,
                Position = boarAt, Heading = math.atan2(-side.x, -side.y) + 0.4f,
            });
            for (int f = 0; f < 1800 && CreatureBuildSystem.Count < baseCount + 1; f++) yield return null;
            for (var w = Settle(150); w.MoveNext();) yield return null;
            var s = DayNightSystem.Shown;
            lines.Add($"new moon night hour {s.Hour:F2}, phase {s.Phase:F2}: {(s.Moon ? "moon" : "sun")} light {s.Intensity:F4}, ambient sky {s.AmbientSky.B:F4}; fire at {lamp.x:F0},{lamp.y:F1},{lamp.z:F0}, tree {tree.x:F0},{tree.y:F0} score {best:F1}, boar {(body < 0 ? "missing" : "built " + (CreatureBuildSystem.Count - baseCount))}");
            for (var w = SetLights(stateQuery, presenter, true, true); w.MoveNext();) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, "lightreceivers-dark");
            for (var w = SetLights(stateQuery, presenter, false, true); w.MoveNext();) yield return null;
            lines.Add("lit: " + TierLine(stateQuery, presenter));
            Shot(cam, null, "warmup");
            Shot(cam, dir, "lightreceivers-lit");
            GameStatus.Lines["~lightreceivers"] = string.Join("\n", lines);
            em.DestroyEntity(boar);
            CreatureView.Current = default;
            FreezeCreatures(false);
        }
    }
}
