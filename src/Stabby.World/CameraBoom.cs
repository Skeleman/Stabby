using System;

namespace Stabby.World
{
    public static class CameraBoom
    {
        public const float ObjectRadius = 0.35f;

        public static float Clear<TG, TE, TO>(TG g, Func<float, float, TE> edits, TO o, float s, float px, float py, float pz, float bx, float by, float bz, float boom, float min, float margin, int samples)
            where TG : struct, IGroundHeight where TE : struct, IEditList where TO : struct, IColumnTable
        {
            float prevT = 0, prevC = Clearance(g, edits, s, px, py, pz, margin);
            if (prevC <= 0) return min;
            for (int k = 1; k <= samples; k++)
            {
                float t = boom * k / samples;
                float x = px + bx * t, y = py + by * t, z = pz + bz * t;
                float c = Clearance(g, edits, s, x, y, z, margin);
                if (c <= 0) return Math.Max(min, prevT + (t - prevT) * prevC / (prevC - c));
                if (t >= min && Obstacles.Top(o, x, z, ObjectRadius, y - margin, y + margin) > float.NegativeInfinity) return Math.Max(min, prevT);
                prevT = t;
                prevC = c;
            }
            return boom;
        }

        static float Clearance<TG, TE>(TG g, Func<float, float, TE> edits, float s, float x, float y, float z, float margin) where TG : struct, IGroundHeight where TE : struct, IEditList
        {
            var e = edits(x, z);
            for (int q = 0; q < e.Count; q++)
            {
                var f = e.At(q);
                if (x >= f.MinX && x <= f.MaxX && z >= f.MinZ && z <= f.MaxZ) return -Walk.Solid(g, e, s, x, y, z) - margin;
            }
            return y - g.HeightAt(x, z) - margin;
        }
    }
}
