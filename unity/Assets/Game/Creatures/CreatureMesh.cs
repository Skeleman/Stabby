using System.Collections.Generic;
using Stabby.World;
using Unity.Mathematics;

namespace Stabby.Game
{
    public static class CreatureMesh
    {
        public const int Stride = 10;
        const int Sides = 18, CapRings = 6, TubeRings = 8;

        public static void Build(CreatureShape s, bool trunk, List<float> verts, List<ushort> index)
        {
            verts.Clear();
            index.Clear();
            int rings = 0;
            if (s.Sphere)
            {
                for (int k = 0; k <= 2 * CapRings; k++)
                {
                    float phi = math.PI * (k / (2f * CapRings) - 0.5f);
                    Ring(s, verts, math.sin(phi) * s.RadiusStart, math.cos(phi) * s.RadiusStart, math.sin(phi), math.cos(phi), 0.5f + 0.5f * math.sin(phi), 0, false, 1);
                    rings++;
                }
            }
            else
            {
                float r0 = s.RadiusStart, r1 = s.RadiusEnd, len = s.Length, span = len + r0 + r1;
                float slope = (r0 - r1) / len;
                for (int k = 0; k < CapRings; k++)
                {
                    float phi = 0.5f * math.PI * (1 - (float)k / CapRings);
                    float z = -r0 * math.sin(phi);
                    Ring(s, verts, z, r0 * math.cos(phi), -math.sin(phi), math.cos(phi), (z + r0) / span, math.sin(phi), trunk, 0);
                    rings++;
                }
                for (int k = 0; k <= TubeRings; k++)
                {
                    float t = (float)k / TubeRings;
                    float nz = slope / math.sqrt(1 + slope * slope), nr = 1 / math.sqrt(1 + slope * slope);
                    Ring(s, verts, len * t, math.lerp(r0, r1, t), nz, nr, (len * t + r0) / span, 0, trunk, t);
                    rings++;
                }
                for (int k = 1; k <= CapRings; k++)
                {
                    float phi = 0.5f * math.PI * k / CapRings;
                    float z = len + r1 * math.sin(phi);
                    Ring(s, verts, z, r1 * math.cos(phi), math.sin(phi), math.cos(phi), (z + r0) / span, 0, trunk, 1);
                    rings++;
                }
            }
            for (int r = 0; r + 1 < rings; r++)
                for (int j = 0; j < Sides; j++)
                {
                    int a = r * (Sides + 1) + j, b = a + 1, c = a + Sides + 1, d = c + 1;
                    index.Add((ushort)a); index.Add((ushort)c); index.Add((ushort)b);
                    index.Add((ushort)b); index.Add((ushort)c); index.Add((ushort)d);
                }
        }

        static void Ring(CreatureShape s, List<float> verts, float z, float r, float nz, float nr, float along, float rear, bool trunk, float t)
        {
            for (int j = 0; j <= Sides; j++)
            {
                float a = 2 * math.PI * j / Sides;
                float cx = math.sin(a), cy = math.cos(a);
                float sag = trunk && cy < 0 ? 1 + 0.14f * math.sin(math.PI * math.saturate(t)) * -cy : 1;
                float3 p = new float3(cx * r, cy * r * s.Flatten * sag, z);
                float3 n = math.normalize(new float3(cx * nr, cy * nr / (s.Flatten * sag), nz));
                float under = math.smoothstep(0.15f, 0.75f, -cy * nr);
                float accent = math.max(s.Belly * under, math.max(s.Rump * math.smoothstep(0.35f, 0.9f, rear) * math.smoothstep(-0.6f, 0.2f, cy), s.Tip * math.smoothstep(0.65f, 1f, along)));
                verts.Add(p.x); verts.Add(p.y); verts.Add(p.z);
                verts.Add(n.x); verts.Add(n.y); verts.Add(n.z);
                verts.Add(s.Tone); verts.Add(accent); verts.Add(along); verts.Add(s.Sphere ? 1 : 0);
            }
        }
    }
}
