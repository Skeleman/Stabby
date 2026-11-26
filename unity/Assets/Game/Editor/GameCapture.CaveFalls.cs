using System.Collections.Generic;
using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator CaveFallsTour(Camera cam, string dir)
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
            if (sys == null) { GameStatus.Lines["~cavefalls"] = GameStatus.Failure = "cavefalls: no map"; yield break; }
            var caves = ContentFiles.Caves();
            var full = System.Threading.Tasks.Task.Run(() =>
            {
                var g = WorldGenerator.Generate(sys.Map.Config, sys.Defs, sys.Materials);
                g.Objects = sys.Objects;
                g.Caves = caves;
                MapGen.RaiseMound(g);
                var detail = new TerrainDetail(g);
                var placed = Features.Place(g, detail, out var gs);
                var brooks = CaveBrooks.Build(g, detail, placed, gs);
                var field = new ObjectField(g, detail, placed, g.Objects);
                var lights = new List<(int kind, Vector3 at)>();
                for (int i = 0; i < field.CaveLightCount; i++)
                    if (field.CaveLightAt(i, out var o)) lights.Add((field.CaveLight(i).Kind, new Vector3(o.X, o.Y, o.Z)));
                return (brooks, gs, lights);
            });
            while (!full.IsCompleted) yield return null;
            if (full.IsFaulted) { GameStatus.Lines["~cavefalls"] = GameStatus.Failure = "cavefalls: " + full.Exception; yield break; }
            var (brooks, graphs, lights) = full.Result;
            var falls = brooks.Where(b => b.End == CaveBrook.Plunge).ToList();
            if (falls.Count == 0) { GameStatus.Lines["~cavefalls"] = GameStatus.Failure = $"cavefalls: no cave fall in this world, {brooks.Length} brooks, GV {WorldConfig.GeneratorVersion}"; yield break; }
            int st = CaveBrook.Stride;
            Vector3 Pool(CaveBrook b) => new Vector3(b.Points[b.Fall * st], b.Points[b.Fall * st + 1], b.Points[b.Fall * st + 2]);
            float Near(CaveBrook b) => lights.Count == 0 ? float.MaxValue : lights.Min(l => Vector3.Distance(l.at, Pool(b)));
            var fall = falls.OrderBy(Near).First();
            var pool = Pool(fall);
            int l0 = (fall.Fall - 1) * st;
            var lip = new Vector3(fall.Points[l0], fall.Points[l0 + 1], fall.Points[l0 + 2]);
            float drop = lip.y - pool.y, near = Near(fall);
            var light = lights.Count == 0 ? default : lights.OrderBy(l => Vector3.Distance(l.at, pool)).First();
            bool clear = CaveLightEye(ground, lip, 1.7f, out var eye, new[] { 6f, 8f, 5f, 10f, 4f });
            var look = Vector3.Lerp(lip, pool, 0.6f);
            cam.fieldOfView = 60f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            for (var w = ShowHour(CaveShotHour); w.MoveNext();) yield return null;
            double waited = LoadWaitTotal();
            for (var w = Settle(120); w.MoveNext();) yield return null;
            var shown = DayNightSystem.Shown;
            string[] kinds = { "spring", "pool", "sink", "plunge" };
            var lines = new List<string>
            {
                $"cavefalls: GV {WorldConfig.GeneratorVersion}, {falls.Count} falls of {brooks.Length} brooks ({string.Join(", ", brooks.GroupBy(b => b.End).Select(g => kinds[g.Key] + " " + g.Count()))}); shot fall in {graphs[fall.Graph].Class} graph {fall.Graph}, drop {drop:F2} m, pool R {fall.PoolR:F1} m at {pool.x:F0},{pool.y:F1},{pool.z:F0}, nearest natural light {(lights.Count == 0 ? "none" : $"kind {light.kind} at {near:F1} m")}, eye {eye.x:F0},{eye.y:F1},{eye.z:F0}{(clear ? "" : " (no clear eye)")}, pinned hour {shown.Hour:F2}, moon phase {shown.Phase:F2}: stream wait {LoadWaitTotal() - waited:F2} s"
            };
            Shot(cam, null, "warmup");
            Shot(cam, dir, "caves-fall");
            ExposedShot(cam, dir, "caves-fall-debug-exposure", CaveDebugExposure);
            GeometryShot(cam, dir, "caves-fall-debug-unlit");
            GameStatus.Lines["~cavefalls"] = string.Join("\n", lines);
        }
    }
}
