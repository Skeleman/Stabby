using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed partial class ObjectField
    {
        public const float BridgeSampleM = 1f;

        int bridgeDef = -2;
        ObjectInstance[][] bridgeBins;

        public int BridgeDef()
        {
            int d = bridgeDef;
            if (d != -2) return d;
            string id = lib.Roads.BridgeObject;
            d = string.IsNullOrEmpty(id) || !(lib.Roads.SpanM > 0) ? -1 : lib.Objects.FindIndex(o => o.Id == id);
            return bridgeDef = d;
        }

        public const int BridgeLookCells = 3, BridgeTries = 3;

        Dictionary<long, bool> bridgeSeen;

        bool BridgeOk(int a, int b)
        {
            bridgeSeen ??= new Dictionary<long, bool>();
            long key = (long)a * n * n + b;
            if (bridgeSeen.TryGetValue(key, out bool ok)) return ok;
            ok = BridgeFault(a % n * cm, a / n * cm, b % n * cm, b / n * cm, out _, out _, out _) == null;
            bridgeSeen[key] = ok;
            return ok;
        }

        bool WideAhead(int cx, int cz, int dx, int dz)
        {
            for (int k = 1; k <= BridgeLookCells; k++)
            {
                int x = cx + k * dx, z = cz + k * dz;
                if (x < 0 || z < 0 || x >= n || z >= n) return false;
                if (WideRiver(z * n + x)) return true;
            }
            return false;
        }

        bool WideRiver(int i)
        {
            for (int k = blockStart[i]; k < blockStart[i + 1]; k++)
            {
                int item = blockItems[k];
                if (item >= 0 && phw[item] > FordHalfM) return true;
            }
            return false;
        }

        bool RiverUnder(float x, float z, float pad, out float surf, out float hw)
        {
            surf = float.MinValue; hw = 0;
            bool any = false;
            int i = Cell(z) * n + Cell(x);
            for (int k = blockStart[i]; k < blockStart[i + 1]; k++)
            {
                int item = blockItems[k];
                if (item < 0) continue;
                float g = phw[item] + pad;
                if (PointSegment2(x, z, pax[item], paz[item], pbx[item], pbz[item]) >= g * g) continue;
                any = true;
                surf = Math.Max(surf, psurf[item]);
                hw = Math.Max(hw, phw[item]);
            }
            return any;
        }

        string Abutment(float x, float z, out float h)
        {
            h = 0;
            if (x < 0 || z < 0 || x >= worldM || z >= worldM) return "leaves the map";
            int i = Cell(z) * n + Cell(x);
            if (w.Sea[i] || w.Water.LakeId[i] >= 0) return "stands in standing water";
            if (RiverUnder(x, z, RoadHalfM, out _, out _)) return "stands in a river";
            h = GroundAt(detail, cm, x, z);
            return null;
        }

        public string BridgeFault(float x0, float z0, float x1, float z1, out float h0, out float h1, out float arch)
        {
            h0 = h1 = arch = 0;
            if (BridgeDef() < 0) return "has no bridge kind";
            float dx = x1 - x0, dz = z1 - z0, len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len > lib.Roads.SpanM) return "spans too far";
            if (len < cm) return "is too short";
            float ux = dx / len, uz = dz / len;
            string a = Abutment(x0, z0, out h0);
            if (a != null) return "abutment " + a;
            a = Abutment(x1, z1, out h1);
            if (a != null) return "abutment " + a;
            float most = 0.25f * (RoadGrade * len - Math.Abs(h1 - h0));
            if (most < 0) return "is too steep";
            int pieces = Math.Max(2, (int)Math.Ceiling(len / BridgeSampleM));
            bool wet = false;
            for (int k = 1; k < pieces; k++)
            {
                float t = (float)k / pieces, x = x0 + dx * t, z = z0 + dz * t;
                int i = Cell(z) * n + Cell(x);
                if (w.Sea[i] || w.Water.LakeId[i] >= 0) return "crosses standing water";
                if (!RiverUnder(x, z, 0, out _, out float hw)) continue;
                if (2 * hw > lib.Roads.RiverM) return "crosses a river too wide to bridge";
                wet = true;
            }
            if (!wet) return "spans no water";
            float need = 0;
            for (int k = 1; k < pieces; k++)
            {
                float t = (float)k / pieces, x = x0 + dx * t, z = z0 + dz * t;
                float req = GroundAt(detail, cm, x, z) - RoadClearM;
                if (RiverUnder(x, z, 0, out float surf, out _)) req = Math.Max(req, surf + lib.Roads.ClearM);
                float lift = req - (h0 + (h1 - h0) * t);
                if (lift > 0) need = Math.Max(need, lift / (4 * t * (1 - t)));
            }
            if (need > most) return "cannot clear the water";
            arch = need;
            int steps = Math.Max(1, (int)Math.Ceiling(len / cm));
            for (int k = 0; k < steps; k++)
            {
                float ta = (float)k / steps, tb = (float)(k + 1) / steps;
                string f = StepFault(x0 + dx * ta, z0 + dz * ta, x0 + dx * tb, z0 + dz * tb, Deck(h0, h1, need, ta), Deck(h0, h1, need, tb), out _, true);
                if (f != null) return f;
            }
            return null;
        }

        static float Deck(float h0, float h1, float arch, float t) => h0 + (h1 - h0) * t + arch * 4 * t * (1 - t);

        void EmitBridges(int d, int chunk, List<ObjectInstance> list, ref int ordinal)
        {
            var bin = BridgeBins()[chunk];
            if (bin == null) return;
            foreach (var m in bin)
            {
                var o = m;
                o.Id = ObjectIds.Make(chunk, d, ordinal++);
                list.Add(o);
            }
        }

        ObjectInstance[][] BridgeBins()
        {
            var segs = Roads();
            var found = bridgeBins;
            if (found != null) return found;
            lock (roadLock)
            {
                if (bridgeBins != null) return bridgeBins;
                var bins = new ObjectInstance[across * across][];
                int d = BridgeDef();
                var def = lib.Objects[d];
                float span = WorldChunks.Size * cm, size = def.Placement.SizeMinM;
                var rng = Rng.Stream(w.Config.Seed, "road-bridges");
                var lists = new List<ObjectInstance>[across * across];
                foreach (var s in segs)
                {
                    if (s.Kind != RoadSegment.Bridge || BridgeFault(s.X0, s.Z0, s.X1, s.Z1, out float h0, out float h1, out float arch) != null) continue;
                    float dx = s.X1 - s.X0, dz = s.Z1 - s.Z0, len = (float)Math.Sqrt(dx * dx + dz * dz), ux = dx / len, uz = dz / len;
                    float cy = (float)Math.Sqrt(Math.Max(0f, 0.5f * (1 + ux))), sy = (float)Math.Sqrt(Math.Max(0f, 0.5f * (1 - ux)));
                    if (uz > 0) sy = -sy;
                    int count = Math.Max(1, (int)Math.Ceiling(len / size));
                    for (int k = 0; k < count; k++)
                    {
                        float t = (k + 0.5f) / count, x = s.X0 + dx * t, z = s.Z0 + dz * t;
                        float slope = (h1 - h0 + arch * 4 * (1 - 2 * t)) / len, cp = 1 / (float)Math.Sqrt(1 + slope * slope);
                        float cz = (float)Math.Sqrt(0.5f * (1 + cp)), sz = (float)Math.Sqrt(Math.Max(0f, 0.5f * (1 - cp)));
                        if (slope < 0) sz = -sz;
                        var o = new ObjectInstance
                        {
                            Def = d, Variant = (int)(rng.NextU32() % (uint)def.Variants), Size = size,
                            X = x, Y = Deck(h0, h1, arch, t), Z = z,
                            Qx = sy * sz, Qy = cz * sy, Qz = cy * sz, Qw = cy * cz,
                        };
                        int c = Math.Min(across - 1, (int)(z / span)) * across + Math.Min(across - 1, (int)(x / span));
                        (lists[c] ?? (lists[c] = new List<ObjectInstance>())).Add(o);
                    }
                }
                for (int c = 0; c < lists.Length; c++) if (lists[c] != null) bins[c] = lists[c].ToArray();
                bridgeBins = bins;
                return bins;
            }
        }
    }
}
