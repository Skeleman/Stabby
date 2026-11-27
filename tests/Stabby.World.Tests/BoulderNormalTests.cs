using System.Globalization;
using Stabby.World;

namespace Stabby.World.Tests;

public class BoulderNormalTests
{
    struct FlatColumns : IColumnField
    {
        public float Height(int i, int k) => 0.5f;
    }

    static readonly float[] Reliefs = { 0f, 0.12f };

    [Fact]
    public void SteepBoulderFacesKeepSteepNormals()
    {
        float r = 3f, cx = 12.3f, cz = 11.7f, cy = 0.5f + 0.6f * r;
        foreach (float relief in Reliefs)
        {
            var e = new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Boulder, R = r, N = relief * r, S = 0.37f, Ax = cx - 0.6f, Ay = cy, Az = cz - 0.3f, Bx = cx + 0.6f, By = cy, Bz = cz + 0.3f };
            var items = new[] { e };
            var f = new NetsBuffers();
            SurfaceNets.Edited(new FlatColumns(), new EditArray { Items = items }, new NetsChunk { X0 = 0, Z0 = 0, Cells = 24, StepM = 1f }, ref f);
            Assert.Null(WorldChecks.BoulderNormalFault(new FlatColumns(), f, e, items, 1f));
            int steep = 0;
            float worst = float.NegativeInfinity;
            foreach (int t in new HashSet<int>(f.Tris))
            {
                int v = t * 6;
                float x = f.Verts[v], y = f.Verts[v + 1], z = f.Verts[v + 2], ny = f.Verts[v + 4];
                float ax = e.Bx - e.Ax, az = e.Bz - e.Az;
                float u = System.Math.Clamp(((x - e.Ax) * ax + (z - e.Az) * az) / (ax * ax + az * az), 0f, 1f);
                float dx = x - (e.Ax + u * ax), dy = System.Math.Max(0f, y - cy), dz = z - (e.Az + u * az);
                float d = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz), ty = dy / d;
                if (System.Math.Abs(d - r) > 0.6f || y < 0.7f || System.Math.Abs(ty) > 0.5f) continue;
                steep++;
                worst = System.Math.Max(worst, ny - ty);
            }
            Assert.True(steep > 40, string.Create(CultureInfo.InvariantCulture, $"relief {relief}: only {steep} steep vertices"));
            Assert.True(worst < 0.15f, string.Create(CultureInfo.InvariantCulture, $"relief {relief}: steep normal tilts up by {worst:F2}"));
        }
    }
}
