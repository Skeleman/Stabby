using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class CreatureSegmentDef
    {
        public const int ToneBase = 0, ToneAccent = 1, ToneDark = 2;
        public static readonly string[] ToneNames = { "base", "accent", "dark" };

        public float Length, RadiusStart, RadiusEnd, Flatten = 1;
        public int Tone;
        public float Belly, Rump, Tip;
        public float[] Idle = new float[3], Reach = new float[3], Push = new float[3], Lift = new float[3];
        public float[] RunReach = new float[3], RunPush = new float[3], RunLift = new float[3], Alert = new float[3];
    }

    public sealed class CreaturePartDef
    {
        public const int Leg = 0, Neck = 1, Head = 2, Tail = 3, Ear = 4, Eye = 5;
        public static readonly string[] RoleNames = { "leg", "neck", "head", "tail", "ear", "eye" };

        public string Id;
        public int Role;
        public List<CreatureSegmentDef> Segments = new List<CreatureSegmentDef>();
    }

    public sealed class CreatureSlot
    {
        public string Part, Parent;
        public float[] Socket = new float[3], Direction = new float[3];
        public bool Mirror;
        public float Phase, MirrorPhase, RunPhase, RunMirrorPhase, Scale = 1;
    }

    public sealed class CreatureDef
    {
        public string Id, Name;
        public List<string> Biomes = new List<string>();
        public float Length = 1, Radius = 0.2f, Taper = 1, Flatten = 1;
        public float Haunch, Waist, Hump;
        public uint Base, Accent, Dark;
        public float SizeMin = 1, SizeMax = 1, WalkSpeed = 1, Jitter;
        public int HerdMin = 1, HerdMax = 1;
        public float HerdChance;
        public float AlertPixels = 25.7f, FleePixels = 46.8f, CalmPixels = 14.7f, RunSpeed = 6.5f, RunStride = 2.4f;
        public float SpawnPixels = 4.32f, FadePixels = 6.48f, ShadowPixels = 32.4f, IkPixels = 64.8f, AnimPixels = 10.8f, LowPixels = 32.4f;
        public List<CreatureSlot> Slots = new List<CreatureSlot>();
    }

    public sealed class CreatureShape
    {
        public float Length, RadiusStart, RadiusEnd, Flatten = 1;
        public int Tone;
        public float Belly, Rump, Tip;
        public float Haunch, Waist, Hump;
        public bool Sphere => Length <= 0;
    }

    public sealed class CreatureLibrary
    {
        public List<CreaturePartDef> Parts = new List<CreaturePartDef>();
        public List<CreatureDef> Bodies = new List<CreatureDef>();

        public CreaturePartDef Part(string id)
        {
            foreach (var p in Parts) if (p.Id == id) return p;
            return null;
        }

        public CreatureDef Body(string id)
        {
            foreach (var b in Bodies) if (b.Id == id) return b;
            return null;
        }

        public static List<string> Validate(CreatureLibrary lib, IReadOnlyList<string> biomeIds)
        {
            var errors = new List<string>();
            var ids = new HashSet<string>();
            foreach (var p in lib.Parts)
            {
                if (string.IsNullOrEmpty(p.Id) || !ids.Add(p.Id)) errors.Add("part id \"" + p.Id + "\" is empty or repeated");
                if (p.Role < 0) errors.Add(p.Id + ": unknown role");
                if (p.Segments.Count == 0) errors.Add(p.Id + ": needs at least one segment");
                if (p.Role == CreaturePartDef.Leg && p.Segments.Count < 2) errors.Add(p.Id + ": a leg needs two segments for its knee");
                for (int i = 0; i < p.Segments.Count; i++)
                {
                    var s = p.Segments[i];
                    string at = p.Id + " segment " + i;
                    if (s.Length < 0 || s.Length > 3) errors.Add(at + ": length out of 0..3");
                    if (s.RadiusStart <= 0 || s.RadiusEnd <= 0 || s.RadiusStart > 1 || s.RadiusEnd > 1) errors.Add(at + ": radius out of (0, 1]");
                    if (s.Flatten <= 0 || s.Flatten > 3) errors.Add(at + ": flatten out of (0, 3]");
                    if (s.Tone < 0) errors.Add(at + ": unknown tone");
                    if (s.Belly < 0 || s.Belly > 1 || s.Rump < 0 || s.Rump > 1 || s.Tip < 0 || s.Tip > 1) errors.Add(at + ": belly/rump/tip out of 0..1");
                    if (s.Idle.Length != 3 || s.Reach.Length != 3 || s.Push.Length != 3 || s.Lift.Length != 3
                        || s.RunReach.Length != 3 || s.RunPush.Length != 3 || s.RunLift.Length != 3 || s.Alert.Length != 3) errors.Add(at + ": poses need 3 angles");
                }
            }
            var bodyIds = new HashSet<string>();
            if (lib.Bodies.Count > CreaturePlan.MaxBodies) errors.Add("more than " + CreaturePlan.MaxBodies + " creature bodies");
            foreach (var b in lib.Bodies)
            {
                if (string.IsNullOrEmpty(b.Id) || !bodyIds.Add(b.Id)) errors.Add("body id \"" + b.Id + "\" is empty or repeated");
                foreach (var biome in b.Biomes)
                    if (biomeIds != null && !Contains(biomeIds, biome)) errors.Add(b.Id + ": unknown biome \"" + biome + "\"");
                if (b.Length <= 0 || b.Radius <= 0 || b.Taper <= 0 || b.Flatten <= 0) errors.Add(b.Id + ": length, radius, taper and flatten must be positive");
                if (b.Length < 2 * b.Radius * Math.Max(1, b.Taper)) errors.Add(b.Id + ": body shorter than its ends");
                if (b.SizeMin <= 0 || b.SizeMax < b.SizeMin) errors.Add(b.Id + ": size range is empty");
                if (b.HerdMin < 1 || b.HerdMax < b.HerdMin) errors.Add(b.Id + ": herd range is empty");
                if (b.HerdMax > CreaturePlan.MaxHerd) errors.Add(b.Id + ": herd above " + CreaturePlan.MaxHerd);
                if (b.HerdChance < 0 || b.HerdChance > 1) errors.Add(b.Id + ": herdChance out of 0..1");
                if (!(b.SpawnPixels > 0 && b.SpawnPixels < b.FadePixels)) errors.Add(b.Id + ": spawnPixels must be positive and below fadePixels");
                if (b.WalkSpeed <= 0) errors.Add(b.Id + ": walkSpeed must be positive");
                if (!(b.RunSpeed > b.WalkSpeed)) errors.Add(b.Id + ": runSpeed must be above walkSpeed");
                if (!(b.RunStride > 0)) errors.Add(b.Id + ": runStride must be positive");
                if (!(b.AlertPixels > 0 && b.FleePixels > b.AlertPixels)) errors.Add(b.Id + ": fleePixels must be above alertPixels");
                if (!(b.CalmPixels <= b.AlertPixels * (1f - Detail.Hysteresis))) errors.Add(b.Id + ": calmPixels must clear alertPixels by the hysteresis");
                if (!(b.CalmPixels > b.SpawnPixels)) errors.Add(b.Id + ": calmPixels must stay above spawnPixels");
                if (b.Jitter < 0 || b.Jitter > 0.25f) errors.Add(b.Id + ": jitter out of 0..0.25");
                if (!(b.FadePixels > 0 && b.AnimPixels > b.FadePixels && b.ShadowPixels > b.FadePixels && b.IkPixels > b.AnimPixels))
                    errors.Add(b.Id + ": detail pixels must rise fade < anim < ik and fade < shadow");
                if (!(b.LowPixels >= 2 * b.FadePixels)) errors.Add(b.Id + ": lowPixels must be at least twice fadePixels");
                if (b.Haunch < 0 || b.Haunch > 0.5f || b.Waist < 0 || b.Waist > 0.5f || b.Hump < 0 || b.Hump > 0.5f) errors.Add(b.Id + ": haunch, waist and hump out of 0..0.5");
                var seen = new List<string>();
                foreach (var s in b.Slots)
                {
                    var part = lib.Part(s.Part);
                    if (part == null) errors.Add(b.Id + ": slot names unknown part \"" + s.Part + "\"");
                    if (s.Parent != null && !seen.Contains(s.Parent)) errors.Add(b.Id + ": slot " + s.Part + " hangs from \"" + s.Parent + "\", not an earlier slot");
                    if (s.Socket.Length != 3 || s.Direction.Length != 3) errors.Add(b.Id + ": slot " + s.Part + " socket/direction need 3 numbers");
                    if (s.Scale <= 0) errors.Add(b.Id + ": slot " + s.Part + " scale must be positive");
                    if (part != null && part.Role == CreaturePartDef.Leg && s.Parent != null) errors.Add(b.Id + ": leg " + s.Part + " must hang from the body");
                    seen.Add(s.Part);
                }
            }
            return errors;
        }

        static bool Contains(IReadOnlyList<string> list, string s)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == s) return true;
            return false;
        }
    }

    public sealed class CreaturePartInstance
    {
        public int Parent, Role, FirstSegment, SegmentCount;
        public float SocketX, SocketY, SocketZ;
        public float RestX, RestY, RestZ;
        public float Phase, RunPhase, Side;
    }

    public sealed class CreatureSegmentInstance
    {
        public int Shape;
        public float Length, Radius, Flatten;
        public float[] Idle = new float[3], Reach = new float[3], Push = new float[3], Lift = new float[3];
        public float[] RunReach = new float[3], RunPush = new float[3], RunLift = new float[3], Alert = new float[3];
    }

    public sealed class CreatureBody
    {
        public const float Radians = 0.017453292f;

        public float Size, Length, RearRadius, FrontRadius, Flatten, WalkSpeed;
        public float[] Base = new float[3], Accent = new float[3], Dark = new float[3];
        public CreatureSegmentInstance Trunk;
        public List<CreaturePartInstance> Parts = new List<CreaturePartInstance>();
        public List<CreatureSegmentInstance> Segments = new List<CreatureSegmentInstance>();

        public static List<CreatureShape> Shapes(CreatureDef def, CreatureLibrary lib)
        {
            float rear = def.Radius * def.Taper, front = def.Radius;
            var shapes = new List<CreatureShape>
            {
                new CreatureShape { Length = Math.Max(0, def.Length - rear - front), RadiusStart = rear, RadiusEnd = front, Flatten = def.Flatten, Belly = 1, Rump = 1, Haunch = def.Haunch, Waist = def.Waist, Hump = def.Hump },
            };
            foreach (var id in UsedParts(def))
                foreach (var s in lib.Part(id).Segments)
                    shapes.Add(new CreatureShape { Length = s.Length, RadiusStart = s.RadiusStart, RadiusEnd = s.RadiusEnd, Flatten = s.Flatten, Tone = s.Tone, Belly = s.Belly, Rump = s.Rump, Tip = s.Tip });
            return shapes;
        }

        static List<string> UsedParts(CreatureDef def)
        {
            var used = new List<string>();
            foreach (var s in def.Slots) if (!used.Contains(s.Part)) used.Add(s.Part);
            return used;
        }

        static void Gait(CreatureDef def, ref Rng rng, out float size, out float thick, out float walk)
        {
            size = rng.Range(def.SizeMin, def.SizeMax);
            thick = 1 + rng.Range(-def.Jitter, def.Jitter);
            walk = def.WalkSpeed * (1 + rng.Range(-def.Jitter, def.Jitter));
        }

        public static float WalkSpeedFor(CreatureDef def, ulong seed)
        {
            var rng = Rng.Stream(seed, "creature-body");
            Gait(def, ref rng, out _, out _, out float walk);
            return walk;
        }

        public static CreatureBody Assemble(CreatureDef def, CreatureLibrary lib, ulong seed)
        {
            var rng = Rng.Stream(seed, "creature-body");
            var used = UsedParts(def);
            var firstShape = new int[used.Count];
            for (int i = 0, k = 1; i < used.Count; i++) { firstShape[i] = k; k += lib.Part(used[i]).Segments.Count; }

            Gait(def, ref rng, out float size, out float thick, out float walk);
            var body = new CreatureBody
            {
                Size = size, Length = def.Length * size, RearRadius = def.Radius * def.Taper * size * thick, FrontRadius = def.Radius * size * thick,
                Flatten = def.Flatten, WalkSpeed = walk,
            };
            float shade = 1 + rng.Range(-0.12f, 0.12f), warm = rng.Range(-0.05f, 0.05f);
            Tint(def.Base, shade, warm, body.Base);
            Tint(def.Accent, 1 + rng.Range(-0.05f, 0.05f), warm * 0.5f, body.Accent);
            Tint(def.Dark, 1 + rng.Range(-0.1f, 0.1f), 0, body.Dark);
            body.Trunk = new CreatureSegmentInstance { Shape = 0, Length = Math.Max(0, body.Length - body.RearRadius - body.FrontRadius), Radius = size * thick, Flatten = def.Flatten };

            float half = body.Length * 0.5f;
            var copies = new List<List<int>>();
            for (int si = 0; si < def.Slots.Count; si++)
            {
                var slot = def.Slots[si];
                var part = lib.Part(slot.Part);
                float scale = size * slot.Scale;
                float stretch = 1 + rng.Range(-def.Jitter, def.Jitter);
                var parents = new List<int>();
                if (slot.Parent == null) parents.Add(-1);
                else
                    for (int k = si - 1; k >= 0; k--)
                        if (def.Slots[k].Part == slot.Parent) { parents.AddRange(copies[k]); break; }
                var made = new List<int>();
                foreach (var parent in parents)
                {
                    float parentSide = parent < 0 ? 1 : body.Parts[parent].Side;
                    int sides = slot.Mirror && parent < 0 ? 2 : 1;
                    for (int m = 0; m < sides; m++)
                    {
                        float side = parent < 0 ? (m == 0 ? 1 : -1) : parentSide;
                        float sx = slot.Socket[0], sy = slot.Socket[1], sz = slot.Socket[2];
                        if (parent < 0)
                        {
                            float r = Lerp(body.RearRadius, body.FrontRadius, sz * 0.5f + 0.5f);
                            sx *= r; sy *= r * def.Flatten; sz *= half;
                        }
                        else { sx *= size; sy *= size; sz *= size; }
                        var inst = new CreaturePartInstance
                        {
                            Parent = parent, Role = part.Role, FirstSegment = body.Segments.Count, SegmentCount = part.Segments.Count,
                            SocketX = sx * side, SocketY = sy, SocketZ = sz,
                            RestX = slot.Direction[0] * Radians, RestY = slot.Direction[1] * Radians * side, RestZ = slot.Direction[2] * Radians * side,
                            Phase = Frac(slot.Phase + (side < 0 && slot.Mirror ? slot.MirrorPhase : 0)),
                            RunPhase = Frac(slot.RunPhase + (side < 0 && slot.Mirror ? slot.RunMirrorPhase : 0)), Side = side,
                        };
                        int shape = firstShape[used.IndexOf(slot.Part)];
                        for (int j = 0; j < part.Segments.Count; j++)
                        {
                            var s = part.Segments[j];
                            var seg = new CreatureSegmentInstance { Shape = shape + j, Length = s.Length * scale * stretch, Radius = scale, Flatten = s.Flatten };
                            Poses(seg, s, side);
                            body.Segments.Add(seg);
                        }
                        made.Add(body.Parts.Count);
                        body.Parts.Add(inst);
                        if (slot.Mirror && parent >= 0)
                        {
                            var twin = new CreaturePartInstance
                            {
                                Parent = parent, Role = inst.Role, FirstSegment = body.Segments.Count, SegmentCount = inst.SegmentCount,
                                SocketX = -inst.SocketX, SocketY = inst.SocketY, SocketZ = inst.SocketZ,
                                RestX = inst.RestX, RestY = -inst.RestY, RestZ = -inst.RestZ,
                                Phase = Frac(slot.Phase + slot.MirrorPhase), RunPhase = Frac(slot.RunPhase + slot.RunMirrorPhase), Side = -side,
                            };
                            for (int j = 0; j < part.Segments.Count; j++)
                            {
                                var src = body.Segments[inst.FirstSegment + j];
                                var s = part.Segments[j];
                                var seg = new CreatureSegmentInstance { Shape = src.Shape, Length = src.Length, Radius = src.Radius, Flatten = src.Flatten };
                                Poses(seg, s, -side);
                                body.Segments.Add(seg);
                            }
                            made.Add(body.Parts.Count);
                            body.Parts.Add(twin);
                        }
                    }
                }
                copies.Add(made);
            }
            return body;
        }

        static void Poses(CreatureSegmentInstance seg, CreatureSegmentDef s, float side)
        {
            seg.Idle = Pose(s.Idle, side); seg.Reach = Pose(s.Reach, side); seg.Push = Pose(s.Push, side); seg.Lift = Pose(s.Lift, side);
            seg.RunReach = Pose(s.RunReach, side); seg.RunPush = Pose(s.RunPush, side); seg.RunLift = Pose(s.RunLift, side); seg.Alert = Pose(s.Alert, side);
        }

        static float[] Pose(float[] deg, float side) => new[] { deg[0] * Radians, deg[1] * Radians * side, deg[2] * Radians * side };

        static void Tint(uint rgb, float shade, float warm, float[] into)
        {
            float r = ((rgb >> 16) & 255) / 255f, g = ((rgb >> 8) & 255) / 255f, b = (rgb & 255) / 255f;
            into[0] = Clamp01(r * shade * (1 + warm));
            into[1] = Clamp01(g * shade);
            into[2] = Clamp01(b * shade * (1 - warm));
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Frac(float x) => x - (float)Math.Floor(x);
        static float Clamp01(float x) => x < 0 ? 0 : x > 1 ? 1 : x;
    }
    public struct CreatureSpawn
    {
        public int Id, Body;
        public float X, Z, Heading, WalkSpeed;
        public ulong BodySeed, HerdSeed;
        public uint Seed;
    }

    public static class CreaturePlan
    {
        public const int SiteCells = 16, Tries = 16, MaxHerd = 16, MaxBodies = 16;
        public const float HerdSpread = 4f;

        public static float SpawnReach(CreatureDef def) => Detail.SimulationReach(def.Length, def.SpawnPixels);

        public static float DespawnReach(CreatureDef def) => SpawnReach(def) / (1f - Detail.Hysteresis);

        public static int SitesAcross(int size) => (size + SiteCells - 1) / SiteCells;

        public static int SiteOf(int id) => id >> 8;

        public static int HerdOf(int id) => id >> 4;

        public static ulong HerdSeed(ulong root, int herd) => Rng.Stream(root ^ Rng.Mix((ulong)herd), "creature-herd").NextU64();

        public static CreatureWary Wary(CreatureDef def) =>
            new CreatureWary
            {
                Alert = Detail.SimulationReach(def.Length, def.AlertPixels), Flee = Detail.SimulationReach(def.Length, def.FleePixels),
                Calm = Detail.SimulationReach(def.Length, def.CalmPixels), RunRatio = def.RunSpeed / def.WalkSpeed,
                Look = def.RunSpeed * HerdMind.LookSeconds,
            };

        public static bool Wet(GeneratedWorld w, int i) => w.Sea[i] || w.Water.LakeId[i] >= 0 || w.Water.IsRiver[i];

        public static WaterGround Ground(GeneratedWorld w)
        {
            var wet = new byte[w.Sea.Length];
            for (int i = 0; i < wet.Length; i++) wet[i] = Wet(w, i) ? (byte)1 : (byte)0;
            return new WaterGround { Wet = wet, Size = w.Config.Size, CellMeters = w.Config.CellMeters };
        }

        public static ulong Root(GeneratedWorld w) => Rng.Stream(w.Config.Seed, "creatures").NextU64();

        public static List<CreatureSpawn> Build(GeneratedWorld w, CreatureLibrary lib)
        {
            var list = new List<CreatureSpawn>();
            ulong root = Root(w);
            int across = SitesAcross(w.Config.Size);
            for (int s = 0; s < across * across; s++) Site(w, lib, root, s, list);
            return list;
        }

        public static void Site(GeneratedWorld w, CreatureLibrary lib, ulong root, int site, List<CreatureSpawn> list)
        {
            int n = w.Config.Size, across = SitesAcross(n);
            int sx = site % across * SiteCells, sz = site / across * SiteCells;
            float cm = w.Config.CellMeters;
            for (int b = 0; b < lib.Bodies.Count; b++)
            {
                var def = lib.Bodies[b];
                var rng = new Rng(Rng.Mix(root ^ (ulong)site * 0x9E3779B97F4A7C15UL ^ (ulong)(b + 1) * 0xC2B2AE3D27D4EB4FUL));
                if (rng.NextFloat() >= def.HerdChance) continue;
                int cell = -1;
                for (int t = 0; t < Tries && cell < 0; t++)
                {
                    int x = sx + rng.Range(0, SiteCells), z = sz + rng.Range(0, SiteCells);
                    if (x < n && z < n && Habitable(w, def, x, z)) cell = z * n + x;
                }
                if (cell < 0) continue;
                float cx = (cell % n + 0.5f) * cm, cz = (cell / n + 0.5f) * cm;
                int count = rng.Range(def.HerdMin, def.HerdMax + 1);
                ulong herdSeed = HerdSeed(root, HerdOf(site << 8 | b << 4));
                float heading = rng.Range(0f, 6.2831853f);
                for (int k = 0; k < count; k++)
                {
                    ulong seed = rng.NextU64();
                    list.Add(new CreatureSpawn
                    {
                        Id = site << 8 | b << 4 | k, Body = b,
                        X = cx + rng.Range(-HerdSpread, HerdSpread), Z = cz + rng.Range(-HerdSpread, HerdSpread),
                        Heading = heading + rng.Range(-0.7f, 0.7f), BodySeed = seed, Seed = rng.NextU32() | 1u, HerdSeed = herdSeed,
                        WalkSpeed = CreatureBody.WalkSpeedFor(def, seed),
                    });
                }
            }
        }

        public static bool Habitable(GeneratedWorld w, CreatureDef def, int x, int z)
        {
            int n = w.Config.Size;
            if (x <= 0 || z <= 0 || x >= n - 1 || z >= n - 1) return false;
            int i = z * n + x;
            if (Wet(w, i)) return false;
            if (w.Biome == null || !def.Biomes.Contains(w.BiomeIds[w.Biome[i]])) return false;
            float cm = w.Config.CellMeters;
            float gx = w.Height[i + 1] - w.Height[i - 1], gz = w.Height[i + n] - w.Height[i - n];
            return gx * gx + gz * gz <= 0.64f * cm * cm;
        }
    }
}
