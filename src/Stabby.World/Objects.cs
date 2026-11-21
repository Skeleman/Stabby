using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct ObjectInstance
    {
        public int Def, Variant;
        public float X, Y, Z, Qx, Qy, Qz, Qw, Size;
        public ulong Id;
    }

    public static class ObjectIds
    {
        public static ulong Make(int chunk, int def, int ordinal) => ((ulong)(uint)chunk << 40) | ((ulong)(uint)def << 32) | (uint)ordinal;
        public static int Chunk(ulong id) => (int)(id >> 40);
        public static int Def(ulong id) => (int)((id >> 32) & 0xff);
        public static int Ordinal(ulong id) => (int)(uint)id;
    }

    public sealed class ObjectField
    {
        public const float SlopeStepM = 1f, ShoreClearM = 0.5f, RiverClearM = 1.5f, BankReachM = 3f;
        public const float BoulderMainR = 0.75f, BoulderLength = 0.25f, BoulderLift = 0.6f, BoulderSideR = 0.5f, BoulderSide = 0.4f;

        readonly GeneratedWorld w;
        readonly TerrainDetail detail;
        readonly ObjectLibrary lib;
        readonly StrataField strata;
        readonly int n, across;
        readonly float cm, worldM, contrast;
        readonly ulong stream, footStream, thinStream, boulderStream, boulderThinStream, structureStream, structureThinStream;
        readonly float[][] density;
        readonly float[] footprint;
        readonly PartShapes shapes;
        readonly float[][] seats;
        readonly ulong[] key;
        readonly int[] blockStart, blockItems;
        readonly FeatureEdit[] edits;
        readonly List<float> pax = new List<float>(), paz = new List<float>(), pbx = new List<float>(), pbz = new List<float>(), phw = new List<float>(), psurf = new List<float>();
        readonly ObjectInstance[][] chunks;

        public ObjectField(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, ObjectLibrary lib)
        {
            this.w = w; this.detail = detail; this.lib = lib;
            this.edits = edits ?? Array.Empty<FeatureEdit>();
            n = w.Config.Size; cm = w.Config.CellMeters; worldM = n * cm;
            across = WorldChunks.Across(n);
            chunks = new ObjectInstance[across * across][];
            stream = Rng.Stream(w.Config.Seed, "objects").NextU64();
            footStream = Rng.Stream(w.Config.Seed, "objects-foot").NextU64();
            thinStream = Rng.Stream(w.Config.Seed, "objects-thin").NextU64();
            boulderStream = Rng.Stream(w.Config.Seed, "boulders").NextU64();
            boulderThinStream = Rng.Stream(w.Config.Seed, "boulders-thin").NextU64();
            structureStream = Rng.Stream(w.Config.Seed, "structures").NextU64();
            structureThinStream = Rng.Stream(w.Config.Seed, "structures-thin").NextU64();
            strata = new StrataField(w.Config, w.Materials);
            contrast = w.Config.StrataContrast > 0 ? w.Config.StrataContrast : 1f;
            int defs = lib.Objects.Count, biomes = w.BiomeIds == null ? 0 : w.BiomeIds.Length;
            shapes = PartShapes.Of(lib);
            seats = new float[defs][];
            density = new float[defs][];
            footprint = new float[defs];
            key = new ulong[defs];
            float maxR = 0;
            for (int d = 0; d < defs; d++)
            {
                var def = lib.Objects[d];
                density[d] = new float[biomes];
                for (int b = 0; b < biomes; b++)
                {
                    bool ok = def.Placement.Biomes.Count == 0 || def.Placement.Biomes.Contains(w.BiomeIds[b]);
                    float scale = 1f;
                    foreach (var bd in def.Placement.BiomeDensity) if (bd.Biome == w.BiomeIds[b]) scale = bd.Scale;
                    density[d][b] = ok ? scale : 0f;
                }
                footprint[d] = def.FootprintRadius();
                key[d] = Key(def.Id);
                seats[d] = Seats(def);
                maxR = Math.Max(maxR, footprint[d] * def.Placement.SizeMaxM + def.Placement.ClusterRadiusM);
            }
            foreach (var r in w.Water.Rivers)
                for (int k = 0; k + 1 < r.X.Count; k++)
                {
                    pax.Add(r.X[k] * cm); paz.Add(r.Y[k] * cm); pbx.Add(r.X[k + 1] * cm); pbz.Add(r.Y[k + 1] * cm);
                    phw.Add(0.5f * Math.Max(r.Width[k], r.Width[k + 1])); psurf.Add(Math.Max(r.Surface[k], r.Surface[k + 1]));
                }
            blockStart = new int[n * n + 1];
            for (int pass = 0; pass < 2; pass++)
            {
                var fill = pass == 1 ? (int[])blockStart.Clone() : null;
                int[] items = pass == 1 ? new int[blockStart[n * n]] : null;
                for (int e = 0; e < this.edits.Length; e++)
                {
                    var f = this.edits[e];
                    Mark(f.MinX - maxR, f.MinZ - maxR, f.MaxX + maxR, f.MaxZ + maxR, ~e, fill, items);
                }
                for (int p = 0; p < pax.Count; p++)
                {
                    float pad = 2 * phw[p] + maxR + RiverClearM + BankReachM;
                    Mark(Math.Min(pax[p], pbx[p]) - pad, Math.Min(paz[p], pbz[p]) - pad, Math.Max(pax[p], pbx[p]) + pad, Math.Max(paz[p], pbz[p]) + pad, p, fill, items);
                }
                if (pass == 0) for (int i = n * n; i > 0; i--) blockStart[i] = blockStart[i - 1];
                if (pass == 0) { blockStart[0] = 0; for (int i = 1; i <= n * n; i++) blockStart[i] += blockStart[i - 1]; }
                else blockItems = items;
            }
        }

        void Mark(float x0, float z0, float x1, float z1, int item, int[] fill, int[] items)
        {
            int a = Math.Max(0, (int)Math.Floor(x0 / cm)), b = Math.Min(n - 1, (int)Math.Ceiling(x1 / cm));
            int c = Math.Max(0, (int)Math.Floor(z0 / cm)), d = Math.Min(n - 1, (int)Math.Ceiling(z1 / cm));
            for (int gz = c; gz <= d; gz++)
                for (int gx = a; gx <= b; gx++)
                {
                    int i = gz * n + gx;
                    if (fill == null) blockStart[i]++;
                    else items[fill[i]++] = item;
                }
        }

        static ulong Key(string id)
        {
            ulong h = 14695981039346656037UL;
            foreach (char ch in id) { h ^= ch; h *= 1099511628211UL; }
            return Rng.Mix(h);
        }

        int Cell(float m) => Math.Min(n - 1, (int)(m / cm + 0.5f));

        public static float GroundAt(TerrainDetail d, float cellMeters, float x, float z) => d.HeightAt(x / cellMeters, z / cellMeters);

        public int ChunksAcross => across;
        public ObjectLibrary Library => lib;
        public float Footprint(int def) => footprint[def];
        public float ChunkMeters => WorldChunks.Size * cm;
        public void ColumnsOf(ObjectInstance o, List<ObjectColumn> list) => AddColumns(lib, shapes, o, list);

        public ObjectInstance[] Chunk(int cx, int cz)
        {
            int c = cz * across + cx;
            return chunks[c] ?? (chunks[c] = Place(cx, cz));
        }

        public ObjectInstance[] Place(int cx, int cz, bool solidOnly = false)
        {
            var list = new List<ObjectInstance>();
            PlaceInto(cx, cz, solidOnly, list, null);
            return list.ToArray();
        }

        public FeatureEdit[] Boulders()
        {
            var list = new List<FeatureEdit>();
            var scratch = new List<ObjectInstance>();
            for (int cz = 0; cz < across; cz++)
                for (int cx = 0; cx < across; cx++)
                    PlaceInto(cx, cz, false, scratch, list);
            return list.ToArray();
        }

        void PlaceInto(int cx, int cz, bool solidOnly, List<ObjectInstance> list, List<FeatureEdit> boulders)
        {
            float span = WorldChunks.Size * cm;
            float x0 = cx * span, z0 = cz * span;
            float sx = Math.Min(span, worldM - x0), sz = Math.Min(span, worldM - z0);
            if (sx <= 0 || sz <= 0) return;
            int chunk = cz * across + cx;
            ulong chunkKey = Rng.Mix((ulong)chunk + 1);
            for (int d = 0; d < lib.Objects.Count; d++)
            {
                int ordinal = 0;
                var pl = lib.Objects[d].Placement;
                if (pl.PerHectare <= 0 || (solidOnly && !AnySolid(d)) || (boulders != null && pl.EditMinSizeM <= 0)) continue;
                bool foot = pl.FootRiseM > 0, built = seats[d] != null;
                var rng = new Rng(Rng.Mix((boulders != null ? boulderStream : built ? structureStream : foot ? footStream : stream) ^ chunkKey ^ key[d]));
                var thin = new Rng(Rng.Mix((boulders != null ? boulderThinStream : built ? structureThinStream : thinStream) ^ chunkKey ^ key[d]));
                float expected = pl.PerHectare * sx * sz / 10000f / pl.ClusterCount;
                int clusters = (int)expected + (rng.NextFloat() < expected - (int)expected ? 1 : 0);
                for (int k = 0; k < clusters; k++)
                {
                    float ax = x0 + rng.NextFloat() * sx, az = z0 + rng.NextFloat() * sz;
                    bool open = Open(d, ax, az) && (!foot || Foot(pl, ax, az));
                    if (foot && !open) continue;
                    for (int m = 0; m < pl.ClusterCount; m++)
                    {
                        float ox = rng.Range(-1f, 1f), oz = rng.Range(-1f, 1f), t = rng.NextFloat(), qa = rng.Range(-1f, 1f), qb = rng.Range(-1f, 1f);
                        int variant = (int)(rng.NextU32() % (uint)lib.Objects[d].Variants);
                        if (!open) continue;
                        float px = m == 0 ? ax : ax + ox * pl.ClusterRadiusM, pz = m == 0 ? az : az + oz * pl.ClusterRadiusM;
                        if (px < x0 || pz < z0 || px >= x0 + sx || pz >= z0 + sz) continue;
                        if (Density(d, px, pz) < 1f && thin.NextFloat() >= Density(d, px, pz)) continue;
                        float size = pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * t * t;
                        if ((pl.EditMinSizeM > 0 && size >= pl.EditMinSizeM) != (boulders != null)) continue;
                        if (!Accept(d, px, pz, size, qa, qb, out var o)) continue;
                        o.Variant = variant;
                        o.Id = ObjectIds.Make(chunk, d, ordinal++);
                        if (boulders == null) list.Add(o);
                        else AddBoulder(o, footprint[d] * size, qa, qb, x0, z0, sx, sz, boulders);
                    }
                }
            }
        }

        void AddBoulder(ObjectInstance o, float reach, float qa, float qb, float x0, float z0, float sx, float sz, List<FeatureEdit> boulders)
        {
            float ql = (float)Math.Sqrt(qa * qa + qb * qb);
            float yw = ql > 1e-4f ? qa / ql : 1f, yy = ql > 1e-4f ? qb / ql : 0f;
            float ux = yw * yw - yy * yy, uz = 2 * yw * yy, side = (o.Variant & 1) == 0 ? 1f : -1f;
            float r = BoulderMainR * reach, h = BoulderLength * reach, y = o.Y + BoulderLift * r;
            float r2 = BoulderSideR * reach, sx2 = o.X - uz * side * BoulderSide * reach, sz2 = o.Z + ux * side * BoulderSide * reach;
            var a = new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Boulder, R = r, Ax = o.X - ux * h, Ay = y, Az = o.Z - uz * h, Bx = o.X + ux * h, By = y, Bz = o.Z + uz * h };
            var b = new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Boulder, R = r2, Ax = sx2, Ay = o.Y + BoulderLift * r2, Az = sz2, Bx = sx2, By = o.Y + BoulderLift * r2, Bz = sz2 };
            if (!Within(a, x0, z0, sx, sz) || !Within(b, x0, z0, sx, sz) || Wet(a) || Wet(b)) return;
            boulders.Add(a);
            boulders.Add(b);
        }

        bool Wet(FeatureEdit e)
        {
            int i = Cell(0.5f * (e.MinZ + e.MaxZ)) * n + Cell(0.5f * (e.MinX + e.MaxX));
            return w.Sea[i] || (w.Water != null && w.Water.LakeId[i] >= 0);
        }

        static bool Within(FeatureEdit e, float x0, float z0, float sx, float sz) => e.MinX >= x0 && e.MaxX < x0 + sx && e.MinZ >= z0 && e.MaxZ < z0 + sz;

        float Density(int d, float x, float z)
        {
            if (x < 0 || z < 0 || x >= worldM || z >= worldM) return 0f;
            int b = w.Biome[Cell(z) * n + Cell(x)];
            return b >= 0 ? density[d][b] : 0f;
        }

        public bool Foot(ObjectPlacement pl, float x, float z)
        {
            int gx = Cell(x), gz = Cell(z);
            float h0 = detail.Height(gz * n + gx), span = 0;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ax = Math.Max(0, Math.Min(n - 1, gx + dx)), az = Math.Max(0, Math.Min(n - 1, gz + dz));
                    span = Math.Max(span, Math.Abs(detail.Height(az * n + ax) - h0));
                }
            float r = pl.FootReachM;
            if (span / cm * r * 1.5f + detail.Amplitude(gz * n + gx) < pl.FootRiseM) return false;
            float gxs = detail.Height(gz * n + Math.Min(n - 1, gx + 1)) - detail.Height(gz * n + Math.Max(0, gx - 1));
            float gzs = detail.Height(Math.Min(n - 1, gz + 1) * n + gx) - detail.Height(Math.Max(0, gz - 1) * n + gx);
            float gl = (float)Math.Sqrt(gxs * gxs + gzs * gzs);
            float y = GroundAt(detail, cm, x, z), diag = r * 0.70710678f, best = 0, below = 0;
            if (gl > 1e-3f && GroundAt(detail, cm, x + gxs / gl * r, z + gzs / gl * r) - y < 0.4f * pl.FootRiseM) return false;
            for (int k = 0; k < 8; k++)
            {
                float ux = k == 0 ? r : k == 1 ? -r : k < 4 ? 0 : k == 4 || k == 6 ? diag : -diag;
                float uz = k == 2 ? r : k == 3 ? -r : k < 2 ? 0 : k == 4 || k == 5 ? diag : -diag;
                float rise = GroundAt(detail, cm, x + ux, z + uz) - y;
                if (rise > best) { best = rise; below = y - GroundAt(detail, cm, x - ux, z - uz); }
            }
            return best >= pl.FootRiseM && below < 0.5f * best;
        }

        bool Open(int d, float x, float z)
        {
            if (x < 0 || z < 0 || x >= worldM || z >= worldM) return false;
            int b = w.Biome[Cell(z) * n + Cell(x)];
            return b >= 0 && density[d][b] > 0;
        }

        bool Accept(int d, float x, float z, float size, float qa, float qb, out ObjectInstance o)
        {
            o = default;
            if (!Open(d, x, z)) return false;
            var pl = lib.Objects[d].Placement;
            int gx = Cell(x), gz = Cell(z), i = gz * n + gx;
            float r = footprint[d] * size;
            float y = GroundAt(detail, cm, x, z);
            float s = SlopeStepM;
            float gxs = (GroundAt(detail, cm, x + s, z) - GroundAt(detail, cm, x - s, z)) / (2 * s);
            float gzs = (GroundAt(detail, cm, x, z + s) - GroundAt(detail, cm, x, z - s)) / (2 * s);
            float slope = (float)Math.Sqrt(gxs * gxs + gzs * gzs);
            if (slope < pl.SlopeMin || slope > pl.SlopeMax) return false;
            float hard = strata.Hardness(i, y) / contrast;
            if (hard < pl.HardnessMin || hard > pl.HardnessMax) return false;
            if (!Clear(i, gx, gz, x, y, z, r)) return false;
            float nx = -gxs * pl.AlignToSlope, nz = -gzs * pl.AlignToSlope, nl = (float)Math.Sqrt(nx * nx + 1 + nz * nz);
            nx /= nl; nz /= nl;
            float ny = 1f / nl;
            float tx = nz, tz = -nx, tw = 1 + ny, tl = (float)Math.Sqrt(tx * tx + tz * tz + tw * tw);
            tx /= tl; tz /= tl; tw /= tl;
            float ql = (float)Math.Sqrt(qa * qa + qb * qb);
            float yw = ql > 1e-4f ? qa / ql : 1f, yy = ql > 1e-4f ? qb / ql : 0f;
            o = new ObjectInstance
            {
                Def = d, X = x, Z = z, Size = size,
                Y = y - pl.Sink * size - slope * r * 0.5f * (1 - pl.AlignToSlope),
                Qx = tx * yw - tz * yy, Qy = tw * yy, Qz = tz * yw + tx * yy, Qw = tw * yw,
            };
            if (seats[d] == null) return true;
            SeatRange(detail, cm, seats[d], o, out float lo, out float hi);
            if (hi - lo > Structures.SeatM) return false;
            o.Y = lo - pl.Sink * size;
            return true;
        }

        public static float[] Seats(ObjectDef def)
        {
            List<float> pts = null;
            foreach (var p in def.Parts)
            {
                if (!ObjectPart.IsStructure(p.Shape)) continue;
                pts = pts ?? new List<float>();
                var seat = Structures.Seat(p.Shape);
                for (int k = 0; k < seat.Length; k += 2) { pts.Add(p.OffsetX + seat[k] * p.ScaleX); pts.Add(p.OffsetZ + seat[k + 1] * p.ScaleZ); }
            }
            return pts?.ToArray();
        }

        public static void SeatRange(TerrainDetail detail, float cellMeters, float[] seat, ObjectInstance o, out float lo, out float hi)
        {
            lo = float.MaxValue; hi = float.MinValue;
            for (int k = 0; k < seat.Length; k += 2)
            {
                Rotate(o, seat[k] * o.Size, 0, seat[k + 1] * o.Size, out float rx, out _, out float rz);
                float g = GroundAt(detail, cellMeters, o.X + rx, o.Z + rz);
                lo = Math.Min(lo, g); hi = Math.Max(hi, g);
            }
        }

        bool Clear(int i, int gx, int gz, float x, float y, float z, float r)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ax = gx + dx, az = gz + dz;
                    if (ax < 0 || az < 0 || ax >= n || az >= n) continue;
                    int j = az * n + ax;
                    if (w.Sea[j] && (j == i || y < ShoreClearM)) return false;
                    int lake = w.Water.LakeId[j];
                    if (lake >= 0 && (j == i || y < w.Water.Lakes[lake].Level + ShoreClearM)) return false;
                }
            for (int k = blockStart[i]; k < blockStart[i + 1]; k++)
            {
                int item = blockItems[k];
                if (item < 0)
                {
                    var f = edits[~item];
                    if (x + r >= f.MinX && x - r <= f.MaxX && z + r >= f.MinZ && z - r <= f.MaxZ) return false;
                    continue;
                }
                float dist = Distance(item, x, z), hw = phw[item];
                if (dist < hw + r + RiverClearM) return false;
                if (dist < 2 * hw + r + BankReachM && y < psurf[item] + ShoreClearM) return false;
            }
            return true;
        }

        float Distance(int p, float x, float z)
        {
            float ax = pax[p], az = paz[p], bx = pbx[p] - ax, bz = pbz[p] - az;
            float l2 = bx * bx + bz * bz;
            float t = l2 > 0 ? Math.Max(0, Math.Min(1, ((x - ax) * bx + (z - az) * bz) / l2)) : 0;
            float ex = x - ax - bx * t, ez = z - az - bz * t;
            return (float)Math.Sqrt(ex * ex + ez * ez);
        }

        bool AnySolid(int d)
        {
            var def = lib.Objects[d];
            foreach (var p in def.Parts) if (lib.PartSolid(def, p)) return true;
            return false;
        }

        public List<ObjectColumn> Columns()
        {
            var list = new List<ObjectColumn>();
            var scratch = new List<ObjectInstance>();
            for (int cz = 0; cz < across; cz++)
                for (int cx = 0; cx < across; cx++)
                {
                    var held = chunks[cz * across + cx];
                    if (held != null) { foreach (var o in held) AddColumns(lib, shapes, o, list); continue; }
                    scratch.Clear();
                    PlaceInto(cx, cz, true, scratch, null);
                    foreach (var o in scratch) AddColumns(lib, shapes, o, list);
                }
            return list;
        }

        public static void AddColumns(ObjectLibrary lib, PartShapes shapes, ObjectInstance o, List<ObjectColumn> list)
        {
            var def = lib.Objects[o.Def];
            for (int pi = 0; pi < def.Parts.Count; pi++)
            {
                var p = def.Parts[pi];
                if (!lib.PartSolid(def, p)) continue;
                float tall = p.ScaleY * o.Size * shapes.Heights[o.Def][pi][o.Variant];
                var local = shapes.Columns[o.Def][pi];
                if (local != null)
                {
                    float rs = Math.Min(p.ScaleX, p.ScaleZ) * o.Size;
                    foreach (var c in local[o.Variant])
                    {
                        Rotate(o, (p.OffsetX + c.X * p.ScaleX) * o.Size, (p.OffsetY + c.Bottom * p.ScaleY) * o.Size, (p.OffsetZ + c.Z * p.ScaleZ) * o.Size, out float cx, out float cy, out float cz);
                        list.Add(new ObjectColumn { X = cx + o.X, Z = cz + o.Z, R = c.R * rs, Bottom = cy + o.Y, Top = cy + o.Y + (c.Top - c.Bottom) * p.ScaleY * o.Size });
                    }
                }
                else if (p.Shape == ObjectPart.Log)
                {
                    Rotate(o, p.OffsetX * o.Size, (p.OffsetY + 0.5f * p.ScaleY) * o.Size, p.OffsetZ * o.Size, out float cx, out float cy, out float cz);
                    cx += o.X; cy += o.Y; cz += o.Z;
                    float len = p.ScaleX * o.Size, rad = 0.5f * Math.Max(p.ScaleY, p.ScaleZ) * o.Size;
                    Rotate(o, 1, 0, 0, out float ux, out float uy, out float uz);
                    int count = Math.Max(1, Math.Min(16, (int)Math.Ceiling(len / (2 * rad))));
                    for (int k = 0; k < count; k++)
                    {
                        float t = ((k + 0.5f) / count - 0.5f) * len;
                        float py = cy + uy * t;
                        list.Add(new ObjectColumn { X = cx + ux * t, Z = cz + uz * t, R = rad, Bottom = py - rad, Top = py + rad });
                    }
                }
                else
                {
                    Rotate(o, p.OffsetX * o.Size, p.OffsetY * o.Size, p.OffsetZ * o.Size, out float cx, out float cy, out float cz);
                    float rad = ObjectPart.IsFoliage(p.Shape) ? ObjectFoliage.TrunkRadius * 1.25f * Math.Min(p.ScaleX, p.ScaleZ) * o.Size : 0.5f * Math.Min(p.ScaleX, p.ScaleZ) * o.Size * 0.85f;
                    list.Add(new ObjectColumn { X = cx + o.X, Z = cz + o.Z, R = rad, Bottom = cy + o.Y, Top = cy + o.Y + tall });
                }
            }
        }

        public static void Rotate(ObjectInstance q, float vx, float vy, float vz, out float rx, out float ry, out float rz)
        {
            float tx = 2 * (q.Qy * vz - q.Qz * vy), ty = 2 * (q.Qz * vx - q.Qx * vz), tz = 2 * (q.Qx * vy - q.Qy * vx);
            rx = vx + q.Qw * tx + (q.Qy * tz - q.Qz * ty);
            ry = vy + q.Qw * ty + (q.Qz * tx - q.Qx * tz);
            rz = vz + q.Qw * tz + (q.Qx * ty - q.Qy * tx);
        }
    }

    public sealed class PartShapes
    {
        public float[][][] Heights;
        public ObjectColumn[][][][] Columns;

        public static PartShapes Of(ObjectLibrary lib)
        {
            var s = new PartShapes { Heights = new float[lib.Objects.Count][][], Columns = new ObjectColumn[lib.Objects.Count][][][] };
            for (int d = 0; d < lib.Objects.Count; d++)
            {
                var def = lib.Objects[d];
                s.Heights[d] = new float[def.Parts.Count][];
                s.Columns[d] = new ObjectColumn[def.Parts.Count][][];
                for (int p = 0; p < def.Parts.Count; p++)
                {
                    int shape = def.Parts[p].Shape;
                    s.Heights[d][p] = new float[def.Variants];
                    if (ObjectPart.IsStructure(shape)) s.Columns[d][p] = new ObjectColumn[def.Variants][];
                    for (int v = 0; v < def.Variants; v++)
                    {
                        ulong k = ObjectMeshes.Key(def.Id, p, v);
                        s.Heights[d][p][v] = shape == ObjectPart.Rock ? ObjectMeshes.RockHeight(k) : 1f;
                        if (s.Columns[d][p] != null) s.Columns[d][p][v] = Structures.Layout(shape, k).Columns.ToArray();
                    }
                }
            }
            return s;
        }
    }

    public struct ObjectColumn
    {
        public float X, Z, R, Bottom, Top;
    }

    public interface IColumnTable
    {
        float CellM { get; }
        float MaxR { get; }
        int Mask { get; }
        int Start(int bucket);
        ObjectColumn Column(int k);
    }

    public struct NoColumns : IColumnTable
    {
        public float CellM => 1;
        public float MaxR => 0;
        public int Mask => -1;
        public int Start(int bucket) => 0;
        public ObjectColumn Column(int k) => default;
    }

    public struct ColumnArrays : IColumnTable
    {
        public const float DefaultCellM = 8f;
        public float Cell, Max;
        public int[] Starts;
        public ObjectColumn[] Items;

        public float CellM => Cell;
        public float MaxR => Max;
        public int Mask => Starts == null ? -1 : Starts.Length - 2;
        public int Start(int bucket) => Starts[bucket];
        public ObjectColumn Column(int k) => Items[k];

        public int Kill(ObjectField field, ObjectInstance o)
        {
            var cols = new List<ObjectColumn>();
            field.ColumnsOf(o, cols);
            int killed = 0;
            foreach (var c in cols)
            {
                int k = Obstacles.Find(this, c);
                if (k >= 0) { Items[k] = Obstacles.Dead; killed++; }
            }
            return killed;
        }

        public static ColumnArrays Build(List<ObjectColumn> columns, float cellM = DefaultCellM)
        {
            int buckets = 64;
            while (buckets < columns.Count * 2 && buckets < (1 << 24)) buckets <<= 1;
            var t = new ColumnArrays { Cell = cellM, Starts = new int[buckets + 1], Items = new ObjectColumn[columns.Count] };
            var at = new int[columns.Count];
            for (int k = 0; k < columns.Count; k++)
            {
                var c = columns[k];
                t.Max = Math.Max(t.Max, c.R);
                at[k] = Obstacles.Bucket((int)Math.Floor(c.X / cellM), (int)Math.Floor(c.Z / cellM), buckets - 1);
                t.Starts[at[k] + 1]++;
            }
            for (int b = 0; b < buckets; b++) t.Starts[b + 1] += t.Starts[b];
            var fill = (int[])t.Starts.Clone();
            for (int k = 0; k < columns.Count; k++) t.Items[fill[at[k]]++] = columns[k];
            return t;
        }
    }

    public static class Obstacles
    {
        public static readonly ObjectColumn Dead = new ObjectColumn { Bottom = float.PositiveInfinity, Top = float.NegativeInfinity };

        public static int Bucket(int cx, int cz, int mask) => (int)(((uint)cx * 73856093u) ^ ((uint)cz * 19349663u)) & mask;

        public static bool Same(ObjectColumn a, ObjectColumn b) => a.X == b.X && a.Z == b.Z && a.R == b.R && a.Bottom == b.Bottom && a.Top == b.Top;

        public static int Find<T>(T t, ObjectColumn c) where T : struct, IColumnTable
        {
            int mask = t.Mask;
            if (mask < 0) return -1;
            int b = Bucket((int)Math.Floor(c.X / t.CellM), (int)Math.Floor(c.Z / t.CellM), mask);
            for (int k = t.Start(b), e = t.Start(b + 1); k < e; k++)
                if (Same(t.Column(k), c)) return k;
            return -1;
        }

        public static float Top<T>(T t, float x, float z, float r, float bottom, float top) where T : struct, IColumnTable
        {
            float best = float.NegativeInfinity;
            int mask = t.Mask;
            if (mask < 0) return best;
            float reach = r + t.MaxR, cell = t.CellM;
            int x0 = (int)Math.Floor((x - reach) / cell), x1 = (int)Math.Floor((x + reach) / cell);
            int z0 = (int)Math.Floor((z - reach) / cell), z1 = (int)Math.Floor((z + reach) / cell);
            for (int cz = z0; cz <= z1; cz++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    int b = Bucket(cx, cz, mask);
                    for (int k = t.Start(b), e = t.Start(b + 1); k < e; k++)
                    {
                        var c = t.Column(k);
                        float dx = c.X - x, dz = c.Z - z, rr = r + c.R;
                        if (dx * dx + dz * dz < rr * rr && c.Bottom < top && c.Top > bottom && c.Top > best) best = c.Top;
                    }
                }
            return best;
        }
    }
}
