using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using Stabby.World;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float PlaytestHour = 15f;
        const int PlaytestRing = 5;
        static readonly string[] TreeBiomes = { "temperate-forest", "boreal", "temperate-rain", "tropical-forest" };

        static System.Collections.IEnumerator PlaytestTour(Camera cam, string dir)
        {
            var server = ServerWorld();
            var view = ViewWorld();
            var w = view?.GetExistingSystemManaged<ClientMapSystem>().Map;
            Entity conn = Entity.Null;
            int id = -1;
            if (view != null)
            {
                using var nq = view.EntityManager.CreateEntityQuery(typeof(NetworkId));
                if (nq.CalculateEntityCount() == 1) { conn = nq.GetSingletonEntity(); id = nq.GetSingleton<NetworkId>().Value; }
            }
            if (server == null || w == null || id < 0) { GameStatus.Lines["~playtest"] = GameStatus.Failure = "playtest: no worlds, map or network id"; yield break; }
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            float2 home2 = new float2(home.x, home.z), spot = default, river = default;
            float best = float.MaxValue;
            int rivers = 0;
            for (int j = PlaytestRing + 1; j < n - PlaytestRing - 1; j++)
                for (int i = PlaytestRing + 1; i < n - PlaytestRing - 1; i++)
                {
                    int c = j * n + i;
                    if (!w.Water.IsRiver[c] || w.Water.LakeId[c] >= 0) continue;
                    rivers++;
                    var at = new float2(i, j) * cm;
                    float d = math.distance(at, home2);
                    if (d >= best) continue;
                    for (int r = 2; r <= PlaytestRing && d < best; r++)
                        for (int dj = -r; dj <= r && d < best; dj++)
                            for (int di = -r; di <= r && d < best; di++)
                            {
                                if (math.max(math.abs(di), math.abs(dj)) != r || !DryCell(w, i + di, j + dj) || !TreeBiome(w, (j + dj) * n + i + di)) continue;
                                best = d;
                                river = at;
                                spot = new float2(i + di, j + dj) * cm;
                            }
                }
            if (best == float.MaxValue) { GameStatus.Lines["~playtest"] = GameStatus.Failure = $"playtest: no dry spot by {rivers} river cells"; yield break; }
            using (var ecb = new EntityCommandBuffer(Allocator.Temp))
            {
                Rpc.Send(ecb, conn, new WarpRpc { At = spot, Face = river });
                ecb.Playback(view.EntityManager);
            }
            PlayerControls.Scripted = true;
            PlayerControls.ScriptMove = PlayerControls.ScriptLook = float2.zero;
            PlayerControls.Yaw = math.degrees(math.atan2(river.x - spot.x, river.y - spot.y));
            PlayerControls.Pitch = 12f;
            cam.fieldOfView = 60f;
            GameStatus.HoldCamera = false;
            for (var s = ShowHour(PlaytestHour); s.MoveNext();) yield return null;
            for (var s = Settle(240); s.MoveNext();) yield return null;
            OwnPlayer(server, id, out var pos, out _);
            GameStatus.Lines["~playtest"] = $"playtest: warp to {spot.x:F0},{spot.y:F0} landed {pos.x:F1},{pos.y:F1},{pos.z:F1}, river {river.x:F0},{river.y:F0} in {w.BiomeIds[w.Biome[CaveBrooks.CellOf(w, spot.x, spot.y)]]}, boom {PlayerControls.BoomNow:F2} m";
            Shot(cam, null, "warmup");
            Shot(cam, dir, "playtest-river");
        }

        static bool TreeBiome(GeneratedWorld w, int c) => w.Biome[c] >= 0 && System.Array.IndexOf(TreeBiomes, w.BiomeIds[w.Biome[c]]) >= 0;

        static bool DryCell(GeneratedWorld w, int i, int j)
        {
            int n = w.Config.Size;
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int c = (j + dj) * n + i + di;
                    if (w.Sea[c] || w.Water.LakeId[c] >= 0 || w.Water.IsRiver[c]) return false;
                }
            return true;
        }
    }
}
