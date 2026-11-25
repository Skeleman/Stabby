using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct RegionGrid
    {
        public int X0, Z0, W, H;
        public float Max;

        public int Cells => W * H;
    }

    public sealed class ColumnRegion
    {
        public const float CellM = 8f;

        public int Index;
        public RegionGrid Grid;
        public int[] Starts;
        public ObjectColumn[] Items;
        public ulong[] Owners;

        public int Count => Items.Length;

        static readonly Stack<List<ObjectInstance>> objectLists = new Stack<List<ObjectInstance>>();
        static readonly Stack<List<ObjectColumn>> columnLists = new Stack<List<ObjectColumn>>();
        static readonly Stack<List<ulong>> ownerLists = new Stack<List<ulong>>();

        public static ColumnRegion Build(ObjectField field, int index)
        {
            List<ObjectInstance> objects;
            List<ObjectColumn> columns;
            List<ulong> owners;
            lock (objectLists) objects = objectLists.Count > 0 ? objectLists.Pop() : new List<ObjectInstance>();
            lock (columnLists) columns = columnLists.Count > 0 ? columnLists.Pop() : new List<ObjectColumn>();
            lock (ownerLists) owners = ownerLists.Count > 0 ? ownerLists.Pop() : new List<ulong>();
            try { return Build(field, index, objects, columns, owners); }
            finally
            {
                lock (objectLists) objectLists.Push(objects);
                lock (columnLists) columnLists.Push(columns);
                lock (ownerLists) ownerLists.Push(owners);
            }
        }

        static ColumnRegion Build(ObjectField field, int index, List<ObjectInstance> objects, List<ObjectColumn> columns, List<ulong> owners)
        {
            objects.Clear();
            columns.Clear();
            owners.Clear();
            int across = field.ChunksAcross;
            field.SolidInto(index % across, index / across, objects);
            Collect(field, objects, columns, owners);
            return Of(index, columns, owners);
        }

        public static void Collect(ObjectField field, IEnumerable<ObjectInstance> objects, List<ObjectColumn> columns, List<ulong> owners)
        {
            foreach (var o in objects)
            {
                int before = columns.Count;
                field.ColumnsOf(o, columns);
                for (int k = before; k < columns.Count; k++) owners.Add(o.Id);
            }
        }

        public static ColumnRegion Of(int index, List<ObjectColumn> columns, List<ulong> owners)
        {
            var r = new ColumnRegion { Index = index, Items = new ObjectColumn[columns.Count], Owners = new ulong[columns.Count] };
            if (columns.Count == 0) { r.Starts = new int[1]; return r; }
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
            float max = 0;
            foreach (var c in columns)
            {
                int cx = CellOf(c.X), cz = CellOf(c.Z);
                x0 = Math.Min(x0, cx); x1 = Math.Max(x1, cx);
                z0 = Math.Min(z0, cz); z1 = Math.Max(z1, cz);
                max = Math.Max(max, c.R);
            }
            r.Grid = new RegionGrid { X0 = x0, Z0 = z0, W = x1 - x0 + 1, H = z1 - z0 + 1, Max = max };
            r.Starts = new int[r.Grid.Cells + 1];
            var at = Scratch(ref scratchAt, columns.Count);
            for (int k = 0; k < columns.Count; k++)
            {
                at[k] = (CellOf(columns[k].Z) - z0) * r.Grid.W + CellOf(columns[k].X) - x0;
                r.Starts[at[k] + 1]++;
            }
            for (int b = 0; b < r.Grid.Cells; b++) r.Starts[b + 1] += r.Starts[b];
            var fill = Scratch(ref scratchFill, r.Starts.Length);
            Array.Copy(r.Starts, fill, r.Starts.Length);
            for (int k = 0; k < columns.Count; k++)
            {
                int q = fill[at[k]]++;
                r.Items[q] = columns[k];
                r.Owners[q] = owners[k];
            }
            return r;
        }

        [ThreadStatic] static int[] scratchAt, scratchFill;

        static int[] Scratch(ref int[] buffer, int n)
        {
            if (buffer == null || buffer.Length < n) buffer = new int[Math.Max(n, buffer == null ? 0 : buffer.Length * 2)];
            return buffer;
        }

        public static int CellOf(float m) => (int)Math.Floor(m / CellM);

        public int Kill(ulong id)
        {
            int killed = 0;
            for (int k = 0; k < Owners.Length; k++)
                if (Owners[k] == id && !Obstacles.IsDead(Items[k])) { Items[k] = Obstacles.Dead; killed++; }
            return killed;
        }

        public int Alive(ulong id)
        {
            int n = 0;
            for (int k = 0; k < Owners.Length; k++)
                if (Owners[k] == id && !Obstacles.IsDead(Items[k])) n++;
            return n;
        }
    }

    public struct ColumnRegions : IColumnTable
    {
        public const float WholeM = 1e9f;

        public ColumnRegion[] Slots;
        public int Count;
        public float Size;

        public float CellM => ColumnRegion.CellM;
        public float RegionM => Size;
        public int Across => Count;

        public bool Grid(int region, out RegionGrid g)
        {
            var r = Slots[region];
            g = r == null ? default : r.Grid;
            return r != null;
        }

        public int Start(int region, int cell) => Slots[region].Starts[cell];
        public ObjectColumn Column(int region, int k) => Slots[region].Items[k];

        public static ColumnRegions For(ObjectField field) =>
            new ColumnRegions { Count = field.ChunksAcross, Size = field.ChunkMeters, Slots = new ColumnRegion[field.ChunksAcross * field.ChunksAcross] };

        public static ColumnRegions Whole(List<ObjectColumn> columns)
        {
            var owners = new List<ulong>(columns.Count);
            for (int k = 0; k < columns.Count; k++) owners.Add(0);
            return new ColumnRegions { Count = 1, Size = WholeM, Slots = new[] { ColumnRegion.Of(0, columns, owners) } };
        }

        public bool Resident(int region) => Slots[region] != null;
        public void Put(ColumnRegion r) => Slots[r.Index] = r;
        public void Free(int region) => Slots[region] = null;

        public int Kill(ulong id)
        {
            int r = ObjectIds.Chunk(id);
            return r < Slots.Length && Slots[r] != null ? Slots[r].Kill(id) : 0;
        }
    }

    public static class ObjectRegions
    {
        public static float Reach(ObjectLibrary lib)
        {
            float pixels = float.MaxValue;
            foreach (var def in lib.Objects)
                foreach (var p in def.Parts)
                    if (lib.PartSolid(def, p)) pixels = Math.Min(pixels, lib.KindOf(def).FadePixels);
            return pixels == float.MaxValue ? 0 : Detail.SimulationReach(Walk.Height, pixels);
        }

        public static float FreeReach(float reach) => reach / (1f - Detail.Hysteresis);

        public static float Distance(int across, float size, int region, float x, float z)
        {
            float x0 = region % across * size, z0 = region / across * size;
            float dx = Math.Max(Math.Max(x0 - x, x - (x0 + size)), 0), dz = Math.Max(Math.Max(z0 - z, z - (z0 + size)), 0);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public static void Near(int across, float size, float x, float z, float reach, List<int> into)
        {
            int x0 = Math.Max(0, (int)Math.Floor((x - reach) / size)), x1 = Math.Min(across - 1, (int)Math.Floor((x + reach) / size));
            int z0 = Math.Max(0, (int)Math.Floor((z - reach) / size)), z1 = Math.Min(across - 1, (int)Math.Floor((z + reach) / size));
            for (int rz = z0; rz <= z1; rz++)
                for (int rx = x0; rx <= x1; rx++)
                {
                    int r = rz * across + rx;
                    if (Distance(across, size, r, x, z) <= reach) into.Add(r);
                }
        }
    }
}
