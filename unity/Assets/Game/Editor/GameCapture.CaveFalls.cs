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
            var look = Vector3.Lerp(lip, pool, 0.6f);
            bool clear = FallEye(ground, fall, lip, look, out var eye);
            cam.fieldOfView = 60f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            var torch = new GameObject("cavefalls-torch").AddComponent<Light>();
            torch.transform.position = eye + Vector3.up * 0.3f + (look - eye).normalized * 1.5f;
            torch.type = LightType.Point;
            torch.color = new Color(1f, 0.72f, 0.45f);
            torch.range = 14f;
            torch.intensity = 4f;
            for (var w = ShowHour(CaveShotHour); w.MoveNext();) yield return null;
            double waited = LoadWaitTotal();
            for (var w = Settle(120); w.MoveNext();) yield return null;
            var shown = DayNightSystem.Shown;
            string[] kinds = { "spring", "pool", "sink", "plunge" };
            var lines = new List<string>
            {
                $"cavefalls: GV {WorldConfig.GeneratorVersion}, {falls.Count} falls of {brooks.Length} brooks ({string.Join(", ", brooks.GroupBy(b => b.End).Select(g => kinds[g.Key] + " " + g.Count()))}); shot fall in {graphs[fall.Graph].Class} graph {fall.Graph}, drop {drop:F2} m, pool R {fall.PoolR:F1} m at {pool.x:F0},{pool.y:F1},{pool.z:F0}, nearest natural light {(lights.Count == 0 ? "none" : $"kind {light.kind} at {near:F1} m")}, eye {eye.x:F0},{eye.y:F1},{eye.z:F0}{(clear ? "" : " (no clear eye)")}, capture-only torch at the eye, pinned hour {shown.Hour:F2}, moon phase {shown.Phase:F2}: stream wait {LoadWaitTotal() - waited:F2} s"
            };
            Shot(cam, null, "warmup");
            Shot(cam, dir, "caves-fall");
            ExposedShot(cam, dir, "caves-fall-debug-exposure", CaveDebugExposure);
            GeometryShot(cam, dir, "caves-fall-debug-unlit");
            Object.Destroy(torch.gameObject);
            GameStatus.Lines["~cavefalls"] = string.Join("\n", lines);
        }

        static bool FallEye(Ground ground, CaveBrook b, Vector3 lip, Vector3 look, out Vector3 eye)
        {
            int st = CaveBrook.Stride;
            var tries = new List<Vector3>();
            for (int i = b.Fall - 2; i >= Mathf.Max(0, b.Fall - 10); i--)
                foreach (float lift in FallEyeLifts) tries.Add(new Vector3(b.Points[i * st], b.Points[i * st + 1] + lift, b.Points[i * st + 2]));
            foreach (float d in new[] { 6f, 8f, 5f, 10f, 4f, 12f })
                for (int a = 0; a < 16; a++)
                {
                    float t = a * (2f * Mathf.PI / 16f);
                    tries.Add(lip + new Vector3(Mathf.Cos(t) * d, 1.7f, Mathf.Sin(t) * d));
                }
            eye = lip + Vector3.up * 1.7f;
            int best = -1;
            foreach (var p in tries)
            {
                bool seen = true;
                for (int k = 0; k <= 6 && seen; k++) seen = OpenAt(ground, Vector3.Lerp(p, look, k / 8f));
                if (!seen) continue;
                int room = 0;
                for (int k = 0; k < 6; k++) if (OpenAt(ground, p + FallEyeRoom[k] * FallEyeClearM)) room++;
                if (room == 6) { eye = p; return true; }
                if (room > best) { best = room; eye = p; }
            }
            return false;
        }

        const float FallEyeClearM = 0.8f;
        static readonly float[] FallEyeLifts = { 1.2f, 0.9f, 1.6f };
        static readonly Vector3[] FallEyeRoom = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
    }
}
