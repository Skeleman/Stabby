using System.Collections.Generic;
using Stabby.World;
using Unity.Mathematics;

namespace Stabby.Game
{
    public static class CreatureMesh
    {
        public const int Stride = 12;

        struct RingAt
        {
            public float Z, R, Nz, Nr, Along, Rear, T;
        }

        public static void Build(CreatureShape s, bool trunk, bool low, List<float> verts, List<ushort> index)
        {
            int sides = low ? 8 : 18, capRings = low ? 3 : 6, tubeRings = low ? 4 : 12;
            verts.Clear();
            index.Clear();
            var rings = new List<RingAt>();
            if (s.Sphere)
            {
                for (int k = 0; k <= 2 * capRings; k++)
                {
                    float phi = math.PI * (k / (2f * capRings) - 0.5f);
                    rings.Add(new RingAt { Z = math.sin(phi) * s.RadiusStart, R = math.cos(phi) * s.RadiusStart, Nz = math.sin(phi), Nr = math.cos(phi), Along = 0.5f + 0.5f * math.sin(phi), T = 1 });
                }
            }
            else
            {
                float r0 = s.RadiusStart, r1 = s.RadiusEnd, len = s.Length, span = len + r0 + r1;
                float slope = (r0 - r1) / len;
                for (int k = 0; k < capRings; k++)
                {
                    float phi = 0.5f * math.PI * (1 - (float)k / capRings);
                    float z = -r0 * math.sin(phi);
                    rings.Add(new RingAt { Z = z, R = r0 * math.cos(phi), Nz = -math.sin(phi), Nr = math.cos(phi), Along = (z + r0) / span, Rear = math.sin(phi) });
                }
                float nz = slope / math.sqrt(1 + slope * slope), nr = 1 / math.sqrt(1 + slope * slope);
                for (int k = 0; k <= tubeRings; k++)
                {
                    float t = (float)k / tubeRings;
                    rings.Add(new RingAt { Z = len * t, R = math.lerp(r0, r1, t), Nz = nz, Nr = nr, Along = (len * t + r0) / span, T = t });
                }
                for (int k = 1; k <= capRings; k++)
                {
                    float phi = 0.5f * math.PI * k / capRings;
                    float z = len + r1 * math.sin(phi);
                    rings.Add(new RingAt { Z = z, R = r1 * math.cos(phi), Nz = math.sin(phi), Nr = math.cos(phi), Along = (z + r0) / span, T = 1 });
                }
            }
            int n = rings.Count, w = sides + 1;
            var pos = new float3[n * w];
            var nrm = new float3[n * w];
            for (int k = 0; k < n; k++)
                for (int j = 0; j <= sides; j++)
                {
                    float a = 2 * math.PI * (j % sides) / sides;
                    pos[k * w + j] = Point(s, trunk, rings[k], math.sin(a), math.cos(a), out nrm[k * w + j]);
                }
            if (trunk)
                for (int k = 0; k < n; k++)
                    for (int j = 0; j <= sides; j++)
                    {
                        int jj = j % sides;
                        float3 dr = pos[math.min(k + 1, n - 1) * w + jj] - pos[math.max(k - 1, 0) * w + jj];
                        float3 ds = pos[k * w + (jj + 1) % sides] - pos[k * w + (jj + sides - 1) % sides];
                        float3 c = math.cross(ds, dr);
                        float l = math.length(c);
                        if (l < 1e-7f) continue;
                        c /= l;
                        nrm[k * w + j] = math.dot(c, nrm[k * w + j]) < 0 ? -c : c;
                    }
            var skin = new float2[n];
            if (!trunk && !s.Sphere)
            {
                float len = s.Length, band = math.min(math.max(s.RadiusStart, 0.25f * len), 0.45f * len);
                for (int k = 0; k < n; k++)
                    skin[k] = new float2(1 - math.smoothstep(-band, band, rings[k].Z), math.smoothstep(len - band, len + band, rings[k].Z));
            }
            for (int k = 0; k < n; k++)
                for (int j = 0; j <= sides; j++)
                {
                    float a = 2 * math.PI * (j % sides) / sides;
                    float cy = math.cos(a);
                    var r = rings[k];
                    float3 p = pos[k * w + j], nn = nrm[k * w + j];
                    float under = math.smoothstep(0.15f, 0.75f, -cy * r.Nr);
                    float accent = math.max(s.Belly * under, math.max(s.Rump * math.smoothstep(0.35f, 0.9f, r.Rear) * math.smoothstep(-0.6f, 0.2f, cy), s.Tip * math.smoothstep(0.65f, 1f, r.Along)));
                    verts.Add(p.x); verts.Add(p.y); verts.Add(p.z);
                    verts.Add(nn.x); verts.Add(nn.y); verts.Add(nn.z);
                    verts.Add(s.Tone); verts.Add(accent); verts.Add(r.Along); verts.Add(s.Sphere ? 1 : 0);
                    verts.Add(skin[k].x); verts.Add(skin[k].y);
                }
            for (int r = 0; r + 1 < n; r++)
                for (int j = 0; j < sides; j++)
                {
                    int a = r * w + j, b = a + 1, c = a + w, d = c + 1;
                    index.Add((ushort)a); index.Add((ushort)c); index.Add((ushort)b);
                    index.Add((ushort)b); index.Add((ushort)c); index.Add((ushort)d);
                }
        }

        static float Bump(float x, float c, float w)
        {
            float d = (x - c) / w;
            return math.exp(-d * d);
        }

        static float3 Point(CreatureShape s, bool trunk, RingAt r, float cx, float cy, out float3 normal)
        {
            float sag = trunk && cy < 0 ? 1 + 0.14f * math.sin(math.PI * math.saturate(r.T)) * -cy : 1;
            normal = math.normalize(new float3(cx * r.Nr, cy * r.Nr / (s.Flatten * sag), r.Nz));
            if (!trunk) return new float3(cx * r.R, cy * r.R * s.Flatten, r.Z);
            float x = r.Along;
            float m = 1 + s.Haunch * Bump(x, 0.24f, 0.13f) * (0.55f + 0.45f * math.smoothstep(-0.6f, 0.6f, cy))
                        - s.Waist * Bump(x, 0.47f, 0.12f) * (0.35f + 0.65f * math.smoothstep(0.3f, -0.9f, cy));
            float hump = s.Hump * r.R * Bump(x, 0.8f, 0.1f) * math.smoothstep(0.2f, 1f, cy) * s.Flatten;
            return new float3(cx * r.R * m, cy * r.R * s.Flatten * sag * m + hump, r.Z);
        }
    }
}
