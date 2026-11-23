using System.Collections.Generic;
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
            var rows = new double[2, 3, 3, 2];
            var casting = new string[2];
            for (int run = 0; run < 3; run++)
                for (int cfg = 0; cfg < 2; cfg++)
                {
                    asset.shadowDistance = cfg == 0 ? dist : 60f;
                    asset.shadowCascadeCount = cfg == 0 ? cascades : 1;
                    for (int k = 0; k < 4; k++) yield return null;
                    BenchRound(cam, rows, cfg, run);
                    if (GameStatus.Line("terrain", out var tl)) casting[cfg] = System.Text.RegularExpressions.Regex.Match(tl, @"\d+ casting \(\d+ vertices\)").Value;
                }
            asset.shadowDistance = dist;
            asset.shadowCascadeCount = cascades;
            for (int k = 0; k < 4; k++) yield return null;
            lines.Add(MedianLine("shadowtiers-forest", rows, 0) + "; " + casting[0]);
            lines.Add(MedianLine("shadowtiers-forest shadows forced to 60 m, 1 cascade", rows, 1) + "; " + casting[1]);
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
