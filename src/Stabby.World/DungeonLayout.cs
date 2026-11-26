using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct DungeonPiece
    {
        public int Shape, Open;
        public float X, Y, Z, C, S, Scale;
        public bool Stub;
    }

    public struct DungeonVault
    {
        public float X0, X1, Y0, Y1, Z0, Z1;
        public bool Bare;
    }

    public static partial class Structures
    {
        public const int DungeonSpanI = 5, DungeonSpanJ = 10, DungeonTries = 40, DungeonRoomsMin = 4, DungeonRoomsMax = 8;
        public const float DungeonZ0 = -20.5f, DungeonFloorY = -8f, DungeonDoorChance = 0.25f, DungeonStubChance = 0.3f;
        public const float VaultR = 1.2f, VaultInset = 0.375f * VaultR, VaultFloor = 0.08f, VaultReach = 0.6f, VaultPorch = 1.2f;
        static readonly float[] DungeonSeats = { 0, -22.1f / DungeonM, 1.9f / DungeonM, -23.5f / DungeonM, -1.9f / DungeonM, -23.5f / DungeonM, 1.4f / DungeonM, -20.2f / DungeonM, -1.4f / DungeonM, -20.2f / DungeonM };

        static int CellIndex(int i, int j) => j * (2 * DungeonSpanI + 1) + i + DungeonSpanI;

        static bool FreeCell(bool[] used, int i, int j) => i >= -DungeonSpanI && i <= DungeonSpanI && j >= 0 && j <= DungeonSpanJ && !used[CellIndex(i, j)];

        static DungeonPiece Cell(int shape, int i, int j, float c, float s) =>
            new DungeonPiece { Shape = shape, X = KitCellM * i, Y = DungeonFloorY, Z = DungeonZ0 + KitCellM * j, C = c, S = s, Scale = 1 };

        public static List<DungeonPiece> DungeonLayout(ulong key)
        {
            var rng = Rng.Stream(key, "dungeon-layout");
            var used = new bool[(2 * DungeonSpanI + 1) * (DungeonSpanJ + 1)];
            var plan = new List<DungeonPiece>
            {
                new DungeonPiece { Shape = ObjectPart.DungeonDoor, Z = DungeonZ0, C = 1, Scale = 1 },
                new DungeonPiece { Shape = ObjectPart.DungeonPillar, X = -1.9f, Z = DungeonZ0 - 2.9f, C = 1, Scale = 0.8f },
                new DungeonPiece { Shape = ObjectPart.DungeonPillar, X = 1.9f, Z = DungeonZ0 - 2.9f, C = 1, Scale = 0.8f },
                new DungeonPiece { Shape = ObjectPart.DungeonStairs, Y = -KitStoreyM, Z = DungeonZ0 + 1.5f * KitCellM, C = -1, Scale = 1 },
                new DungeonPiece { Shape = ObjectPart.DungeonStairs, Y = -2 * KitStoreyM, Z = DungeonZ0 + 3.5f * KitCellM, C = -1, Scale = 1 },
            };
            for (int j = 0; j <= 4; j++) used[CellIndex(0, j)] = true;
            var ri = new List<int> { 0 };
            var rj = new List<int> { 6 };
            var open = new List<int> { 4 };
            Claim(used, 0, 6);
            int target = rng.Range(DungeonRoomsMin, DungeonRoomsMax);
            for (int t = 0; t < DungeonTries && ri.Count < target; t++)
            {
                int r = rng.Range(0, ri.Count), d = rng.Range(0, 4), len = rng.Range(1, 4);
                if ((open[r] & (1 << d)) != 0) continue;
                int dx = RoomDx[d], dz = RoomDz[d], ni = ri[r] + (len + 3) * dx, nj = rj[r] + (len + 3) * dz;
                if (ni < 1 - DungeonSpanI || ni > DungeonSpanI - 1 || nj < 1 || nj > DungeonSpanJ - 1) continue;
                bool free = true;
                for (int k = 2; k <= len + 1 && free; k++) free = FreeCell(used, ri[r] + k * dx, rj[r] + k * dz);
                for (int a = -1; a <= 1 && free; a++)
                    for (int b = -1; b <= 1 && free; b++) free = FreeCell(used, ni + a, nj + b);
                if (!free) continue;
                for (int k = 2; k <= len + 1; k++)
                {
                    int ci = ri[r] + k * dx, cj = rj[r] + k * dz;
                    used[CellIndex(ci, cj)] = true;
                    float sg = rng.NextFloat() < 0.5f ? -1 : 1;
                    plan.Add(Cell(rng.NextFloat() < DungeonDoorChance ? ObjectPart.DungeonDoor : ObjectPart.DungeonCorridor, ci, cj, sg * dz, -sg * dx));
                }
                Claim(used, ni, nj);
                open[r] |= 1 << d;
                ri.Add(ni); rj.Add(nj); open.Add(1 << ((d + 2) & 3));
            }
            for (int r = 0; r < ri.Count; r++)
                for (int d = 0; d < 4; d++)
                {
                    if ((open[r] & (1 << d)) != 0 || !(rng.NextFloat() < DungeonStubChance)) continue;
                    int ci = ri[r] + 2 * RoomDx[d], cj = rj[r] + 2 * RoomDz[d];
                    if (!FreeCell(used, ci, cj)) continue;
                    used[CellIndex(ci, cj)] = true;
                    var p = Cell(ObjectPart.DungeonCollapse, ci, cj, RoomDz[d], -RoomDx[d]);
                    p.Stub = true;
                    plan.Add(p);
                    open[r] |= 1 << d;
                }
            for (int r = 0; r < ri.Count; r++)
            {
                var p = Cell(ObjectPart.DungeonRoom, ri[r], rj[r], 1, 0);
                p.Open = open[r];
                plan.Add(p);
            }
            return plan;
        }

        static void Claim(bool[] used, int i, int j)
        {
            for (int a = -1; a <= 1; a++)
                for (int b = -1; b <= 1; b++) used[CellIndex(i + a, j + b)] = true;
        }

        static void Composite(StructureLayout l, ulong key, in Frame f, int trim)
        {
            var plan = DungeonLayout(key);
            for (int k = 0; k < plan.Count; k++)
            {
                var p = plan[k];
                var g = f.Child(p.X, p.Y, p.Z, p.C, p.S, scale: p.Scale);
                ulong pk = Rng.Mix(key ^ ((ulong)(k + 1) * 0x9E3779B97F4A7C15UL));
                int start = l.Pieces.Count;
                switch (p.Shape)
                {
                    case ObjectPart.DungeonCorridor: Corridor(l, pk, g, false); break;
                    case ObjectPart.DungeonCollapse: Corridor(l, pk, g, true); break;
                    case ObjectPart.DungeonDoor: Door(l, pk, g, trim); break;
                    case ObjectPart.DungeonStairs: Stairs(l, pk, g); break;
                    case ObjectPart.DungeonPillar: Pillar(l, pk, g, trim); break;
                    case ObjectPart.DungeonRoom: Room(l, pk, g, trim, p.Open); break;
                }
                for (int i = start; i < l.Pieces.Count; i++)
                {
                    var q = l.Pieces[i];
                    q.Base = g.Y;
                    l.Pieces[i] = q;
                }
            }
        }

        public static void DungeonPortals(in DungeonPiece p, List<float> xyz)
        {
            switch (p.Shape)
            {
                case ObjectPart.DungeonCorridor:
                case ObjectPart.DungeonDoor:
                case ObjectPart.DungeonCollapse:
                    Portal(p, 0, 0, -0.5f * KitCellM, xyz);
                    Portal(p, 0, 0, 0.5f * KitCellM, xyz);
                    break;
                case ObjectPart.DungeonStairs:
                    Portal(p, 0, 0, -0.5f * StairsM, xyz);
                    Portal(p, 0, KitStoreyM, 0.5f * StairsM, xyz);
                    break;
                case ObjectPart.DungeonRoom:
                    for (int d = 0; d < 4; d++)
                        if ((p.Open & (1 << d)) != 0) Portal(p, 0.5f * RoomM * RoomDx[d], 0, 0.5f * RoomM * RoomDz[d], xyz);
                    break;
            }
        }

        static void Portal(in DungeonPiece p, float x, float y, float z, List<float> xyz)
        {
            xyz.Add(p.X + p.Scale * (p.C * x - p.S * z));
            xyz.Add(p.Y + p.Scale * y);
            xyz.Add(p.Z + p.Scale * (p.S * x + p.C * z));
        }

        public static void DungeonFootprint(in DungeonPiece p, out float x0, out float x1, out float z0, out float z1)
        {
            float hx = p.Shape == ObjectPart.DungeonRoom ? 0.5f * RoomM : p.Shape == ObjectPart.DungeonPillar ? 0.6f : 0.5f * KitCellM;
            float hz = p.Shape == ObjectPart.DungeonRoom ? 0.5f * RoomM : p.Shape == ObjectPart.DungeonStairs ? 0.5f * StairsM : hx;
            Span(p, -hx, hx, -hz, hz, out x0, out x1, out z0, out z1);
        }

        static void Span(in DungeonPiece p, float lx0, float lx1, float lz0, float lz1, out float x0, out float x1, out float z0, out float z1)
        {
            x0 = z0 = float.MaxValue;
            x1 = z1 = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                float x = (k & 1) == 0 ? lx0 : lx1, z = (k & 2) == 0 ? lz0 : lz1;
                float wx = p.X + p.Scale * (p.C * x - p.S * z), wz = p.Z + p.Scale * (p.S * x + p.C * z);
                x0 = Math.Min(x0, wx); x1 = Math.Max(x1, wx);
                z0 = Math.Min(z0, wz); z1 = Math.Max(z1, wz);
            }
        }

        public static void DungeonVaults(ulong key, List<DungeonVault> list)
        {
            list.Clear();
            var plan = DungeonLayout(key);
            float hx = 0.5f * PortalWidthM + 0.3f, c = 0.5f * KitCellM;
            for (int k = 0; k < plan.Count; k++)
            {
                var p = plan[k];
                switch (p.Shape)
                {
                    case ObjectPart.DungeonCorridor:
                    case ObjectPart.DungeonDoor:
                        Vault(p, list, -hx, hx, -VaultFloor, PortalHeightM + 0.1f, -c - VaultReach - (k == 0 ? VaultPorch : 0), c + VaultReach, k == 0);
                        break;
                    case ObjectPart.DungeonCollapse:
                        Vault(p, list, -hx, hx, -VaultFloor, PortalHeightM + 0.1f, -c - VaultReach, 0.5f, false);
                        break;
                    case ObjectPart.DungeonRoom:
                        float a = 0.5f * RoomM - 0.5f;
                        Vault(p, list, -a, a, -VaultFloor, KitRoomHeightM + 0.1f, -a, a, false);
                        break;
                    case ObjectPart.DungeonStairs:
                        int segs = (int)Math.Round(StairsM);
                        for (int j = 0; j < segs; j++)
                        {
                            float za = -0.5f * StairsM + j, zb = za + 1;
                            Vault(p, list, -hx, hx, StairTop(za) - VaultFloor, Ceiling(j) + 0.1f, za - (j == 0 ? VaultReach : 0.25f), zb + (j == segs - 1 ? VaultReach : 0.25f), true);
                        }
                        break;
                }
            }
        }

        public static string DungeonLayoutFault(ulong key)
        {
            var plan = DungeonLayout(key);
            float half = 0.5f * DungeonPlotM;
            var boxes = new float[plan.Count * 4];
            for (int k = 0; k < plan.Count; k++)
            {
                DungeonFootprint(plan[k], out float x0, out float x1, out float z0, out float z1);
                if (x0 < -half || x1 > half || z0 < -half || z1 > half) return $"piece {k} leaves the {DungeonPlotM:F0} m plot";
                for (int m = 0; m < k; m++)
                    if (x0 < boxes[m * 4 + 1] - 0.01f && boxes[m * 4] < x1 - 0.01f && z0 < boxes[m * 4 + 3] - 0.01f && boxes[m * 4 + 2] < z1 - 0.01f) return $"piece {k} overlaps piece {m}";
                boxes[k * 4] = x0; boxes[k * 4 + 1] = x1; boxes[k * 4 + 2] = z0; boxes[k * 4 + 3] = z1;
            }
            var xyz = new List<float>();
            var owner = new List<int>();
            for (int k = 0; k < plan.Count; k++)
            {
                int n = xyz.Count;
                DungeonPortals(plan[k], xyz);
                for (int i = n; i < xyz.Count; i += 3) owner.Add(k);
            }
            var links = new List<int>[plan.Count];
            for (int k = 0; k < plan.Count; k++) links[k] = new List<int>();
            for (int a = 0; a < owner.Count; a++)
            {
                int hits = 0;
                for (int b = 0; b < owner.Count; b++)
                {
                    if (owner[b] == owner[a] || Math.Abs(xyz[a * 3] - xyz[b * 3]) > 0.01f || Math.Abs(xyz[a * 3 + 1] - xyz[b * 3 + 1]) > 0.01f || Math.Abs(xyz[a * 3 + 2] - xyz[b * 3 + 2]) > 0.01f) continue;
                    hits++;
                    links[owner[a]].Add(owner[b]);
                }
                bool loose = a == 0 || (plan[owner[a]].Stub && a > 0 && owner[a - 1] == owner[a]);
                if (hits > 1 || (hits == 0 && !loose)) return $"piece {owner[a]} has a doorway joined to {hits} pieces";
            }
            var seen = new bool[plan.Count];
            var queue = new Queue<int>();
            seen[0] = true;
            queue.Enqueue(0);
            while (queue.Count > 0)
                foreach (int m in links[queue.Dequeue()])
                    if (!seen[m]) { seen[m] = true; queue.Enqueue(m); }
            for (int k = 0; k < plan.Count; k++)
                if (!seen[k] && plan[k].Shape != ObjectPart.DungeonPillar) return $"piece {k} cannot be reached from the entrance";
            return null;
        }

        static void Vault(in DungeonPiece p, List<DungeonVault> list, float lx0, float lx1, float y0, float y1, float lz0, float lz1, bool bare)
        {
            Span(p, lx0, lx1, lz0, lz1, out float x0, out float x1, out float z0, out float z1);
            list.Add(new DungeonVault { X0 = x0, X1 = x1, Y0 = p.Y + y0, Y1 = p.Y + y1, Z0 = z0, Z1 = z1, Bare = bare });
        }
    }

    public sealed partial class ObjectField
    {
        public const float DungeonCoverM = 0.5f;

        public void DungeonEditsOf(ObjectInstance o, List<FeatureEdit> list, List<bool> bare = null)
        {
            var vaults = new List<DungeonVault>();
            Structures.DungeonVaults(ObjectMeshes.Key(lib.Objects[o.Def].Id, 0, o.Variant), vaults);
            float u = o.Size / Structures.DungeonM, r = Structures.VaultR * u, inset = Structures.VaultInset;
            foreach (var v in vaults)
            {
                float x0 = v.X0 + inset, x1 = v.X1 - inset, y0 = v.Y0 + inset, y1 = v.Y1 - inset, z0 = v.Z0 + inset, z1 = v.Z1 - inset;
                float ym = 0.5f * (y0 + y1), xm = 0.5f * (x0 + x1), zm = 0.5f * (z0 + z1);
                bool alongX = x1 - x0 >= z1 - z0;
                float ax = alongX ? x0 : xm, az = alongX ? zm : z0, bx = alongX ? x1 : xm, bz = alongX ? zm : z1;
                Rotate(o, ax * u, ym * u, az * u, out float wax, out float way, out float waz);
                Rotate(o, bx * u, ym * u, bz * u, out float wbx, out float wby, out float wbz);
                list.Add(new FeatureEdit
                {
                    Kind = FeatureEdit.Carve, Shape = FeatureEdit.Vault, R = r,
                    W = 0.5f * (alongX ? z1 - z0 : x1 - x0) * u, H = 0.5f * (y1 - y0) * u,
                    Ax = o.X + wax, Ay = o.Y + way, Az = o.Z + waz,
                    Bx = o.X + wbx, By = o.Y + wby, Bz = o.Z + wbz,
                }.Finer());
                bare?.Add(v.Bare);
            }
        }

        public FeatureEdit[] DungeonEdits()
        {
            var list = new List<FeatureEdit>();
            foreach (var o in Discoveries())
                if (lib.Objects[o.Def].Placement.Discovery == Dungeon) DungeonEditsOf(o, list);
            return list.ToArray();
        }

        public string DungeonFault(ObjectInstance o)
        {
            var layout = Structures.DungeonLayoutFault(ObjectMeshes.Key(lib.Objects[o.Def].Id, 0, o.Variant));
            if (layout != null) return "has a broken layout: " + layout;
            var vaults = new List<FeatureEdit>();
            var bare = new List<bool>();
            DungeonEditsOf(o, vaults, bare);
            float span = WorldChunks.Size * cm, cx0 = (float)Math.Floor(o.X / span) * span, cz0 = (float)Math.Floor(o.Z / span) * span;
            for (int q = 0; q < vaults.Count; q++)
            {
                var b = vaults[q];
                if (!Features.Inside(b, cm)) return "dungeon vault leaves its tile interior";
                if (b.MinX < cx0 || b.MaxX >= Math.Min(cx0 + span, worldM) || b.MinZ < cz0 || b.MaxZ >= Math.Min(cz0 + span, worldM)) return "dungeon vault leaves its object chunk";
                foreach (var f in edits)
                    if (f.Shape != FeatureEdit.Boulder && f.Shape != FeatureEdit.Spring && f.Shape != FeatureEdit.Vault && b.MaxX >= f.MinX && b.MinX <= f.MaxX && b.MaxZ >= f.MinZ && b.MinZ <= f.MaxZ) return "dungeon vault overlaps a terrain feature";
                float ex = b.Bx - b.Ax, ez = b.Bz - b.Az, el = (float)Math.Sqrt(ex * ex + ez * ez);
                float nx = el > 0 ? -ez / el * b.W : 0, nz = el > 0 ? ex / el * b.W : b.W;
                for (int s = 0; s < 9; s++)
                {
                    float t = 0.5f * (s % 3), side = s / 3 - 1, x = b.Ax + t * ex + side * nx, z = b.Az + t * ez + side * nz;
                    int gx = Math.Max(0, Math.Min(n - 1, (int)(x / cm + 0.5f))), gz = Math.Max(0, Math.Min(n - 1, (int)(z / cm + 0.5f)));
                    if (w.Sea[gz * n + gx]) return "dungeon vault lies under the sea";
                    if (w.Water.LakeId[gz * n + gx] >= 0) return "dungeon vault lies under a lake";
                    if (RiverGap(x, z) <= b.R + b.W + 2) return "dungeon vault lies under a river";
                    if (!bare[q] && GroundAt(detail, cm, x, z) < b.MaxY + DungeonCoverM) return "dungeon vault breaks the surface";
                }
            }
            return null;
        }
    }
}
