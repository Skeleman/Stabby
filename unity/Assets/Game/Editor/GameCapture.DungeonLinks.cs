using System.Collections.Generic;
using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator DungeonLinksTour(Camera cam, string dir)
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
            if (sys == null) { GameStatus.Lines["~dungeonlinks"] = GameStatus.Failure = "dungeon links: no map"; yield break; }
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
            if (full.IsFaulted) { GameStatus.Lines["~dungeonlinks"] = GameStatus.Failure = "dungeon links: " + full.Exception; yield break; }
            var (edits, graphs) = full.Result;
            var link = Nearest(graphs, DungeonLinks.Class);
            if (link == null) { GameStatus.Lines["~dungeonlinks"] = GameStatus.Failure = "dungeon links: no link in this world"; yield break; }
            var lines = new List<string> { $"dungeon links: {graphs.Count(DungeonLinks.IsLink)} links, {edits.Count(e => e.Link)} link carves, GV {WorldConfig.GeneratorVersion}" };
            cam.fieldOfView = 60f;
            for (var w = ShowHour(CaveShotHour); w.MoveNext();) yield return null;
            var n0 = link.Nodes[0];
            var m = new Vector3(n0.X, n0.Y, n0.Z);
            var din = new Vector3(n0.Hx, 0, n0.Hz).normalized;
            var at = m - din * (DungeonLinks.AheadM + 3f);
            float tx = at.x, ty = m.y, tz = at.z;
            Walk.Step(ground, ground.EditsNear(tx, tz, Walk.Radius), cols, ground.WalkStep, ref tx, ref ty, ref tz, 0, 0, Walk.Reach);
            var eye = new Vector3(tx, ty + 1.6f, tz);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(m + din * 8f - Vector3.up * 0.5f - eye));
            for (var w = Settle(120); w.MoveNext();) yield return null;
            var sky = DayNightSystem.Shown;
            lines.Add($"breach {m.x:F0},{m.y:F0},{m.z:F0}, eye {eye.x:F0},{eye.y:F1},{eye.z:F0} inside the dungeon, pinned hour {sky.Hour:F2}, moon phase {sky.Phase:F2}; debug shots are the same frame +{CaveDebugExposure:F0} EV and with cave darkness off, never the look");
            Shot(cam, null, "warmup");
            Shot(cam, dir, "dungeonlinks-breach");
            ExposedShot(cam, dir, "dungeonlinks-breach-debug-exposure", CaveDebugExposure);
            GeometryShot(cam, dir, "dungeonlinks-breach-debug-unlit");
            GameStatus.Lines["~dungeonlinks"] = string.Join("\n", lines);
        }
    }
}
