using System.Collections.Generic;
using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator MinesTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            ClientMapSystem sys = null;
            Ground ground = default;
            ObjectColumns cols = default;
            foreach (var w in Unity.Entities.World.All)
            {
                var s = w.GetExistingSystemManaged<ClientMapSystem>();
                var q = w.EntityManager.CreateEntityQuery(typeof(Ground));
                if (s?.Map == null || q.CalculateEntityCount() != 1) continue;
                sys = s;
                ground = q.GetSingleton<Ground>();
                var oq = w.EntityManager.CreateEntityQuery(typeof(ObjectColumns));
                if (oq.CalculateEntityCount() == 1) cols = oq.GetSingleton<ObjectColumns>();
                break;
            }
            if (sys == null) { GameStatus.Lines["~mines"] = GameStatus.Failure = "mines: no map"; yield break; }
            var caves = ContentFiles.Caves();
            var full = System.Threading.Tasks.Task.Run(() =>
            {
                var g = WorldGenerator.Generate(sys.Map.Config, sys.Defs, sys.Materials);
                g.Objects = sys.Objects;
                g.Caves = caves;
                MapGen.RaiseMound(g);
                var placed = Features.Place(g, new TerrainDetail(g), out var gs);
                return (placed.ToArray(), gs.ToArray());
            });
            while (!full.IsCompleted) yield return null;
            if (full.IsFaulted) { GameStatus.Lines["~mines"] = GameStatus.Failure = "mines: " + full.Exception; yield break; }
            var (edits, graphs) = full.Result;
            var mine = Nearest(graphs, Mines.Class);
            if (mine == null) { GameStatus.Lines["~mines"] = GameStatus.Failure = "mines: no mine in this world"; yield break; }
            var lines = new List<string> { $"mines: {graphs.Count(Mines.IsMine)} mines, {edits.Count(e => e.Shape == FeatureEdit.Mine && Mines.Vertical(e))} shafts, GV {WorldConfig.GeneratorVersion}" };
            cam.fieldOfView = 60f;
            for (var w = ShowHour(CaveShotHour); w.MoveNext();) yield return null;
            var mouth = mine.Nodes[0];
            var end = mine.Nodes[1];
            var m = new Vector3(mouth.X, mouth.Y, mouth.Z);
            var din = new Vector3(mouth.Hx, 0, mouth.Hz).normalized;
            var eye = m - din * 12f;
            eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z) + 1.7f, m.y + 1.5f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(m + din * 4f + Vector3.up * 1.2f - eye));
            for (var w = Settle(120); w.MoveNext();) yield return null;
            var sky = DayNightSystem.Shown;
            lines.Add($"entrance {m.x:F0},{m.y:F0},{m.z:F0}, eye {eye.x:F0},{eye.y:F1},{eye.z:F0}, pinned hour {sky.Hour:F2}, moon phase {sky.Phase:F2}");
            Shot(cam, null, "warmup");
            Shot(cam, dir, "mines-entrance");
            var e1 = new Vector3(end.X, end.Y, end.Z);
            var at = e1 - din * 10f;
            float tx = at.x, ty = at.y, tz = at.z;
            Walk.Step(ground, ground.EditsNear(tx, tz, Walk.Radius), cols, ground.WalkStep, ref tx, ref ty, ref tz, 0, 0, Walk.Reach);
            eye = new Vector3(tx, ty + 1.7f, tz);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(din));
            for (var w = Settle(120); w.MoveNext();) yield return null;
            sky = DayNightSystem.Shown;
            lines.Add($"interior eye {eye.x:F0},{eye.y:F1},{eye.z:F0}, {Vector3.Distance(eye, m):F0} m in from the mouth, pinned hour {sky.Hour:F2}, moon phase {sky.Phase:F2}; debug shots are the same frame +{CaveDebugExposure:F0} EV and with cave darkness off, never the look");
            Shot(cam, null, "warmup");
            Shot(cam, dir, "mines-interior");
            ExposedShot(cam, dir, "mines-interior-debug-exposure", CaveDebugExposure);
            GeometryShot(cam, dir, "mines-interior-debug-unlit");
            GameStatus.Lines["~mines"] = string.Join("\n", lines);
        }
    }
}
