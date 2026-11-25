using System.Collections.Generic;
using System.Linq;
using Stabby.Viewer;
using Stabby.World;
using UnityEditor;
using UnityEngine;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        const float CaveShotHour = PinnedMidday, CaveDebugExposure = 3f;

        static Vector3 OpenGround(Ground ground, CaveGraph[] graphs, float dist, float span)
        {
            for (int a = 0; a < 64; a++)
            {
                float t = a * (2f * Mathf.PI / 64f);
                var p = home + new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t)) * dist;
                if (p.x < 500f || p.z < 500f || p.x > span - 500f || p.z > span - 500f) continue;
                float h = ground.HeightAt(p.x, p.z);
                if (h < 20f) continue;
                bool clear = true;
                foreach (var g in graphs)
                    foreach (var n in g.Nodes)
                        if (new Vector2(n.X - p.x, n.Z - p.z).magnitude < 800f) clear = false;
                if (clear) return new Vector3(p.x, h + 1.7f, p.z);
            }
            return home;
        }

        static System.Collections.IEnumerator SettleWhy(int frames, double[] why)
        {
            double last = EditorApplication.timeSinceStartup;
            for (int k = 0; k < frames || Loading(); k++)
            {
                yield return null;
                double now = EditorApplication.timeSinceStartup, dt = now - last;
                last = now;
                bool columns = true;
                foreach (var w in Unity.Entities.World.All)
                    if (w.Name == GameSettings.ViewClient) columns = w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Settled ?? false;
                var cam = Camera.main;
                if (GameStatus.TerrainBehind > 0) why[0] += dt;
                if (!columns) why[1] += dt;
                if (cam != null && !ObjectViewSystem.ReadyAround(cam.transform.position, ReadyWithin)) why[2] += dt;
            }
        }

        static void GeometryShot(Camera cam, string dir, string name)
        {
            Shader.SetGlobalFloat("_StabbyCaveDarkOff", 1f);
            Shot(cam, dir, name);
            Shader.SetGlobalFloat("_StabbyCaveDarkOff", 0f);
        }

        static void UndimmedShot(Camera cam, string dir, string name, float ev)
        {
            var min = Shader.GetGlobalVector("_StabbyCaveDarkMin");
            Shader.SetGlobalVector("_StabbyCaveDarkMin", new Vector4(min.x, min.y, min.z, 0));
            ExposedShot(cam, dir, name, ev);
            Shader.SetGlobalVector("_StabbyCaveDarkMin", min);
        }

        static void ExposedShot(Camera cam, string dir, string name, float ev)
        {
            GameStatus.ToolFrame = Time.frameCount;
            var rt = new RenderTexture(1600, 900, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            float gain = Mathf.Pow(2f, ev);
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                px[i] = new Color(Mathf.LinearToGammaSpace(Mathf.Min(1f, Mathf.GammaToLinearSpace(c.r) * gain)), Mathf.LinearToGammaSpace(Mathf.Min(1f, Mathf.GammaToLinearSpace(c.g) * gain)), Mathf.LinearToGammaSpace(Mathf.Min(1f, Mathf.GammaToLinearSpace(c.b) * gain)));
            }
            tex.SetPixels(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        static System.Collections.IEnumerator CavesTour(Camera cam, string dir)
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
            if (sys == null) { GameStatus.Lines["~caves"] = GameStatus.Failure = "caves: no map"; yield break; }
            var lines = new List<string>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var caves = ContentFiles.Caves();
            var full = System.Threading.Tasks.Task.Run(() =>
            {
                var g = WorldGenerator.Generate(sys.Map.Config, sys.Defs, sys.Materials);
                g.Objects = sys.Objects;
                g.Caves = caves;
                MapGen.RaiseMound(g);
                long gen = watch.ElapsedMilliseconds;
                var placed = Features.Place(g, new TerrainDetail(g), out var gs);
                return (g, placed, gs, gen, watch.ElapsedMilliseconds - gen);
            });
            while (!full.IsCompleted) yield return null;
            if (full.IsFaulted) { GameStatus.Lines["~caves"] = GameStatus.Failure = "caves: " + full.Exception; yield break; }
            var (map, edits, graphs, genMs, placeMs) = full.Result;
            lines.Add($"caves: {graphs.Length} graphs ({string.Join(", ", graphs.GroupBy(g => g.Class).Select(g => g.Key + " " + g.Count()))}) placed in {placeMs} ms after a {genMs} ms full regeneration, both in a task, GV {WorldConfig.GeneratorVersion}");
            var hall = Nearest(graphs, "hall");
            var lab = Nearest(graphs, "labyrinth");
            if (hall == null || lab == null) { GameStatus.Lines["~caves"] = GameStatus.Failure = lines[0] + "; no " + (hall == null ? "hall" : "labyrinth") + " in this world"; yield break; }
            cam.fieldOfView = 60f;
            for (var w = ShowHour(CaveShotHour); w.MoveNext();) yield return null;

            int ei = lab.Nodes.FindIndex(n => n.Entrance);
            var mouth = lab.Nodes[ei];
            var edge = lab.Edges.Find(x => x.A == ei || x.B == ei);
            var p = edge.Points;
            int inner = edge.A == ei ? Mathf.Min(6, p.Length / 3 - 1) * 3 : Mathf.Max(0, p.Length / 3 - 7) * 3;
            var m = new Vector3(mouth.X, mouth.Y, mouth.Z);
            var din = new Vector3(p[inner] - m.x, 0, p[inner + 2] - m.z).normalized;
            var eye = m - din * 10f;
            eye.y = Mathf.Max(ground.HeightAt(eye.x, eye.z) + 1.7f, m.y + 1.5f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(m + din * 3f - eye));
            cam.transform.GetPositionAndRotation(out var keepP, out var keepR);
            var open = OpenGround(ground, graphs, Vector3.Distance(m, home), map.Config.Size * map.Config.CellMeters);
            cam.transform.SetPositionAndRotation(open, Quaternion.LookRotation(din));
            var why = new double[3];
            double waited = LoadWaitTotal();
            for (var w = SettleWhy(120, why); w.MoveNext();) yield return null;
            lines.Add($"open ground control {open.x:F0},{open.y:F0},{open.z:F0}, {Vector3.Distance(open, home):F0} m from home: stream wait {LoadWaitTotal() - waited:F2} s (terrain {why[0]:F1}, object columns {why[1]:F1}, objects {why[2]:F1})");
            cam.transform.SetPositionAndRotation(keepP, keepR);
            why = new double[3];
            double wall = EditorApplication.timeSinceStartup;
            waited = LoadWaitTotal();
            for (var w = SettleWhy(120, why); w.MoveNext();) yield return null;
            lines.Add($"labyrinth busy: terrain {why[0]:F1} s, object columns {why[1]:F1} s, objects {why[2]:F1} s");
            lines.Add($"labyrinth {lab.Nodes.Count} chambers {lab.Edges.Count} tunnels, mouth {m.x:F0},{m.y:F0},{m.z:F0}, {Vector3.Distance(m, home):F0} m from home: stream wait {LoadWaitTotal() - waited:F2} s, settle {EditorApplication.timeSinceStartup - wall:F2} s");
            Shot(cam, null, "warmup");
            Shot(cam, dir, "caves-labyrinth-entrance");
            lines.Add(CollisionBench(ground, cols, lab, home));

            var reach = CaveLight.Distances(edits, graphs);
            int deep = -1;
            for (int q = 0; q < edits.Length; q++)
                if (CaveLight.Lit(edits[q]) && edits[q].Shape == FeatureEdit.Cave && Features.TileOf(edits[q], map.Config.CellMeters) == lab.Tile && reach[2 * q] < CaveLight.Unlit && (deep < 0 || Mathf.Min(reach[2 * q], reach[2 * q + 1]) > Mathf.Min(reach[2 * deep], reach[2 * deep + 1])))
                    deep = q;
            if (deep >= 0)
            {
                var de = edits[deep];
                bool fromA = reach[2 * deep] <= reach[2 * deep + 1];
                var da = fromA ? new Vector3(de.Ax, de.Ay, de.Az) : new Vector3(de.Bx, de.By, de.Bz);
                var db = fromA ? new Vector3(de.Bx, de.By, de.Bz) : new Vector3(de.Ax, de.Ay, de.Az);
                var at = Vector3.Lerp(da, db, 0.3f);
                float tx = at.x, ty = at.y, tz = at.z;
                Walk.Step(ground, ground.EditsNear(tx, tz, Walk.Radius), cols, ground.WalkStep, ref tx, ref ty, ref tz, 0, 0, Walk.Reach);
                eye = new Vector3(tx, ty + 1.7f, tz);
                var ahead = db - da;
                ahead.y = 0;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(ahead.normalized));
                waited = LoadWaitTotal();
                for (var w = Settle(120); w.MoveNext();) yield return null;
                float walked = Mathf.Lerp(reach[2 * deep], reach[2 * deep + 1], fromA ? 0.3f : 0.7f);
                lines.Add($"deep tunnel at {eye.x:F0},{eye.y:F0},{eye.z:F0}: {walked:F0} m of tunnel from the nearest mouth, tunnel R {de.R:F0} m: stream wait {LoadWaitTotal() - waited:F2} s");
                Shot(cam, null, "warmup");
                Shot(cam, dir, "caves-deep-tunnel");
                ExposedShot(cam, dir, "caves-deep-tunnel-debug-exposure", CaveDebugExposure);
                GeometryShot(cam, dir, "caves-deep-tunnel-debug-unlit");
            }
            else lines.Add("deep tunnel: no labyrinth tunnel reachable from a mouth");

            var hn = hall.Nodes.Find(n => n.Hall);
            var hc = new Vector3(hn.X, hn.Y, hn.Z);
            float rh = 0f;
            var bridge = Vector3.zero;
            int bridges = 0;
            foreach (var e in edits)
            {
                if (Features.TileOf(e, map.Config.CellMeters) != hall.Tile) continue;
                if (e.Shape == FeatureEdit.CaveRoom && new Vector2(e.Ax - hc.x, e.Az - hc.z).magnitude < 4f) rh = Mathf.Max(rh, e.R);
                if (e.Shape == FeatureEdit.Bridge && bridges++ == 0) bridge = new Vector3((e.Ax + e.Bx) * 0.5f, (e.Ay + e.By) * 0.5f, (e.Az + e.Bz) * 0.5f);
            }
            var toward = bridges > 0 ? new Vector3(bridge.x - hc.x, 0, bridge.z - hc.z).normalized : Vector3.forward;
            float fx = hc.x - toward.x * rh * 0.3f, fy = hc.y, fz = hc.z - toward.z * rh * 0.3f;
            Walk.Step(ground, ground.EditsNear(fx, fz, Walk.Radius), cols, ground.WalkStep, ref fx, ref fy, ref fz, 0, 0, Walk.Reach);
            eye = new Vector3(fx, fy + 1.7f, fz);
            var look = bridges > 0 ? bridge : hc + toward * rh;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            waited = LoadWaitTotal();
            for (var w = Settle(120); w.MoveNext();) yield return null;
            lines.Add($"hall {hall.Nodes.Count(n => n.Entrance)} entrances, {bridges} rock bridges, hollow R {rh * Features.CaveHollow:F0} m, centre {hc.x:F0},{hc.y:F0},{hc.z:F0}, floor at eye {fy:F1}: stream wait {LoadWaitTotal() - waited:F2} s");
            Shot(cam, null, "warmup");
            Shot(cam, dir, "caves-hall");
            ExposedShot(cam, dir, "caves-hall-debug-exposure", CaveDebugExposure);
            GeometryShot(cam, dir, "caves-hall-debug-unlit");
            var probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(probe.GetComponent<Collider>());
            var probeMat = new Material(Shader.Find("Stabby/Object"));
            probe.GetComponent<MeshRenderer>().sharedMaterial = probeMat;
            probe.transform.position = eye + (look - eye).normalized * 4f - Vector3.up * 0.5f;
            probe.transform.localScale = Vector3.one * 1.5f;
            ExposedShot(cam, dir, "caves-hall-debug-probe", CaveDebugExposure);
            UndimmedShot(cam, dir, "caves-hall-debug-probe-undimmed", CaveDebugExposure);
            Object.DestroyImmediate(probe);
            Object.DestroyImmediate(probeMat);
            var sky = DayNightSystem.Shown;
            lines.Add($"pinned hour {sky.Hour:F2}, moon phase {sky.Phase:F2}; each debug-exposure inspection shot is the same frame with +{CaveDebugExposure:F0} EV applied to the saved pixels, no lights added; the debug-probe pair adds a 1.5 m Stabby/Object sphere 4 m ahead in the hall, dimmed by the cave volume and then undimmed; each debug-unlit shot turns cave darkness off to show the geometry and is never the look");
            GameStatus.Lines["~caves"] = string.Join("\n", lines);
        }

        static CaveGraph Nearest(CaveGraph[] graphs, string cls)
        {
            CaveGraph best = null;
            float bd = float.MaxValue;
            foreach (var g in graphs)
            {
                if (g.Class != cls) continue;
                var n = g.Nodes[0];
                float d = new Vector2(n.X - home.x, n.Z - home.z).sqrMagnitude;
                if (d < bd) { bd = d; best = g; }
            }
            return best;
        }

        static string CollisionBench(Ground ground, ObjectColumns cols, CaveGraph lab, Vector3 open)
        {
            var cave = new List<Vector3>();
            foreach (var e in lab.Edges)
                for (int k = 0; k + 2 < e.Points.Length; k += 15) cave.Add(new Vector3(e.Points[k], e.Points[k + 1], e.Points[k + 2]));
            var plain = new List<Vector3>();
            for (int k = 0; k < cave.Count; k++)
            {
                float a = k * 2.399963f, r = 20f + k % 7 * 15f;
                float x = open.x + Mathf.Sin(a) * r, z = open.z + Mathf.Cos(a) * r;
                plain.Add(new Vector3(x, ground.HeightAt(x, z) + 1f, z));
            }
            double us(List<Vector3> at, out float edits)
            {
                long ticks = 0;
                int calls = 0;
                edits = 0;
                float s = ground.WalkStep, fall = Walk.FallSpeed / 60f;
                foreach (var q in at)
                {
                    float x = q.x, y = q.y, z = q.z;
                    Walk.Step(ground, ground.EditsNear(x, z, Walk.Radius), cols, s, ref x, ref y, ref z, 0, 0, Walk.Reach);
                    edits += ground.EditsNear(x, z, Walk.Radius).N;
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    for (int i = 0; i < 200; i++)
                    {
                        float px = x, py = y, pz = z, dx = 0.09f * Mathf.Sin(i * 0.7f), dz = 0.09f * Mathf.Cos(i * 0.7f);
                        Walk.Step(ground, ground.EditsNear(px, pz, 0.1f + Walk.Radius), cols, s, ref px, ref py, ref pz, dx, dz, fall);
                    }
                    ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                    calls += 200;
                }
                edits /= Mathf.Max(1, at.Count);
                return ticks * 1e6 / System.Diagnostics.Stopwatch.Frequency / Mathf.Max(1, calls);
            }
            us(plain, out _);
            double c = us(cave, out float ce), o = us(plain, out float oe);
            return $"collision (managed Walk.Step, one 60 Hz stride): labyrinth {c:F2} us over {cave.Count} spots with {ce:F0} edits per cell, open ground near home {o:F2} us with {oe:F0}; ratio {c / System.Math.Max(o, 1e-6):F2}";
        }
    }
}
