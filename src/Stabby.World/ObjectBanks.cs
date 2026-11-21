using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static class BankSites
    {
        public const int Reed = 0, Bar = 1, Drift = 2, Rapid = 3;
        public const float LakeShare = 0.5f, ReachPadM = 0.5f;
        public static readonly string[] Names = { "reed", "bar", "drift", "rapid" };
        public static readonly float[] Below = { 0.4f, 0.15f, 0.3f, 1f }, Above = { 0.6f, 0.9f, 1.6f, 0.8f };

        public static int Of(string name) => Array.IndexOf(Names, name);
        public static bool InBand(int site, float rise) => rise >= -Below[site] && rise <= Above[site];

        public static bool Nearest<T>(T c, float x, float z, out RiverPiece q, out float d, out float t) where T : struct, INearCells
        {
            q = default; d = 0; t = 0;
            int cell = NearField.CellOf(x, z, c.CellMeters, c.N);
            float best = float.MaxValue;
            for (int k = c.PieceStart(cell), e = c.PieceStart(cell + 1); k < e; k++)
            {
                var p = c.Piece(c.PieceAt(k));
                NearField.Project(p, x, z, out float pd, out float pt);
                float m = pd - (p.Wa + (p.Wb - p.Wa) * pt);
                if (m < best) { best = m; q = p; d = pd; t = pt; }
            }
            return best < float.MaxValue;
        }
    }

    public sealed partial class ObjectField
    {
        static void BankIndex(TerrainDetail detail, ObjectLibrary lib, int across, float cm, out int[] site, out int[] start, out int[] items)
        {
            site = new int[lib.Objects.Count];
            float reach = 0;
            for (int d = 0; d < site.Length; d++)
            {
                var pl = lib.Objects[d].Placement;
                site[d] = pl.Bank == null ? -1 : BankSites.Of(pl.Bank);
                if (site[d] >= 0) reach = Math.Max(reach, pl.ClusterRadiusM);
            }
            var c = detail.Cells;
            float span = WorldChunks.Size * cm;
            start = new int[across * across + 1];
            items = null;
            for (int pass = 0; pass < 2; pass++)
            {
                var fill = pass == 1 ? (int[])start.Clone() : null;
                if (pass == 1) items = new int[start[across * across]];
                for (int p = 0; p < c.PieceCount; p++)
                {
                    var q = c.Piece(p);
                    float w = Math.Max(q.Wa, q.Wb), pad = w + NearField.Bank(w) + reach + 1f;
                    int x0 = Math.Max(0, (int)Math.Floor((Math.Min(q.Ax, q.Bx) - pad) / span)), x1 = Math.Min(across - 1, (int)Math.Floor((Math.Max(q.Ax, q.Bx) + pad) / span));
                    int z0 = Math.Max(0, (int)Math.Floor((Math.Min(q.Az, q.Bz) - pad) / span)), z1 = Math.Min(across - 1, (int)Math.Floor((Math.Max(q.Az, q.Bz) + pad) / span));
                    for (int cz = z0; cz <= z1; cz++)
                        for (int cx = x0; cx <= x1; cx++)
                        {
                            int i = cz * across + cx;
                            if (fill == null) start[i + 1]++;
                            else items[fill[i]++] = p;
                        }
                }
                if (pass == 0) for (int i = 0; i < across * across; i++) start[i + 1] += start[i];
            }
        }

        void PlaceBank(int d, int chunk, ulong chunkKey, float x0, float z0, float sx, float sz, List<ObjectInstance> list)
        {
            var def = lib.Objects[d];
            var pl = def.Placement;
            var c = detail.Cells;
            int site = bankSite[d], ordinal = 0;
            for (int k = bankStart[chunk]; k < bankStart[chunk + 1]; k++)
            {
                int p = bankItems[k];
                var q = c.Piece(p);
                float ex = q.Bx - q.Ax, ez = q.Bz - q.Az, el = (float)Math.Sqrt(ex * ex + ez * ez);
                if (el < 1e-3f) continue;
                float fx = ex / el, fz = ez / el;
                var rng = new Rng(Rng.Mix(bankStream ^ chunkKey ^ key[d] ^ Rng.Mix((ulong)p + 1)));
                float expected = pl.PerHundredM * el / 100f / pl.ClusterCount;
                int clusters = (int)expected + (rng.NextFloat() < expected - (int)expected ? 1 : 0);
                for (int m0 = 0; m0 < clusters; m0++)
                {
                    float t = rng.NextFloat(), side = rng.NextFloat() < 0.5f ? -1f : 1f, out0 = rng.NextFloat();
                    float w = q.Wa + (q.Wb - q.Wa) * t, off = side * (0.5f * w + out0 * (0.5f * w + NearField.Bank(w)));
                    float ax = q.Ax + ex * t - fz * off, az = q.Az + ez * t + fx * off;
                    Members(d, site, pl, ref rng, ax, az, fx, fz, chunk, x0, z0, sx, sz, ref ordinal, list);
                }
            }
            if (site == BankSites.Reed) LakeReeds(d, pl, chunk, x0, z0, sx, sz, ref ordinal, list);
        }

        void Members(int d, int site, ObjectPlacement pl, ref Rng rng, float ax, float az, float fx, float fz, int chunk, float x0, float z0, float sx, float sz, ref int ordinal, List<ObjectInstance> list)
        {
            for (int m = 0; m < pl.ClusterCount; m++)
            {
                float ox = rng.Range(-1f, 1f), oz = rng.Range(-1f, 1f), t = rng.NextFloat(), qa = rng.Range(-1f, 1f), qb = rng.Range(-1f, 1f), turn = rng.NextFloat();
                int variant = (int)(rng.NextU32() % (uint)lib.Objects[d].Variants);
                float px = m == 0 ? ax : ax + ox * pl.ClusterRadiusM, pz = m == 0 ? az : az + oz * pl.ClusterRadiusM;
                if (px < x0 || pz < z0 || px >= x0 + sx || pz >= z0 + sz) continue;
                float size = pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * t * t;
                float ground = GroundAt(detail, cm, px, pz);
                if (fx == 0 && fz == 0 ? !LakeSite(px, pz, ground) : !RiverSite(site, px, pz, ground, size)) continue;
                if (site == BankSites.Drift)
                {
                    float ux = fx + qa * 0.6f * -fz, uz = fz + qa * 0.6f * fx;
                    if (turn < 0.2f) { float s = ux; ux = -uz; uz = s; }
                    float ul = (float)Math.Sqrt(ux * ux + uz * uz);
                    if (ul < 1e-3f) { ux = 1; uz = 0; ul = 1; }
                    ux /= ul; uz /= ul;
                    qa = (float)Math.Sqrt(Math.Max(0, 0.5f * (1 + ux)));
                    qb = (uz > 0 ? -1f : 1f) * (float)Math.Sqrt(Math.Max(0, 0.5f * (1 - ux)));
                }
                if (!Accept(d, px, pz, size, qa, qb, out var o, true)) continue;
                o.Variant = variant;
                o.Id = ObjectIds.Make(chunk, d, ordinal++);
                list.Add(o);
            }
        }

        bool RiverSite(int site, float x, float z, float ground, float size)
        {
            if (!BankSites.Nearest(detail.Cells, x, z, out var q, out float d, out float t)) return false;
            float w = q.Wa + (q.Wb - q.Wa) * t, s = q.Sa + (q.Sb - q.Sa) * t, bank = NearField.Bank(w);
            if (!BankSites.InBand(site, ground - s)) return false;
            float ex = q.Bx - q.Ax, ez = q.Bz - q.Az, el = (float)Math.Sqrt(ex * ex + ez * ez);
            float lat = el > 1e-3f ? ((z - q.Az) * ex - (x - q.Ax) * ez) / el : 0;
            float bend = q.Ka + (q.Kb - q.Ka) * t, inner = Math.Max(-1f, Math.Min(1f, bend * lat / w));
            float rapid = q.Ra + (q.Rb - q.Ra) * t, plunge = q.Pa + (q.Pb - q.Pa) * t;
            bool bar = inner > 0.3f && d < w + bank;
            switch (site)
            {
                case BankSites.Reed: return rapid < 0.1f && plunge <= 0 && inner > 0.12f && d >= 0.75f * w && d <= w + 0.6f * bank;
                case BankSites.Bar: return bar;
                case BankSites.Drift: return bar || (inner < -0.3f && d >= w - 0.5f && d <= w + 1.5f);
                default: return ground - s >= -0.45f * size && d >= 0.6f * w && ((rapid > 0.3f && d < w + 1f) || (plunge > 0 && d < w + 1.5f));
            }
        }

        bool LakeSite(float x, float z, float ground)
        {
            int lake = MarginLake(Cell(x), Cell(z));
            return lake >= 0 && BankSites.InBand(BankSites.Reed, ground - w.Water.Lakes[lake].Level);
        }

        int MarginLake(int gx, int gz)
        {
            int own = w.Water.LakeId[gz * n + gx], found = -1;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ax = gx + dx, az = gz + dz;
                    if (ax < 0 || az < 0 || ax >= n || az >= n) continue;
                    int j = az * n + ax, l = w.Water.LakeId[j];
                    if (l != own && !w.Sea[j]) found = l >= 0 ? l : own;
                }
            return found;
        }

        void LakeReeds(int d, ObjectPlacement pl, int chunk, float x0, float z0, float sx, float sz, ref int ordinal, List<ObjectInstance> list)
        {
            int gx0 = Math.Max(0, (int)Math.Floor(x0 / cm)), gx1 = Math.Min(n - 1, (int)Math.Ceiling((x0 + sx) / cm));
            int gz0 = Math.Max(0, (int)Math.Floor(z0 / cm)), gz1 = Math.Min(n - 1, (int)Math.Ceiling((z0 + sz) / cm));
            float expected = BankSites.LakeShare * pl.PerHundredM * cm / 100f / pl.ClusterCount;
            for (int gz = gz0; gz <= gz1; gz++)
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    int i = gz * n + gx;
                    if (w.Sea[i] || MarginLake(gx, gz) < 0) continue;
                    var rng = new Rng(Rng.Mix(lakeReedStream ^ key[d] ^ Rng.Mix((ulong)i + 1)));
                    int clusters = (int)expected + (rng.NextFloat() < expected - (int)expected ? 1 : 0);
                    for (int k = 0; k < clusters; k++)
                    {
                        float ax = (gx + rng.Range(-0.5f, 0.5f)) * cm, az = (gz + rng.Range(-0.5f, 0.5f)) * cm;
                        Members(d, BankSites.Reed, pl, ref rng, ax, az, 0, 0, chunk, x0, z0, sx, sz, ref ordinal, list);
                    }
                }
        }
    }
}
