using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct EditIds : IEditList
    {
        public FeatureEdit[] Items;
        public int[] Ids;
        public int Start, N;

        public int Count => N;
        public FeatureEdit At(int k) => Items[Ids[Start + k]];
    }

    public sealed class EditGrid
    {
        public const float CellM = 64f, PadM = 4f;

        public readonly FeatureEdit[] Edits;
        public readonly int Across;
        public readonly int[] Start, Items;

        public EditGrid(FeatureEdit[] edits, float worldM)
        {
            Edits = edits;
            Build(edits, worldM, out Across, out Start, out Items);
        }

        public static int CellsAcross(float worldM) => Math.Max(1, (int)Math.Ceiling(worldM / CellM));

        public static int CellOf(float v, int across)
        {
            int c = (int)Math.Floor(v / CellM);
            return c < 0 ? 0 : c >= across ? across - 1 : c;
        }

        public static void Build(FeatureEdit[] edits, float worldM, out int across, out int[] start, out int[] items)
        {
            int a = across = CellsAcross(worldM);
            start = new int[a * a + 1];
            for (int q = 0; q < edits.Length; q++)
            {
                Span(edits[q], a, out int x0, out int x1, out int z0, out int z1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++) start[z * a + x + 1]++;
            }
            for (int c = 0; c < a * a; c++) start[c + 1] += start[c];
            items = new int[start[a * a]];
            var fill = (int[])start.Clone();
            for (int q = 0; q < edits.Length; q++)
            {
                Span(edits[q], a, out int x0, out int x1, out int z0, out int z1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++) items[fill[z * a + x]++] = q;
            }
        }

        static void Span(FeatureEdit f, int across, out int x0, out int x1, out int z0, out int z1)
        {
            x0 = CellOf(f.MinX - PadM, across); x1 = CellOf(f.MaxX + PadM, across);
            z0 = CellOf(f.MinZ - PadM, across); z1 = CellOf(f.MaxZ + PadM, across);
        }

        public EditIds Cell(float x, float z)
        {
            int c = CellOf(z, Across) * Across + CellOf(x, Across);
            return new EditIds { Items = Edits, Ids = Items, Start = Start[c], N = Start[c + 1] - Start[c] };
        }

        public void Near(float x0, float z0, float x1, float z1, List<int> ids)
        {
            ids.Clear();
            int cx0 = CellOf(x0, Across), cx1 = CellOf(x1, Across), cz0 = CellOf(z0, Across), cz1 = CellOf(z1, Across);
            for (int z = cz0; z <= cz1; z++)
                for (int x = cx0; x <= cx1; x++)
                    for (int k = Start[z * Across + x], end = Start[z * Across + x + 1]; k < end; k++)
                    {
                        var f = Edits[Items[k]];
                        if (f.MaxX > x0 && f.MinX < x1 && f.MaxZ > z0 && f.MinZ < z1) ids.Add(Items[k]);
                    }
            if (cx0 == cx1 && cz0 == cz1) return;
            ids.Sort();
            int n = 0;
            for (int k = 0; k < ids.Count; k++) if (n == 0 || ids[n - 1] != ids[k]) ids[n++] = ids[k];
            ids.RemoveRange(n, ids.Count - n);
        }
    }
}
