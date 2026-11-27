using Stabby.World;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float WakesHour = 10.5f, WakesMoon = 0.5f;

        static System.Collections.IEnumerator WakesTour(Camera cam, string dir)
        {
            var server = ServerWorld();
            var view = ViewWorld();
            var map = view?.GetExistingSystemManaged<ClientMapSystem>()?.Map;
            Ground ground = default;
            bool hasGround = false;
            if (view != null)
            {
                using var gq = view.EntityManager.CreateEntityQuery(typeof(Ground));
                if (gq.CalculateEntityCount() == 1) { ground = gq.GetSingleton<Ground>(); hasGround = true; }
            }
            if (server == null || map == null || !hasGround) { GameStatus.Lines["~wakes"] = GameStatus.Failure = "wakes: no worlds, map or ground"; yield break; }
            if (!ShallowSpot(map, ground, out var spot, out var along, out float level, out float depth)) { GameStatus.Lines["~wakes"] = GameStatus.Failure = "wakes: no shallow water found"; yield break; }
            var side = new float2(along.y, -along.x);
            if (ground.HeightAt(spot.x + side.x * 4f, spot.y + side.y * 4f) < ground.HeightAt(spot.x - side.x * 4f, spot.y - side.y * 4f)) side = -side;
            float yaw = math.degrees(math.atan2(-along.x, -along.y));

            cam.fieldOfView = 60f;
            for (var w = ShowHour(WakesHour, WakesMoon); w.MoveNext();) yield return null;
            PlayerControls.Scripted = true;
            PlayerControls.ScriptMove = PlayerControls.ScriptLook = float2.zero;
            PlayerControls.ScriptSprint = false;
            PlayerControls.Yaw = yaw;
            PlayerControls.Pitch = 28f;
            GameStatus.HoldCamera = false;
            PlaceServerPlayers(server, (Vector2)(spot + along * 7f));
            for (var w = Settle(180); w.MoveNext();) yield return null;
            PlayerControls.Yaw = yaw;
            PlayerControls.ScriptMove = new float2(0, 1);
            for (int k = 0; k < 75; k++) yield return null;
            int playerWakes = WakeCount(view);
            Shot(cam, null, "warmup");
            Shot(cam, dir, "wakes-player");
            PlayerControls.ScriptMove = float2.zero;

            GameStatus.HoldCamera = true;
            PlaceServerPlayers(server, (Vector2)(spot + along * 45f));
            var em = view.EntityManager;
            var lib = Stabby.Viewer.ContentFiles.Creatures(Stabby.Viewer.ContentFiles.Biomes());
            int body = math.max(0, lib.Bodies.FindIndex(b => b.Id == "deer"));
            var at = spot - along * 3f;
            float bed = ground.HeightAt(at.x, at.y);
            var eye = new Vector3(at.x + side.x * 7f - along.x * 4f, 0, at.y + side.y * 7f - along.y * 4f);
            eye.y = math.max(ground.HeightAt(eye.x, eye.z), level) + 2.4f;
            cam.fieldOfView = 45f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(new Vector3(at.x, level + 0.3f, at.y) - eye));
            FreezeCreatures(true);
            CreatureView.Current = new CreatureView { Override = 1, Move = 1, Phase = 0.3f };
            int baseCount = CreatureBuildSystem.Count;
            var deer = em.CreateEntity(typeof(CreatureState));
            uint h = math.hash(new uint2(870u, 0x3A4Eu));
            em.SetComponentData(deer, new CreatureState
            {
                Id = -1 - 900870, Body = body, BodySeed = ((ulong)h << 32) | math.hash(new uint2(h, 5u)), Seed = h, WalkSpeed = 1.6f, Age = CreatureBody.AdultAge,
                Position = at, Heading = math.atan2(along.x, along.y),
            });
            for (int f = 0; f < 1800 && CreatureBuildSystem.Count <= baseCount; f++) yield return null;
            for (var w = Settle(150); w.MoveNext();) yield return null;
            int creatureWakes = WakeCount(view);
            Shot(cam, null, "warmup");
            Shot(cam, dir, "wakes-creature");
            em.DestroyEntity(deer);
            CreatureView.Current = default;
            FreezeCreatures(false);
            PlayerControls.Scripted = false;

            var sky = DayNightSystem.Shown;
            GameStatus.Lines["~wakes"] = $"wakes: shallow water at {spot.x:F0},{spot.y:F0} depth {depth:F2} m (lake level {level:F1}), creature bed depth {level - bed:F2} m, emitters {playerWakes} with the player wading, {creatureWakes} with the deer, pinned hour {sky.Hour:F2} moon {sky.Phase:F2}";
        }

        static int WakeCount(Unity.Entities.World view) => view.GetExistingSystemManaged<WakePresenterSystem>()?.Count ?? -1;

        static bool ShallowSpot(GeneratedWorld map, Ground ground, out float2 at, out float2 along, out float level, out float depth)
        {
            at = along = default;
            level = depth = 0;
            int n = map.Config.Size;
            float cm = map.Config.CellMeters, best = float.MaxValue;
            var from = new float2(home.x, home.z);
            foreach (var lake in map.Water.Lakes)
            {
                if (lake.Liquid != 0 || lake.Cells.Count < 12) continue;
                foreach (int i in lake.Cells)
                {
                    var p = new float2(i % n, i / n) * cm;
                    float dist = math.distancesq(p, from);
                    if (dist >= best) continue;
                    float d = lake.Level - ground.HeightAt(p.x, p.y);
                    if (d < 0.45f || d > 0.85f) continue;
                    var grad = new float2(ground.HeightAt(p.x + 1f, p.y) - ground.HeightAt(p.x - 1f, p.y), ground.HeightAt(p.x, p.y + 1f) - ground.HeightAt(p.x, p.y - 1f));
                    var tan = math.lengthsq(grad) > 1e-6f ? math.normalize(new float2(-grad.y, grad.x)) : new float2(1, 0);
                    bool ok = true;
                    for (int k = -3; k <= 3 && ok; k++)
                    {
                        var q = p + tan * (3f * k);
                        float dq = lake.Level - ground.HeightAt(q.x, q.y);
                        ok = dq > 0.25f && dq < 1.3f;
                    }
                    if (!ok) continue;
                    best = dist;
                    at = p;
                    along = tan;
                    level = lake.Level;
                    depth = d;
                }
            }
            return best < float.MaxValue;
        }
    }
}
