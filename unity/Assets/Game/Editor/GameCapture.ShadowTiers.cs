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
            var fwd = cam.transform.forward;
            var d = new Vector3(fwd.x, 0, fwd.z).normalized;
            var start = cam.transform.position - d * 6f;
            PlaceServerPlayers(server, new Vector2(start.x, start.z));
            Vector3 Pose(float along)
            {
                var p = start + d * along;
                p.y = GroundHeight(p.x, p.z) + 14f;
                return p;
            }
            var look = Quaternion.LookRotation(d) * Quaternion.Euler(5f, 0, 0);
            cam.transform.SetPositionAndRotation(Pose(0), look);
            for (var w = Settle(360); w.MoveNext();) yield return null;
            for (int k = 0; k < 1800 && ObjectViewSystem.Pending > 0; k++) yield return null;
            var asset = UniversalRenderPipeline.asset;
            lines.Add($"pose eye {start.x:F0},{Pose(0).y:F0},{start.z:F0} along {d.x:F2},{d.z:F2}, 14 m above ground, pitch -5, FOV 60");
            lines.Add($"shadow distance {asset.shadowDistance:F1} m, {asset.shadowCascadeCount} cascades, splits2 {asset.cascade2Split:F4} splits3 {asset.cascade3Split} splits4 {asset.cascade4Split}");
            Shot(cam, null, "warmup");
            Shot(cam, dir, "shadowtiers-forest");
            lines.Add(Bench(cam, "shadowtiers-forest"));
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
    }
}
