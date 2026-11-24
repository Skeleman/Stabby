using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

    public sealed partial class ObjectField
    {
        public const float SlopeStepM = 1f, ShoreClearM = 0.5f, RiverClearM = 1.5f, BankReachM = 3f, CrustThin = 0.85f;
        public const float BoulderMainR = 0.75f, BoulderLength = 0.25f, BoulderLift = 0.6f, BoulderSideR = 0.5f, BoulderSide = 0.4f;

        readonly GeneratedWorld w;
        readonly TerrainDetail detail;
        readonly ObjectLibrary lib;
        readonly StrataField strata;
        readonly int n, across;
        readonly float cm, worldM, contrast;
        readonly ulong stream, footStream, thinStream, boulderStream, boulderThinStream, structureStream, structureThinStream, chainStream, bankStream, lakeReedStream, sinterStream;
        readonly float[][] density;
        readonly float[] footprint;
        readonly PartShapes shapes;
        readonly float[][] seats;
        readonly ulong[] key;
        readonly int[] blockStart, blockItems, bankSite, bankStart, bankItems;
        readonly FeatureEdit[] edits;
        readonly List<float> pax = new List<float>(), paz = new List<float>(), pbx = new List<float>(), pbz = new List<float>(), phw = new List<float>(), psurf = new List<float>();
        readonly ObjectInstance[][] chunks;
        readonly object[] chunkGates;
        int chunkComputes;

        public ObjectField(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, ObjectLibrary lib)
        {
            this.w = w; this.detail = detail; this.lib = lib;
            this.edits = edits ?? Array.Empty<FeatureEdit>();
            n = w.Config.Size; cm = w.Config.CellMeters; worldM = n * cm;
            across = WorldChunks.Across(n);
            chunks = new ObjectInstance[across * across][];
            chunkGates = new object[across * across];
            stream = Rng.Stream(w.Config.Seed, "objects").NextU64();
            footStream = Rng.Stream(w.Config.Seed, "objects-foot").NextU64();
            thinStream = Rng.Stream(w.Config.Seed, "objects-thin").NextU64();
            boulderStream = Rng.Stream(w.Config.Seed, "boulders").NextU64();
            boulderThinStream = Rng.Stream(w.Config.Seed, "boulders-thin").NextU64();
            structureStream = Rng.Stream(w.Config.Seed, "structures").NextU64();
            structureThinStream = Rng.Stream(w.Config.Seed, "structures-thin").NextU64();
            chainStream = Rng.Stream(w.Config.Seed, "structures-chain").NextU64();
            bankStream = Rng.Stream(w.Config.Seed, "objects-bank").NextU64();
            lakeReedStream = Rng.Stream(w.Config.Seed, "objects-lake-reeds").NextU64();
            sinterStream = Rng.Stream(w.Config.Seed, "objects-sinter").NextU64();
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
            BankIndex(detail, lib, across, cm, out bankSite, out bankStart, out bankItems);
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
                    float ext = f.Crusts ? f.CrustExtent : 0f;
                    Mark(Math.Min(f.MinX, f.Ax - ext) - maxR, Math.Min(f.MinZ, f.Az - ext) - maxR, Math.Max(f.MaxX, f.Ax + ext) + maxR, Math.Max(f.MaxZ, f.Az + ext) + maxR, ~e, fill, items);
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
            InitSites();
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
        public float GroundAt(float x, float z) => GroundAt(detail, cm, x, z);

        public int ChunksAcross => across;
        public ObjectLibrary Library => lib;
        public float Footprint(int def) => footprint[def];
        public float ChunkMeters => WorldChunks.Size * cm;
        public void ColumnsOf(ObjectInstance o, List<ObjectColumn> list) => AddColumns(lib, shapes, o, list);
        public void ContactsOf(ObjectInstance o, List<ObjectColumn> list) => AddContacts(lib, shapes, o, list);
        public void LushOf(ObjectInstance o, List<ObjectColumn> list) => AddLush(lib, shapes, o, list);
        public void LightsOf(ObjectInstance o, List<StructureLight> list) => AddLights(lib, shapes, o, list);
        public void PartContactsOf(ObjectInstance o, int part, List<ObjectColumn> list) => AddPart(lib, shapes, o, part, list, true);

        public int ChunkComputes => chunkComputes;

        public ObjectInstance[] Chunk(int cx, int cz)
        {
            int c = cz * across + cx;
            var s = Volatile.Read(ref chunks[c]);
            if (s != null) return s;
            var gate = Volatile.Read(ref chunkGates[c]) ?? Interlocked.CompareExchange(ref chunkGates[c], new object(), null) ?? chunkGates[c];
            lock (gate)
            {
                s = chunks[c];
                if (s != null) return s;
                s = Place(cx, cz);
                Interlocked.Increment(ref chunkComputes);
                Volatile.Write(ref chunks[c], s);
                return s;
            }
        }

        [ThreadStatic] static List<ObjectInstance> scratch;

        public ObjectInstance[] Place(int cx, int cz, bool solidOnly = false)
        {
            var list = scratch ?? (scratch = new List<ObjectInstance>());
            list.Clear();
            PlaceInto(cx, cz, solidOnly, list, null);
            return list.ToArray();
        }

        public void SolidInto(int cx, int cz, List<ObjectInstance> list)
        {
            list.Clear();
            var cached = Volatile.Read(ref chunks[cz * across + cx]);
            if (cached != null) list.AddRange(cached);
            else PlaceInto(cx, cz, true, list, null);
        }

        public void PlaceInto(int cx, int cz, List<ObjectInstance> list)
        {
            list.Clear();
            var cached = Volatile.Read(ref chunks[cz * across + cx]);
            if (cached != null) list.AddRange(cached);
            else PlaceInto(cx, cz, false, list, null);
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

        void PlaceInto(int cx, int cz, bool solidOnly, List<ObjectInstance> list, List<FeatureEdit> boulders, bool sitesOnly = false, bool builtOnly = false)
        {
            float span = WorldChunks.Size * cm;
            float x0 = cx * span, z0 = cz * span;
            float sx = Math.Min(span, worldM - x0), sz = Math.Min(span, worldM - z0);
            if (sx <= 0 || sz <= 0) return;
            int chunk = cz * across + cx;
            ulong chunkKey = Rng.Mix((ulong)chunk + 1);
            var near = sitesOnly ? null : SitesNear(cx, cz);
            for (int d = 0; d < lib.Objects.Count; d++)
            {
                int ordinal = 0;
                var pl = lib.Objects[d].Placement;
                if (sitesOnly && siteBox[d] == null) continue;
                if (bankSite[d] >= 0)
                {
                    if (boulders == null && (!solidOnly || AnySolid(d))) PlaceBank(d, chunk, chunkKey, x0, z0, sx, sz, near, list);
                    continue;
                }
                if (pl.Discovery > 0)
                {
                    if (!builtOnly && boulders == null && (!solidOnly || AnySolid(d))) EmitDiscoveries(d, chunk, x0, z0, sx, sz, list);
                    continue;
                }
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
                    float fx = 0, fz = 0, fan = 0;
                    if (pl.FanM > 0) fan = Fan(pl, ax, az, out fx, out fz);
                    for (int m = 0; m < pl.ClusterCount; m++)
                    {
                        float ox = rng.Range(-1f, 1f), oz = rng.Range(-1f, 1f), t = rng.NextFloat(), qa = rng.Range(-1f, 1f), qb = rng.Range(-1f, 1f);
                        int variant = (int)(rng.NextU32() % (uint)lib.Objects[d].Variants);
                        if (!open) continue;
                        float px = m == 0 ? ax : ax + ox * pl.ClusterRadiusM, pz = m == 0 ? az : az + oz * pl.ClusterRadiusM, grade = 1f;
                        if (fan > 0 && m > 0)
                        {
                            float along = 0.15f + 0.85f * Math.Abs(oz), reach = along * fan, side = ox * (0.2f + 0.8f * along) * 0.55f * reach;
                            px = ax + fx * reach - fz * side;
                            pz = az + fz * reach + fx * side;
                            grade = 0.7f + 0.6f * along;
                        }
                        if (px < x0 || pz < z0 || px >= x0 + sx || pz >= z0 + sz) continue;
                        if (Density(d, px, pz) < 1f && thin.NextFloat() >= Density(d, px, pz)) continue;
                        float size = (pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * t * t) * grade;
                        if ((pl.EditMinSizeM > 0 && size >= pl.EditMinSizeM) != (boulders != null)) continue;
                        if (!built && Cleared(near, px, pz, footprint[d] * size * (boulders != null ? BoulderReach : 1f))) continue;
                        if (!Accept(d, px, pz, size, qa, qb, out var o)) continue;
                        o.Variant = variant;
                        o.Id = ObjectIds.Make(chunk, d, ordinal++);
                        if (boulders == null) list.Add(o);
                        else AddBoulder(o, footprint[d] * size, qa, qb, x0, z0, sx, sz, boulders);
                        if (pl.ChainMax > 1 && boulders == null) Chain(d, o, chunk, chunkKey, x0, z0, sx, sz, ref ordinal, list);
                    }
                }
            }
        }

        void Chain(int d, ObjectInstance o, int chunk, ulong chunkKey, float x0, float z0, float sx, float sz, ref int ordinal, List<ObjectInstance> list)
        {
            var def = lib.Objects[d];
            var pl = def.Placement;
            var rng = new Rng(Rng.Mix(chainStream ^ chunkKey ^ key[d] ^ ((ulong)ordinal * 0x9E3779B97F4A7C15UL)));
            int count = rng.Range(pl.ChainMin, pl.ChainMax + 1) - 1;
            float size = o.Size, span = Structures.WallSpan * size, thick = Structures.WallBase * size;
            Rotate(o, 1, 0, 0, out float ux, out _, out float uz);
            float ex = o.X + ux * 0.5f * span, ez = o.Z + uz * 0.5f * span;
            for (int k = 0; k < count; k++)
            {
                float roll = rng.NextFloat(), turn = rng.NextFloat() < 0.5f ? -1f : 1f, wander = rng.Range(-0.1f, 0.1f);
                int variant = (int)(rng.NextU32() % (uint)def.Variants);
                bool gate = rng.NextFloat() < pl.ChainGate, corner = roll < pl.ChainCorner;
                float bx = ex, bz = ez;
                if (corner)
                {
                    bx -= ux * 0.5f * thick; bz -= uz * 0.5f * thick;
                    float t = ux; ux = -uz * turn; uz = t * turn;
                }
                else
                {
                    float gx = GroundAt(detail, cm, ex + SlopeStepM, ez) - GroundAt(detail, cm, ex - SlopeStepM, ez);
                    float gz = GroundAt(detail, cm, ex, ez + SlopeStepM) - GroundAt(detail, cm, ex, ez - SlopeStepM);
                    float gl = (float)Math.Sqrt(gx * gx + gz * gz);
                    if (gl > 0.02f * SlopeStepM)
                    {
                        float cx = -gz / gl, cz = gx / gl;
                        if (cx * ux + cz * uz < 0) { cx = -cx; cz = -cz; }
                        ux += 0.8f * (cx - ux); uz += 0.8f * (cz - uz);
                    }
                    float px = -uz, pz = ux;
                    ux += wander * px; uz += wander * pz;
                    float ul = (float)Math.Sqrt(ux * ux + uz * uz);
                    ux /= ul; uz /= ul;
                    bx -= ux * Structures.WallOverlap * size; bz -= uz * Structures.WallOverlap * size;
                }
                if (gate) { ex = bx + ux * Structures.GateSpan * span; ez = bz + uz * Structures.GateSpan * span; continue; }
                bool placed = false;
                float ax = ux, az = uz, sbx = bx, sbz = bz;
                for (int tries = 0; tries < (corner ? 1 : 3) && !placed; tries++)
                {
                    if (tries > 0)
                    {
                        float side = tries == 1 ? turn : -turn;
                        bx = sbx - ax * 0.5f * thick; bz = sbz - az * 0.5f * thick;
                        ux = -az * side; uz = ax * side;
                    }
                    float mx = bx + ux * 0.5f * span, mz = bz + uz * 0.5f * span, fx = bx + ux * span, fz = bz + uz * span;
                    if (Math.Min(bx, fx) < x0 || Math.Min(bz, fz) < z0 || Math.Max(bx, fx) >= x0 + sx || Math.Max(bz, fz) >= z0 + sz) continue;
                    float qw = (float)Math.Sqrt(Math.Max(0, 0.5f * (1 + ux))), qy = (uz > 0 ? -1f : 1f) * (float)Math.Sqrt(Math.Max(0, 0.5f * (1 - ux)));
                    if (!Accept(d, mx, mz, size, qw, qy, out var s)) continue;
                    s.Variant = variant;
                    s.Id = ObjectIds.Make(chunk, d, ordinal++);
                    list.Add(s);
                    ex = fx; ez = fz;
                    placed = true;
                }
                if (!placed) return;
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
            if (!Within(a, x0, z0, sx, sz) || !Within(b, x0, z0, sx, sz) || Wet(a) || Wet(b) || NearBog(a) || NearBog(b)) return;
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

        float Fan(ObjectPlacement pl, float x, float z, out float fx, out float fz)
        {
            float r = pl.FootReachM, diag = 0.70710678f, y = GroundAt(detail, cm, x, z), best = float.MinValue;
            int top = 0;
            Span<float> rise = stackalloc float[8];
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, diag, out float ux, out float uz);
                rise[k] = GroundAt(detail, cm, x + ux * r, z + uz * r) - y;
                if (rise[k] > best) { best = rise[k]; top = k; }
            }
            FanDir(top, diag, out fx, out fz);
            fx = -fx; fz = -fz;
            bool gully = best > 0 && rise[(top + 1) & 7] > 0.7f * best && rise[(top + 7) & 7] > 0.7f * best;
            return gully ? 1.6f * pl.FanM : pl.FanM;
        }

        static void FanDir(int k, float diag, out float ux, out float uz)
        {
            int a = k & 7;
            ux = a == 0 ? 1 : a == 4 ? -1 : a == 2 || a == 6 ? 0 : a < 2 || a > 6 ? diag : -diag;
            uz = a == 2 ? 1 : a == 6 ? -1 : a == 0 || a == 4 ? 0 : a < 4 ? diag : -diag;
        }

        bool Open(int d, float x, float z)
        {
            if (x < 0 || z < 0 || x >= worldM || z >= worldM) return false;
            int b = w.Biome[Cell(z) * n + Cell(x)];
            return b >= 0 && density[d][b] > 0;
        }

        bool Accept(int d, float x, float z, float size, float qa, float qb, out ObjectInstance o, bool bank = false)
        {
            o = default;
            if (!bank && !Open(d, x, z)) return false;
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
            if (!Clear(i, gx, gz, x, y, z, r, bank, pl.Discovery)) return false;
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

        bool Clear(int i, int gx, int gz, float x, float y, float z, float r, bool bank, int site = 0)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ax = gx + dx, az = gz + dz;
                    if (ax < 0 || az < 0 || ax >= n || az >= n) continue;
                    int j = az * n + ax;
                    if (w.Sea[j] && (j == i || y < ShoreClearM)) return false;
                    int lake = w.Water.LakeId[j];
                    if (!bank && lake >= 0 && (j == i || y < w.Water.Lakes[lake].Level + ShoreClearM)) return false;
                }
            for (int k = blockStart[i]; k < blockStart[i + 1]; k++)
            {
                int item = blockItems[k];
                if (item < 0)
                {
                    var f = edits[~item];
                    if (site > 0 && (f.Shape == FeatureEdit.Spring || ((site == WolfDen || Pooled(site)) && f.Shape == FeatureEdit.Boulder))) continue;
                    if (x + r >= f.MinX && x - r <= f.MaxX && z + r >= f.MinZ && z - r <= f.MaxZ) return false;
                    if (f.Crusts && CrustDraw(x, z) < CrustThin * f.Crust(x, z)) return false;
                    continue;
                }
                if (bank) continue;
                float dist = Distance(item, x, z), hw = phw[item];
                if (dist < hw + r + RiverClearM) return false;
                if (dist < 2 * hw + r + BankReachM && y < psurf[item] + ShoreClearM) return false;
            }
            return true;
        }

        float CrustDraw(float x, float z)
        {
            ulong h = Rng.Mix(sinterStream ^ ((ulong)(uint)(int)Math.Floor(x * 16f) << 32 | (uint)(int)Math.Floor(z * 16f)));
            return (h >> 40) * (1f / 16777216f);
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

        public static void AddColumns(ObjectLibrary lib, PartShapes shapes, ObjectInstance o, List<ObjectColumn> list)
        {
            var def = lib.Objects[o.Def];
            for (int pi = 0; pi < def.Parts.Count; pi++)
                if (lib.PartSolid(def, def.Parts[pi])) AddPart(lib, shapes, o, pi, list, false);
        }

        public static void AddContacts(ObjectLibrary lib, PartShapes shapes, ObjectInstance o, List<ObjectColumn> list)
        {
            var def = lib.Objects[o.Def];
            for (int pi = 0; pi < def.Parts.Count; pi++) AddPart(lib, shapes, o, pi, list, true);
        }

        public const float ContactReach = 0.35f, ContactReachPerM = 0.1f;

        public static bool Stamps(ObjectLibrary lib, ObjectInstance o, ObjectColumn c) => c.Bottom <= o.Y + (lib.Objects[o.Def].Placement.Sink + ContactReachPerM) * o.Size + ContactReach;

        public static void AddLush(ObjectLibrary lib, PartShapes shapes, ObjectInstance o, List<ObjectColumn> list)
        {
            var def = lib.Objects[o.Def];
            for (int pi = 0; pi < def.Parts.Count; pi++)
            {
                var bands = shapes.Lush[o.Def][pi];
                if (bands == null || bands[o.Variant] == null) continue;
                var p = def.Parts[pi];
                float rs = Math.Min(p.ScaleX, p.ScaleZ) * o.Size;
                foreach (var c in bands[o.Variant])
                {
                    Rotate(o, (p.OffsetX + c.X * p.ScaleX) * o.Size, 0, (p.OffsetZ + c.Z * p.ScaleZ) * o.Size, out float cx, out _, out float cz);
                    list.Add(new ObjectColumn { X = cx + o.X, Z = cz + o.Z, R = c.R * rs, Bottom = c.Bottom * rs, Top = o.Y });
                }
            }
        }

        public static void AddLights(ObjectLibrary lib, PartShapes shapes, ObjectInstance o, List<StructureLight> list)
        {
            var def = lib.Objects[o.Def];
            for (int pi = 0; pi < def.Parts.Count; pi++)
            {
                var anchors = shapes.Lights[o.Def][pi];
                if (anchors == null || anchors[o.Variant] == null) continue;
                var p = def.Parts[pi];
                foreach (var a in anchors[o.Variant])
                {
                    Rotate(o, (p.OffsetX + a.X * p.ScaleX) * o.Size, (p.OffsetY + a.Y * p.ScaleY) * o.Size, (p.OffsetZ + a.Z * p.ScaleZ) * o.Size, out float lx, out float ly, out float lz);
                    list.Add(new StructureLight { X = lx + o.X, Y = ly + o.Y, Z = lz + o.Z, Shape = a.Shape });
                }
            }
        }

        public static void AddPart(ObjectLibrary lib, PartShapes shapes, ObjectInstance o, int pi, List<ObjectColumn> list, bool contact)
        {
            var def = lib.Objects[o.Def];
            {
                var p = def.Parts[pi];
                float tall = p.ScaleY * o.Size * shapes.Heights[o.Def][pi][o.Variant];
                var local = shapes.Columns[o.Def][pi];
                if (local != null)
                {
                    float rs = Math.Min(p.ScaleX, p.ScaleZ) * o.Size;
                    foreach (var c in local[o.Variant]) AddLocal(o, p, c, rs, list);
                    if (contact)
                        foreach (var c in shapes.Contacts[o.Def][pi][o.Variant]) AddLocal(o, p, c, rs, list);
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
                    if (p.Ends == ObjectPart.EndsRooted)
                    {
                        ObjectMeshes.RootPlate(ObjectMeshes.Key(def.Id, pi, o.Variant), out float rp, out float yc, out float thick);
                        float t = -0.5f * len + (0.3f - 0.5f * thick) * rad;
                        Rotate(o, 0, 0, 1, out float wx, out float wy, out float wz);
                        for (int k = -1; k <= 1; k++)
                        {
                            float side = 0.55f * rp * rad * k;
                            float py = cy + uy * t + wy * side;
                            list.Add(new ObjectColumn { X = cx + ux * t + wx * side, Z = cz + uz * t + wz * side, R = 0.35f * rp * rad, Bottom = py - rad, Top = py + (yc + 0.9f * rp) * rad });
                        }
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

        static void AddLocal(ObjectInstance o, ObjectPart p, ObjectColumn c, float rs, List<ObjectColumn> list)
        {
            Rotate(o, (p.OffsetX + c.X * p.ScaleX) * o.Size, (p.OffsetY + c.Bottom * p.ScaleY) * o.Size, (p.OffsetZ + c.Z * p.ScaleZ) * o.Size, out float cx, out float cy, out float cz);
            list.Add(new ObjectColumn { X = cx + o.X, Z = cz + o.Z, R = c.R * rs, Bottom = cy + o.Y, Top = cy + o.Y + (c.Top - c.Bottom) * p.ScaleY * o.Size });
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
        public ObjectColumn[][][][] Columns, Contacts, Lush;
        public StructureLight[][][][] Lights;

        public static PartShapes Of(ObjectLibrary lib)
        {
            int n = lib.Objects.Count;
            var s = new PartShapes { Heights = new float[n][][], Columns = new ObjectColumn[n][][][], Contacts = new ObjectColumn[n][][][], Lush = new ObjectColumn[n][][][], Lights = new StructureLight[n][][][] };
            for (int d = 0; d < n; d++)
            {
                var def = lib.Objects[d];
                s.Heights[d] = new float[def.Parts.Count][];
                s.Columns[d] = new ObjectColumn[def.Parts.Count][][];
                s.Contacts[d] = new ObjectColumn[def.Parts.Count][][];
                s.Lush[d] = new ObjectColumn[def.Parts.Count][][];
                s.Lights[d] = new StructureLight[def.Parts.Count][][];
                for (int p = 0; p < def.Parts.Count; p++)
                {
                    int shape = def.Parts[p].Shape;
                    s.Heights[d][p] = new float[def.Variants];
                    bool built = ObjectPart.IsStructure(shape);
                    if (built)
                    {
                        s.Columns[d][p] = new ObjectColumn[def.Variants][];
                        s.Contacts[d][p] = new ObjectColumn[def.Variants][];
                    }
                    for (int v = 0; v < def.Variants; v++)
                    {
                        ulong k = ObjectMeshes.Key(def.Id, p, v);
                        s.Heights[d][p][v] = shape == ObjectPart.Rock ? ObjectMeshes.RockHeight(k) : 1f;
                        if (!built) continue;
                        var l = Structures.Layout(shape, k);
                        s.Columns[d][p][v] = l.Columns.ToArray();
                        s.Contacts[d][p][v] = l.Contacts.ToArray();
                        if (l.Lights.Count > 0)
                        {
                            if (s.Lights[d][p] == null) s.Lights[d][p] = new StructureLight[def.Variants][];
                            s.Lights[d][p][v] = l.Lights.ToArray();
                        }
                        if (l.Lush.Count == 0) continue;
                        if (s.Lush[d][p] == null) s.Lush[d][p] = new ObjectColumn[def.Variants][];
                        s.Lush[d][p][v] = l.Lush.ToArray();
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
        float RegionM { get; }
        int Across { get; }
        bool Grid(int region, out RegionGrid g);
        int Start(int region, int cell);
        ObjectColumn Column(int region, int k);
    }

    public struct NoColumns : IColumnTable
    {
        public float CellM => 1;
        public float RegionM => 1;
        public int Across => 0;
        public bool Grid(int region, out RegionGrid g) { g = default; return false; }
        public int Start(int region, int cell) => 0;
        public ObjectColumn Column(int region, int k) => default;
    }

    public static class Obstacles
    {
        public static readonly ObjectColumn Dead = new ObjectColumn { Bottom = float.PositiveInfinity, Top = float.NegativeInfinity };

        public static bool IsDead(ObjectColumn c) => c.Top < c.Bottom;

        public static float Top<T>(T t, float x, float z, float r, float bottom, float top) where T : struct, IColumnTable
        {
            float best = float.NegativeInfinity;
            int across = t.Across;
            if (across <= 0) return best;
            float size = t.RegionM, cell = t.CellM;
            int rx = (int)Math.Floor(x / size), rz = (int)Math.Floor(z / size);
            for (int qz = Math.Max(0, rz - 1); qz <= Math.Min(across - 1, rz + 1); qz++)
                for (int qx = Math.Max(0, rx - 1); qx <= Math.Min(across - 1, rx + 1); qx++)
                {
                    int region = qz * across + qx;
                    if (!t.Grid(region, out var g) || g.W == 0) continue;
                    float reach = r + g.Max;
                    int x0 = Math.Max(g.X0, (int)Math.Floor((x - reach) / cell)), x1 = Math.Min(g.X0 + g.W - 1, (int)Math.Floor((x + reach) / cell));
                    int z0 = Math.Max(g.Z0, (int)Math.Floor((z - reach) / cell)), z1 = Math.Min(g.Z0 + g.H - 1, (int)Math.Floor((z + reach) / cell));
                    if (x0 > x1 || z0 > z1) continue;
                    for (int cz = z0; cz <= z1; cz++)
                    {
                        int row = (cz - g.Z0) * g.W - g.X0;
                        for (int k = t.Start(region, row + x0), e = t.Start(region, row + x1 + 1); k < e; k++)
                        {
                            var c = t.Column(region, k);
                            float dx = c.X - x, dz = c.Z - z, rr = r + c.R;
                            if (dx * dx + dz * dz < rr * rr && c.Bottom < top && c.Top > bottom && c.Top > best) best = c.Top;
                        }
                    }
                }
            return best;
        }
    }
}
