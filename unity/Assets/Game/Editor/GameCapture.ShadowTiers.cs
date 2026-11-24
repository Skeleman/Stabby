using System.Collections.Generic;
using Stabby.Viewer;
using Stabby.World;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Stabby.Game.Editor
{
    public static partial class GameCapture
    {
        static System.Collections.IEnumerator ShadowTiersTour(Camera cam, string dir)
        {
            var server = ServerWorld();
            var lines = new List<string>();
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 60f;
            if (server == null || !AimBiome(cam, "temperate-forest") || !AimForest(cam, "temperate-forest", 1)) { GameStatus.Lines["~shadowtiers"] = "shadowtiers: no temperate forest found"; yield break; }
            var stand = cam.transform.position;
            var d = Vector3.forward;
            float open = float.MaxValue;
            for (int a = 0; a < 32; a++)
            {
                var td = Quaternion.Euler(0, a * 11.25f, 0) * Vector3.forward;
                float eyeY = GroundHeight(stand.x, stand.z) + 14f, rise = float.MinValue;
                for (float r = 20f; r <= 300f; r += 10f) rise = Mathf.Max(rise, (GroundHeight(stand.x + td.x * r, stand.z + td.z * r) - eyeY) / r);
                if (rise < open) { open = rise; d = td; }
            }
            var start = stand;
            PlaceServerPlayers(server, new Vector2(start.x, start.z));
            Vector3 Pose(float along)
            {
                var p = start + d * along;
                p.y = GroundHeight(p.x, p.z) + 14f;
                return p;
            }
            var far = start + d * 200f;
            float pitch = Mathf.Atan2(Pose(0).y - GroundHeight(far.x, far.z), 200f) * Mathf.Rad2Deg;
            var look = Quaternion.LookRotation(d) * Quaternion.Euler(pitch, 0, 0);
            cam.transform.SetPositionAndRotation(Pose(0), look);
            for (var w = Settle(360); w.MoveNext();) yield return null;
            for (int k = 0; k < 1800 && ObjectViewSystem.Pending > 0; k++) yield return null;
            var asset = UniversalRenderPipeline.asset;
            lines.Add($"pose eye {start.x:F0},{Pose(0).y:F0},{start.z:F0} along {d.x:F2},{d.z:F2}, 14 m above ground, pitch {-pitch:F1} aimed at ground 200 m out, worst rise {open:F2}, FOV 60");
            lines.Add($"shadow distance {asset.shadowDistance:F1} m, {asset.shadowCascadeCount} cascades, splits2 {asset.cascade2Split:F4} splits3 {asset.cascade3Split} splits4 {asset.cascade4Split}");
            Shot(cam, null, "warmup");
            Shot(cam, dir, "shadowtiers-forest");
            float dist = asset.shadowDistance;
            int cascades = asset.shadowCascadeCount;
            var rows = new double[3, 3, 3, 2];
            var casting = new string[3];
            for (int run = 0; run < 3; run++)
                for (int cfg = 0; cfg < 3; cfg++)
                {
                    asset.shadowDistance = cfg == 1 ? 60f : dist;
                    asset.shadowCascadeCount = cfg == 1 ? 1 : cascades;
                    TerrainViewSystem.ShadowRule = TerrainViewSystem.SprayShadows = cfg != 2;
                    for (int k = 0; k < 4; k++) yield return null;
                    BenchRound(cam, rows, cfg, run);
                    if (GameStatus.Line("terrain", out var tl)) casting[cfg] = System.Text.RegularExpressions.Regex.Match(tl, @"\d+ casting \(\d+ vertices\)").Value;
                }
            asset.shadowDistance = dist;
            asset.shadowCascadeCount = cascades;
            TerrainViewSystem.ShadowRule = TerrainViewSystem.SprayShadows = true;
            for (int k = 0; k < 4; k++) yield return null;
            lines.Add(MedianLine("shadowtiers-forest", rows, 0) + "; " + casting[0]);
            lines.Add(MedianLine("shadowtiers-forest shadows forced to 60 m, 1 cascade", rows, 1) + "; " + casting[1]);
            lines.Add(MedianLine("shadowtiers-forest terrain rule and spray shadows off", rows, 2) + "; " + casting[2]);
            const int MoveFrames = 120;
            const float MoveMetres = 24f;
            var rt = new RenderTexture(ProbeW, ProbeH, 24);
            var tex = new Texture2D(ProbeW, ProbeH, TextureFormat.RGB24, false);
            try
            {
                float[] prev = null;
                float worst = 0;
                int jumps = 0;
                for (int k = 0; k <= MoveFrames; k++)
                {
                    cam.transform.SetPositionAndRotation(Pose(MoveMetres * k / MoveFrames), look);
                    yield return null;
                    if (k % (MoveFrames / 3) == 0) Shot(cam, dir, $"shadowtiers-move-{k / (MoveFrames / 3)}");
                    var lum = Probe(cam, rt, tex);
                    if (prev != null)
                    {
                        float frame = 0;
                        for (int by = 0; by < ProbeH / 12; by++)
                            for (int bx = 0; bx < ProbeW / 12; bx++)
                            {
                                float a = 0, b = 0;
                                for (int y = by * 12; y < by * 12 + 12; y++)
                                    for (int x = bx * 12; x < bx * 12 + 12; x++) { a += lum[y * ProbeW + x]; b += prev[y * ProbeW + x]; }
                                frame = Mathf.Max(frame, Mathf.Abs(a - b) / 144f);
                            }
                        worst = Mathf.Max(worst, frame);
                        if (frame > 0.08f) jumps++;
                    }
                    prev = lum;
                }
                lines.Add($"move {MoveMetres:F0} m over {MoveFrames} frames: {jumps} frames with a 12 px block jump > 0.08, worst block change {worst:F3}");
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
            GameStatus.Line("terrain", out var t);
            lines.Add(t);
            GameStatus.Lines["~shadowtiers"] = string.Join("\n", lines);
        }

        static System.Collections.IEnumerator SprayShadowsTour(Camera cam, string dir)
        {
            GameStatus.HoldCamera = true;
            cam.fieldOfView = 60f;
            var lines = new List<string>();
            Light sun = RenderSettings.sun != null ? RenderSettings.sun : System.Array.Find(Object.FindObjectsByType<Light>(), l => l.type == LightType.Directional);
            var sd = sun != null ? sun.transform.forward : Vector3.down;
            var across = new Vector3(-sd.z, 0, sd.x).normalized;
            if (across.sqrMagnitude < 0.5f) across = Vector3.right;
            float drop = Mathf.Max(-sd.y, 0.15f);
            ObjectField field = null;
            ClientMapSystem map = null;
            foreach (var w in Unity.Entities.World.All)
                if (w.Name == GameSettings.ViewClient)
                {
                    field = w.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field;
                    map = w.GetExistingSystemManaged<ClientMapSystem>();
                }
            if (field != null)
            {
                var pick = default(ObjectInstance);
                float best = float.MaxValue;
                foreach (var o in field.Discoveries())
                {
                    if (!field.IsVent(o) || field.VentKind(o) != ObjectField.Geyser) continue;
                    float score = new Vector2(o.X - home.x, o.Z - home.z).magnitude;
                    if (score < best) { best = score; pick = o; }
                }
                if (best < float.MaxValue)
                {
                    var pools = new List<float>();
                    field.SpringPoolsAt(pick, pools);
                    var c = new Vector3(pools[0], pools[4], pools[1]);
                    float h = WaterGeometry.ColumnPerRadius * pools[2];
                    Structures.GeyserCycle(field.VentSeed(pick), out float interval, out float duration, out float offset);
                    TerrainViewSystem.ClockOverride = offset + 0.4 * duration;
                    var s = c + new Vector3(sd.x, 0, sd.z) * (0.5f * h / drop);
                    for (var e = SprayShot(cam, dir, "sprayshadows-geyser", 0.5f * (c + Vector3.up * 0.3f * h + s), across, Mathf.Max(h, (s - c).magnitude) * 1.5f); e.MoveNext();) yield return null;
                    TerrainViewSystem.ClockOverride = null;
                    lines.Add($"geyser at {c.x:F0},{c.y:F0},{c.z:F0}, column {h:F1} m, shadow centre {s.x:F0},{s.z:F0}");
                }
                else lines.Add("no geyser found");
            }
            if (map?.Map != null)
            {
                var best = default(FallCurtain);
                bool found = false;
                foreach (var l in NearField.Lines(map.Map))
                    for (int k = 1; k < l.X.Length; k++)
                        if (WaterGeometry.FallAt(l, k, out var f, out _) && f.Drop >= 3f && (!found || f.Discharge > best.Discharge)) { best = f; found = true; }
                if (found)
                {
                    var fd = new Vector3(best.DirX, 0, best.DirZ);
                    var lip = new Vector3(best.LipX, best.LipY, best.LipZ);
                    float h = best.Drop;
                    var mid = lip + fd * 0.5f - Vector3.up * 0.55f * h;
                    var side = Vector3.Dot(fd, sd) > 0 ? fd : (fd + 0.6f * new Vector3(fd.z, 0, -fd.x)).normalized;
                    for (var e = SprayShot(cam, dir, "sprayshadows-curtain", mid, side, 1.3f * h + 0.6f * best.Width + 6f); e.MoveNext();) yield return null;
                    lines.Add($"fall at {lip.x:F0},{lip.y:F0},{lip.z:F0}, drop {h:F1} m, width {best.Width:F0} m, discharge {best.Discharge:F0}");
                }
                else lines.Add("no waterfall found");
            }
            lines.Add($"sun {sd.x:F2},{sd.y:F2},{sd.z:F2}");
            GameStatus.Lines["~sprayshadows"] = string.Join("\n", lines);
        }

        static System.Collections.IEnumerator SprayShot(Camera cam, string dir, string name, Vector3 target, Vector3 side, float dist)
        {
            var eye = target + side * dist + Vector3.up * 0.45f * dist;
            eye.y = Mathf.Max(eye.y, GroundHeight(eye.x, eye.z) + 2f);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            for (var w = Settle(240); w.MoveNext();) yield return null;
            for (int k = 0; k < 1200 && ObjectViewSystem.Pending > 0; k++) yield return null;
            Shot(cam, null, "warmup");
            Shot(cam, dir, name);
            TerrainViewSystem.SprayShadows = false;
            for (int k = 0; k < 4; k++) yield return null;
            Shot(cam, dir, name + "-off");
            TerrainViewSystem.SprayShadows = true;
            for (int k = 0; k < 4; k++) yield return null;
        }

        static readonly (int w, int h)[] BenchSizes = { (1920, 1080), (2560, 1440), (3840, 2160) };

        static void BenchRound(Camera cam, double[,,,] rows, int cfg, int run)
        {
            for (int r = 0; r < BenchSizes.Length; r++)
            {
                Shader.SetGlobalFloat("_StabbyDetailOff", 1);
                rows[cfg, r, run, 0] = BenchAt(cam, BenchSizes[r].w, BenchSizes[r].h);
                Shader.SetGlobalFloat("_StabbyDetailOff", 0);
                rows[cfg, r, run, 1] = BenchAt(cam, BenchSizes[r].w, BenchSizes[r].h);
            }
        }

        static string MedianLine(string label, double[,,,] rows, int cfg)
        {
            int runs = rows.GetLength(2);
            var sb = new System.Text.StringBuilder($"bench {label}, median of {runs} interleaved (ms, base/full):");
            for (int r = 0; r < BenchSizes.Length; r++)
            {
                sb.Append($" {BenchSizes[r].h}p");
                for (int m = 0; m < 2; m++)
                {
                    var v = new double[runs];
                    for (int k = 0; k < runs; k++) v[k] = rows[cfg, r, k, m];
                    System.Array.Sort(v);
                    sb.Append(m == 0 ? " " : "/").Append(v[runs / 2].ToString("F2"));
                }
            }
            return sb.ToString();
        }
    }
}
