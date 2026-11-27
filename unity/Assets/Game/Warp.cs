#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine.InputSystem;

namespace Stabby.Game
{
    public struct WarpRpc : IRpcCommand
    {
        public float2 At, Face;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class WarpSystem : SystemBase
    {
        const float CaveStandoffM = 7f, FeatureStandoffM = 14f, BiomeStandoffM = 0f, TownStandoffM = 6f, FeatureMergeM = 96f;
        const int Rings = 4, Angles = 16;
        static readonly string[] Kinds = { "caves", "features", "biomes", "towns" };

        struct Target
        {
            public string Name;
            public float X, Z, Y, StandoffM, Hx, Hz;
        }

        Task<List<Target>[]> lists;
        readonly int[] at = { -1, -1, -1, -1 };

        protected override void OnCreate() => Enabled = World.Name == GameSettings.ViewClient;

        protected override void OnUpdate()
        {
            var map = World.GetExistingSystemManaged<ClientMapSystem>();
            var field = World.GetExistingSystemManaged<ObjectColumnsSystem>().Field;
            var w = map.Map;
            var parts = map.Parts;
            if (lists == null && w != null && parts != null && field != null)
                lists = Task.Run(() => new[] { Build(0, w, parts, field), Build(1, w, parts, field), Build(2, w, parts, field), Build(3, w, parts, field) });
            var kb = Keyboard.current;
            if (kb == null) return;
            int key = kb.f3Key.wasPressedThisFrame ? 0 : kb.f4Key.wasPressedThisFrame ? 1 : kb.f5Key.wasPressedThisFrame ? 2 : kb.f6Key.wasPressedThisFrame ? 3 : -1;
            if (key < 0) return;
            if (lists == null || !lists.IsCompletedSuccessfully || !SystemAPI.TryGetSingletonEntity<NetworkId>(out var conn))
            {
                WarpHud.Show(lists != null && lists.IsFaulted ? "warp: index failed" : "warp: world not ready");
                return;
            }
            var list = lists.Result[key];
            if (list.Count == 0)
            {
                WarpHud.Show($"warp: no {Kinds[key]}");
                return;
            }
            bool back = kb.shiftKey.isPressed;
            int n = list.Count;
            at[key] = at[key] < 0 ? (back ? n - 1 : 0) : (at[key] + (back ? n - 1 : 1)) % n;
            var tg = list[at[key]];
            if (!Landing(w, tg, out var spot))
            {
                WarpHud.Show($"warp: no dry ground by {tg.Name}");
                return;
            }
            var face = new float2(tg.X, tg.Z);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            Rpc.Send(ecb, conn, new WarpRpc { At = spot, Face = face });
            ecb.Playback(EntityManager);
            ecb.Dispose();
            if (math.distancesq(face, spot) > 1e-4f) PlayerControls.Yaw = math.degrees(math.atan2(face.x - spot.x, face.y - spot.y));
            WarpHud.Show($"{tg.Name}  {at[key] + 1}/{n}");
        }

        static List<Target> Build(int key, GeneratedWorld w, GroundParts parts, ObjectField field)
        {
            var list = new List<Target>();
            if (key == 0)
            {
                foreach (var g in parts.Caves)
                    foreach (var node in g.Nodes)
                        if (node.Entrance && CaveBrooks.Open(w, node))
                            list.Add(new Target { Name = $"{g.Class} entrance", X = node.X, Z = node.Z, Y = node.Y, StandoffM = CaveStandoffM, Hx = node.Hx, Hz = node.Hz });
            }
            else if (key == 1)
            {
                var seen = new HashSet<long>();
                foreach (var e in parts.Edits)
                {
                    string name = FeatureName(e.Shape);
                    if (name == null) continue;
                    long cell = ((long)e.Shape << 40) | ((long)(int)(e.Ax / FeatureMergeM) << 20) | (long)(int)(e.Az / FeatureMergeM);
                    if (seen.Add(cell)) list.Add(new Target { Name = name, X = e.Ax, Z = e.Az, Y = float.NaN, StandoffM = FeatureStandoffM + e.R });
                }
            }
            else if (key == 2)
            {
                int size = w.Config.Size, kinds = w.BiomeIds.Length;
                float cm = w.Config.CellMeters;
                var sum = new double3[kinds];
                for (int i = 0; i < w.Biome.Length; i++)
                    if (Dry(w, i) && w.Biome[i] >= 0) sum[w.Biome[i]] += new double3(i % size, i / size, 1);
                var best = new double[kinds];
                var pick = new int[kinds];
                for (int b = 0; b < kinds; b++) { best[b] = double.MaxValue; pick[b] = -1; if (sum[b].z > 0) sum[b] /= sum[b].z; }
                for (int i = 0; i < w.Biome.Length; i++)
                {
                    int b = w.Biome[i];
                    if (b < 0 || !Dry(w, i)) continue;
                    double dx = i % size - sum[b].x, dy = i / size - sum[b].y, d = dx * dx + dy * dy;
                    if (d < best[b]) { best[b] = d; pick[b] = i; }
                }
                for (int b = 0; b < kinds; b++)
                    if (pick[b] >= 0) list.Add(new Target { Name = $"biome {w.BiomeIds[b]}", X = pick[b] % size * cm, Z = pick[b] / size * cm, Y = float.NaN, StandoffM = BiomeStandoffM });
            }
            else
                foreach (var p in field.TownPlans())
                    list.Add(new Target { Name = $"town {w.Objects.Towns[p.Town].Id}", X = p.X, Z = p.Z, Y = float.NaN, StandoffM = p.LaneM + TownStandoffM, Hx = p.Ux, Hz = p.Uz });
            return list;
        }

        static string FeatureName(int shape) => shape switch
        {
            FeatureEdit.Arch => "arch",
            FeatureEdit.Tor => "tor",
            FeatureEdit.Hoodoo => "hoodoo",
            FeatureEdit.Butte => "butte",
            FeatureEdit.Cenote => "cenote",
            FeatureEdit.Spring => "spring",
            FeatureEdit.Sink => "sinkhole",
            FeatureEdit.Bridge => "rock bridge",
            FeatureEdit.Shaft => "shaft",
            FeatureEdit.Vault => "vault",
            _ => null,
        };

        static bool Dry(GeneratedWorld w, int i) => !w.Sea[i] && w.Water.LakeId[i] < 0 && !w.Water.IsRiver[i];

        static bool DryAround(GeneratedWorld w, float x, float z)
        {
            int size = w.Config.Size;
            float cm = w.Config.CellMeters, top = (size - 1) * cm;
            if (x < cm || z < cm || x > top - cm || z > top - cm) return false;
            int c = CaveBrooks.CellOf(w, x, z), i = c % size, j = c / size;
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                    if (!Dry(w, (j + dj) * size + i + di)) return false;
            return true;
        }

        static bool Landing(GeneratedWorld w, Target t, out float2 spot)
        {
            spot = default;
            float best = float.MaxValue, start = math.lengthsq(new float2(t.Hx, t.Hz)) > 0 ? math.atan2(t.Hz, t.Hx) : 0;
            for (int r = 0; r < Rings; r++)
            {
                float radius = t.StandoffM + r * math.max(4f, 0.5f * t.StandoffM);
                for (int a = 0; a < Angles; a++)
                {
                    float ang = start + a * (2 * math.PI / Angles);
                    var p = new float2(t.X + math.cos(ang) * radius, t.Z + math.sin(ang) * radius);
                    if (!DryAround(w, p.x, p.y)) continue;
                    float cost = float.IsNaN(t.Y) ? a : math.abs(w.Height[CaveBrooks.CellOf(w, p.x, p.y)] - t.Y);
                    if (cost < best) { best = cost; spot = p; }
                }
                if (best < float.MaxValue) return true;
            }
            return false;
        }
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct ServerWarpSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<Ground>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ground = SystemAPI.GetSingleton<Ground>();
            var cols = SystemAPI.TryGetSingleton<ObjectColumns>(out var oc) ? oc : default;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (warp, rx, e) in SystemAPI.Query<RefRO<WarpRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                ecb.DestroyEntity(e);
                if (!SystemAPI.HasComponent<NetworkId>(rx.ValueRO.SourceConnection)) continue;
                int nid = SystemAPI.GetComponent<NetworkId>(rx.ValueRO.SourceConnection).Value;
                float top = ground.Top;
                float x = math.clamp(warp.ValueRO.At.x, 0, top), z = math.clamp(warp.ValueRO.At.y, 0, top);
                float h = ground.HeightAt(x, z);
                var edits = ground.EditsNear(x, z, Walk.Radius);
                float y = Walk.Surface(ground, edits, ground.WalkStep, x, z, h + Walk.Reach, h - Walk.Reach);
                Walk.Step(ground, edits, cols, ground.WalkStep, ref x, ref y, ref z, 0, 0, Walk.Reach);
                var look = new float3(warp.ValueRO.Face.x - x, 0, warp.ValueRO.Face.y - z);
                var rot = math.lengthsq(look) > 1e-4f ? quaternion.LookRotationSafe(look, math.up()) : quaternion.identity;
                foreach (var (owner, t, motion) in SystemAPI.Query<RefRO<GhostOwner>, RefRW<LocalTransform>, RefRW<PlayerMotion>>().WithAll<Player>())
                {
                    if (owner.ValueRO.NetworkId != nid) continue;
                    t.ValueRW = LocalTransform.FromPositionRotation(new float3(x, y, z), rot);
                    motion.ValueRW = default;
                }
            }
            ecb.Playback(state.EntityManager);
        }
    }
}
#endif
