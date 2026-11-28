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
            ok = BridgeFault(a % n * cm, a / n * cm, b % n * cm, b / n * cm, out _, out _, out _, true) == null;
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

        bool[] wideCells;

        bool WideRiver(int i)
        {
            if (wideCells == null)
            {
                var wc = new bool[n * n];
                for (int c = 0; c < n * n; c++)
                    for (int k = blockStart[c]; k < blockStart[c + 1] && !wc[c]; k++)
                    {
                        int item = blockItems[k];
                        wc[c] = item >= 0 && phw[item] > FordHalfM;
                    }
                wideCells = wc;
            }
            return wideCells[i];
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
            string a = Footing(x, z);
            if (a == null) h = GroundAt(detail, cm, x, z);
            return a;
        }

        string Footing(float x, float z)
        {
            if (x < 0 || z < 0 || x >= worldM || z >= worldM) return "leaves the map";
            int i = Cell(z) * n + Cell(x);
            if (w.Sea[i] || w.Water.LakeId[i] >= 0) return "stands in standing water";
            if (RiverUnder(x, z, RoadHalfM, out _, out _)) return "stands in a river";
            return null;
        }

        public string BridgeFault(float x0, float z0, float x1, float z1, out float h0, out float h1, out float arch) => BridgeFault(x0, z0, x1, z1, out h0, out h1, out arch, false);

        string BridgeFault(float x0, float z0, float x1, float z1, out float h0, out float h1, out float arch, bool quick)
        {
            h0 = h1 = arch = 0;
            if (BridgeDef() < 0) return "has no bridge kind";
            float dx = x1 - x0, dz = z1 - z0, len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len > lib.Roads.SpanM) return "spans too far";
            if (len < cm) return "is too short";
            string a = Abutment(x0, z0, out h0);
            if (a != null) return "abutment " + a;
            a = Abutment(x1, z1, out h1);
            if (a != null) return "abutment " + a;
            float most = 0.25f * (RoadGrade * len - Math.Abs(h1 - h0));
            if (most < 0) return "is too steep";
            int pieces = Math.Max(2, (int)Math.Ceiling(len / BridgeSampleM));
            bool wet = false;
            float need = 0;
            for (int k = 1; k < pieces; k++)
            {
                float t = (float)k / pieces, x = x0 + dx * t, z = z0 + dz * t;
                int i = Cell(z) * n + Cell(x);
                if (w.Sea[i] || w.Water.LakeId[i] >= 0) return "crosses standing water";
                if (!RiverUnder(x, z, 0, out float surf, out float hw)) continue;
                if (2 * hw > lib.Roads.RiverM) return "crosses a river too wide to bridge";
                wet = true;
                float lift = surf + lib.Roads.ClearM - (h0 + (h1 - h0) * t);
                if (lift > 0) need = Math.Max(need, lift / (4 * t * (1 - t)));
            }
            if (!wet) return "spans no water";
            if (need > most) return "cannot clear the water";
            int steps = Math.Max(1, (int)Math.Ceiling(len / cm));
            if (quick)
                for (int k = 0; k < steps; k++)
                {
                    float ta = (float)k / steps, tb = (float)(k + 1) / steps;
                    string f = StepFault(x0 + dx * ta, z0 + dz * ta, x0 + dx * tb, z0 + dz * tb, Deck(h0, h1, need, ta), Deck(h0, h1, need, tb), Deck(h0, h1, most, ta), Deck(h0, h1, most, tb), out _, true);
                    if (f != null) return f;
                }
            int group = Math.Max(1, (int)(cm / BridgeSampleM));
            if (quick && Clears(x0, z0, dx, dz, h0, h1, pieces, group, most))
            {
                bool loose = false;
                for (int k = 0; k < steps && !loose; k++)
                {
                    float ta = (float)k / steps, tb = (float)(k + 1) / steps;
                    loose = StepFault(x0 + dx * ta, z0 + dz * ta, x0 + dx * tb, z0 + dz * tb, Deck(h0, h1, most, ta), Deck(h0, h1, most, tb), Deck(h0, h1, need, ta), Deck(h0, h1, need, tb), out _, true) != null;
                }
                if (!loose) return null;
            }
            for (int k0 = 1; k0 < pieces; k0 += group)
            {
                int k1 = Math.Min(pieces - 1, k0 + group - 1);
                if (!Rises(x0, z0, dx, dz, h0, h1, pieces, k0, k1, need)) continue;
                for (int k = k0; k <= k1; k++)
                {
                    float t = (float)k / pieces, x = x0 + dx * t, z = z0 + dz * t;
                    float req = GroundAt(detail, cm, x, z) - RoadClearM;
                    if (RiverUnder(x, z, 0, out float surf, out _)) req = Math.Max(req, surf + lib.Roads.ClearM);
                    float lift = req - (h0 + (h1 - h0) * t);
                    if (lift > 0) need = Math.Max(need, lift / (4 * t * (1 - t)));
                    if (need > most) return "cannot clear the water";
                }
            }
            arch = need;
            for (int k = 0; k < steps; k++)
            {
                float ta = (float)k / steps, tb = (float)(k + 1) / steps;
                float da = Deck(h0, h1, need, ta), db = Deck(h0, h1, need, tb);
                string f = StepFault(x0 + dx * ta, z0 + dz * ta, x0 + dx * tb, z0 + dz * tb, da, db, da, db, out _, true);
                if (f != null) return f;
            }
            return null;
        }

        bool Clears(float x0, float z0, float dx, float dz, float h0, float h1, int pieces, int group, float most)
        {
            for (int k0 = 1; k0 < pieces; k0 += group)
                if (!ClearsPart(x0, z0, dx, dz, h0, h1, pieces, k0, Math.Min(pieces - 1, k0 + group - 1), most)) return false;
            return true;
        }

        bool ClearsPart(float x0, float z0, float dx, float dz, float h0, float h1, int pieces, int k0, int k1, float most)
        {
            if (!Rises(x0, z0, dx, dz, h0, h1, pieces, k0, k1, most)) return true;
            if (k1 - k0 >= 3)
            {
                int km = (k0 + k1) / 2;
                return ClearsPart(x0, z0, dx, dz, h0, h1, pieces, k0, km, most) && ClearsPart(x0, z0, dx, dz, h0, h1, pieces, km + 1, k1, most);
            }
            for (int k = k0; k <= k1; k++)
            {
                float t = (float)k / pieces, lift = GroundAt(detail, cm, x0 + dx * t, z0 + dz * t) - RoadClearM - (h0 + (h1 - h0) * t);
                if (lift > 0 && lift / (4 * t * (1 - t)) > most) return false;
            }
            return true;
        }

        bool Rises(float x0, float z0, float dx, float dz, float h0, float h1, int pieces, int k0, int k1, float arch)
        {
            float ta = (float)k0 / pieces, tb = (float)k1 / pieces;
            float xa = x0 + dx * ta, za = z0 + dz * ta, xb = x0 + dx * tb, zb = z0 + dz * tb;
            float hi = RangeHi(Math.Min(xa, xb), Math.Min(za, zb), Math.Max(xa, xb), Math.Max(za, zb));
            for (int k = k0; k <= k1; k++)
            {
                float t = (float)k / pieces, lift = hi - RoadClearM - (h0 + (h1 - h0) * t);
                if (lift > 0 && lift / (4 * t * (1 - t)) > arch) return true;
            }
            return false;
        }

        const int RangeBlock = 32;
        float[][] rangeBlocks;

        float RangeHi(float x0, float z0, float x1, float z1)
        {
            var c = detail.Cells;
            int cn = c.N, across = (cn + RangeBlock - 1) / RangeBlock;
            NearField.Window(cn, c.CellMeters, x0, z0, x1, z1, out int i0, out int i1, out int k0, out int k1);
            rangeBlocks ??= new float[across * across][];
            float hi = float.MinValue, amp = 0, whi = float.MinValue;
            for (int k = k0; k <= k1; k++)
                for (int i = i0; i <= i1; i++)
                {
                    ref var b = ref rangeBlocks[k / RangeBlock * across + i / RangeBlock];
                    if (b == null) { b = new float[RangeBlock * RangeBlock * 3]; for (int j = 0; j < b.Length; j += 3) b[j] = float.NaN; }
                    int o = (k % RangeBlock * RangeBlock + i % RangeBlock) * 3;
                    if (float.IsNaN(b[o])) NearField.CellRange(c, k * cn + i, out _, out b[o], out b[o + 1], out _, out b[o + 2]);
                    hi = Math.Max(hi, b[o]); amp = Math.Max(amp, b[o + 1]); whi = Math.Max(whi, b[o + 2]);
                }
            hi += NearField.DetailReach * amp;
            return Math.Max(hi, whi);
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
