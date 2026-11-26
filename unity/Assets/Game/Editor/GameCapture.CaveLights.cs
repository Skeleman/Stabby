using System.Collections.Generic;
using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static readonly float[] CaveLightEyeDistances = { 5f, 7f, 3.5f };
        static readonly float[] FungusEyeDistances = { 2.5f, 3.5f, 5f };

        static bool OpenAt(Ground ground, Vector3 p) => Walk.Solid(ground, ground.EditsNear(p.x, p.z, Walk.Radius), ground.WalkStep, p.x, p.y, p.z) < 0;

        static bool CaveLightEye(Ground ground, Vector3 target, float lift, out Vector3 eye, float[] distances = null)
        {
            foreach (float d in distances ?? CaveLightEyeDistances)
                for (int a = 0; a < 16; a++)
                {
                    float t = a * (2f * Mathf.PI / 16f);
                    eye = target + new Vector3(Mathf.Cos(t) * d, lift, Mathf.Sin(t) * d);
                    bool clear = true;
                    for (int k = 0; k <= 6 && clear; k++) clear = OpenAt(ground, Vector3.Lerp(eye, target + Vector3.up * 0.4f, k / 8f));
                    if (clear) return true;
                }
            eye = target + Vector3.up * lift;
            return false;
        }

        static bool ShaftEye(Ground ground, Vector3 floor, Vector3 top, out Vector3 eye)
        {
            foreach (float d in new[] { 1.5f, 2.5f, 3.5f, 0.8f })
                for (int a = 0; a < 16; a++)
                {
                    float t = a * (2f * Mathf.PI / 16f);
                    eye = floor + new Vector3(Mathf.Cos(t) * d, 1.6f, Mathf.Sin(t) * d);
                    bool clear = true;
                    for (int k = 0; k <= 24 && clear; k++) clear = OpenAt(ground, Vector3.Lerp(eye, top, k / 24f));
                    if (clear) return true;
                }
            eye = floor + Vector3.up * 1.6f;
            return false;
        }

        static System.Collections.IEnumerator CaveLightsTour(Camera cam, string dir)
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
            if (sys == null) { GameStatus.Lines["~cavelights"] = GameStatus.Failure = "cavelights: no map"; yield break; }
            var caves = ContentFiles.Caves();
            var full = System.Threading.Tasks.Task.Run(() =>
            {
                var g = WorldGenerator.Generate(sys.Map.Config, sys.Defs, sys.Materials);
                g.Objects = sys.Objects;
                g.Caves = caves;
                MapGen.RaiseMound(g);
                var detail = new TerrainDetail(g);
                var placed = Features.Place(g, detail, out _);
                var field = new ObjectField(g, detail, placed, g.Objects);
                var found = new List<(int kind, int tile, ObjectInstance o)>();
                for (int i = 0; i < field.CaveLightCount; i++)
                    if (field.CaveLightAt(i, out var o)) found.Add((field.CaveLight(i).Kind, field.CaveLight(i).Tile, o));
                int tiles = placed.Where(e => FeatureEdit.IsRoom(e.Shape) || e.Shape == FeatureEdit.Shaft).Select(e => Features.TileOf(e, g.Config.CellMeters)).Where(t => t >= 0).Distinct().Count();
                var shafts = placed.Where(e => e.Shape == FeatureEdit.Shaft).Select(e => new Vector4(e.Ax, e.Az, e.By - e.R, e.Ay)).ToList();
                return (field.CaveLightCount, tiles, found, shafts);
            });
            while (!full.IsCompleted) yield return null;
            if (full.IsFaulted) { GameStatus.Lines["~cavelights"] = GameStatus.Failure = "cavelights: " + full.Exception; yield break; }
            var (planned, caveTiles, lights, shafts) = full.Result;
            var lines = new List<string>
            {
                $"cavelights: GV {WorldConfig.GeneratorVersion}, {planned} lights planned, {lights.Count} resolved, {lights.Select(l => l.tile).Distinct().Count()} of {caveTiles} cave tiles lit; fungus {lights.Count(l => l.kind == ObjectField.CaveFungus)}, crystal {lights.Count(l => l.kind == ObjectField.CaveCrystalLight)}, skylight {lights.Count(l => l.kind == ObjectField.CaveSkylight)}"
            };
            var tilesByKinds = lights.GroupBy(l => l.tile).OrderByDescending(g => g.Select(l => l.kind).Distinct().Count()).ThenBy(g => Vector2.Distance(new Vector2(g.First().o.X, g.First().o.Z), new Vector2(home.x, home.z)));
            var best = tilesByKinds.FirstOrDefault()?.Key ?? -1;
            cam.fieldOfView = 60f;
            foreach (var (kind, name) in new[] { (ObjectField.CaveFungus, "fungus"), (ObjectField.CaveSkylight, "skylight"), (ObjectField.CaveCrystalLight, "crystal") })
            {
                var pick = lights.Where(l => l.kind == kind).OrderBy(l => l.tile == best ? 0 : 1).ThenBy(l => Vector2.Distance(new Vector2(l.o.X, l.o.Z), new Vector2(home.x, home.z))).ToList();
                if (pick.Count == 0) { lines.Add($"{name}: none in this world"); continue; }
                var o = pick[0].o;
                var at = new Vector3(o.X, o.Y, o.Z);
                bool sky = kind == ObjectField.CaveSkylight;
                bool clear;
                Vector3 eye, look = at;
                if (sky && shafts.Count > 0)
                {
                    var sh = shafts.OrderBy(v => Vector2.Distance(new Vector2(v.x, v.y), new Vector2(o.X, o.Z))).First();
                    look = new Vector3(sh.x, sh.z, sh.y);
                    clear = ShaftEye(ground, new Vector3(sh.x, o.Y, sh.y), look, out eye);
                }
                else if (kind == ObjectField.CaveFungus) clear = CaveLightEye(ground, at, 1.8f, out eye, FungusEyeDistances);
                else clear = CaveLightEye(ground, at, 1.4f, out eye);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                var hours = sky ? new[] { (PinnedMidday, "noon"), (NightHour, "night") } : new[] { (PinnedMidday, "noon") };
                foreach (var (hour, when) in hours)
                {
                    for (var w = ShowHour(hour); w.MoveNext();) yield return null;
                    for (var w = Settle(120); w.MoveNext();) yield return null;
                    var gs = DayNightSystem.Shown;
                    string shot = "cavelights-" + name + (sky ? "-" + when : "");
                    lines.Add($"{shot}: tile {pick[0].tile}{(pick[0].tile == best ? " (shared tile)" : "")}, light {o.X:F0},{o.Y:F1},{o.Z:F0}, eye {eye.x:F0},{eye.y:F1},{eye.z:F0}, look {look.x:F0},{look.y:F1},{look.z:F0}{(clear ? "" : " (no clear eye)")}, pinned hour {gs.Hour:F2}, moon phase {gs.Phase:F2}, sky intensity {gs.Intensity:F2}");
                    Shot(cam, null, "warmup");
                    Shot(cam, dir, shot);
                }
                if (!sky) continue;
                bool open = CaveLightEye(ground, at, 2.2f, out var pool);
                cam.transform.SetPositionAndRotation(pool, Quaternion.LookRotation(at - pool));
                for (var w = ShowHour(PinnedMidday); w.MoveNext();) yield return null;
                for (var w = Settle(120); w.MoveNext();) yield return null;
                var ps = DayNightSystem.Shown;
                lines.Add($"cavelights-skylight-pool: light {o.X:F0},{o.Y:F1},{o.Z:F0}, eye {pool.x:F0},{pool.y:F1},{pool.z:F0}{(open ? "" : " (no clear eye)")}, pinned hour {ps.Hour:F2}, moon phase {ps.Phase:F2}, sky intensity {ps.Intensity:F2}");
                Shot(cam, null, "warmup");
                Shot(cam, dir, "cavelights-skylight-pool");
            }
            GameStatus.Lines["~cavelights"] = string.Join("\n", lines);
        }
    }
}
