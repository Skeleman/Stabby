#nullable disable
using System;
using System.Collections.Generic;
using Stabby.World;

namespace Stabby.Game
{
    public sealed class ColumnBake
    {
        public RegionGrid Grid;
        public int Count, Cells;
        public int[] Starts = new int[1];
        public ObjectColumn[] Items = Array.Empty<ObjectColumn>();
        public ulong[] Owners = Array.Empty<ulong>();

        readonly List<ObjectInstance> objects = new List<ObjectInstance>();
        readonly List<ObjectColumn> columns = new List<ObjectColumn>();
        readonly List<ulong> owners = new List<ulong>();
        int[] at = Array.Empty<int>(), fill = new int[1];

        [ThreadStatic] static ColumnBake local;

        public static ColumnBake Local => local ??= new ColumnBake();

        public void Run(ObjectField field, int index)
        {
            columns.Clear();
            owners.Clear();
            int across = field.ChunksAcross;
            field.SolidInto(index % across, index / across, objects);
            for (int o = 0; o < objects.Count; o++)
            {
                int before = columns.Count;
                field.ColumnsOf(objects[o], columns);
                ulong id = objects[o].Id;
                for (int k = before; k < columns.Count; k++) owners.Add(id);
            }
            objects.Clear();
            Sort();
        }

        void Sort()
        {
            int n = columns.Count;
            Count = n;
            if (Items.Length < n) { Items = new ObjectColumn[Math.Max(n, Items.Length * 2)]; Owners = new ulong[Items.Length]; at = new int[Items.Length]; }
            if (n == 0) { Grid = default; Cells = 0; Starts[0] = 0; return; }
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
            float max = 0;
            for (int k = 0; k < n; k++)
            {
                var c = columns[k];
                int cx = ColumnRegion.CellOf(c.X), cz = ColumnRegion.CellOf(c.Z);
                x0 = Math.Min(x0, cx); x1 = Math.Max(x1, cx);
                z0 = Math.Min(z0, cz); z1 = Math.Max(z1, cz);
                max = Math.Max(max, c.R);
            }
            Grid = new RegionGrid { X0 = x0, Z0 = z0, W = x1 - x0 + 1, H = z1 - z0 + 1, Max = max };
            Cells = Grid.Cells;
            if (Starts.Length < Cells + 1) { Starts = new int[Math.Max(Cells + 1, Starts.Length * 2)]; fill = new int[Starts.Length]; }
            Array.Clear(Starts, 0, Cells + 1);
            for (int k = 0; k < n; k++)
            {
                at[k] = (ColumnRegion.CellOf(columns[k].Z) - z0) * Grid.W + ColumnRegion.CellOf(columns[k].X) - x0;
                Starts[at[k] + 1]++;
            }
            for (int b = 0; b < Cells; b++) Starts[b + 1] += Starts[b];
            Array.Copy(Starts, fill, Cells + 1);
            for (int k = 0; k < n; k++)
            {
                int q = fill[at[k]]++;
                Items[q] = columns[k];
                Owners[q] = owners[k];
            }
        }
    }
}
