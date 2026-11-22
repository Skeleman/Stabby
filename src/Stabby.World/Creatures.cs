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
        public float Phase, MirrorPhase, Scale = 1;
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
        public float FadePixels = 6.48f, ShadowPixels = 32.4f, IkPixels = 64.8f, AnimPixels = 10.8f, LowPixels = 32.4f;
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
                    if (s.Idle.Length != 3 || s.Reach.Length != 3 || s.Push.Length != 3 || s.Lift.Length != 3) errors.Add(at + ": poses need 3 angles");
                }
            }
            var bodyIds = new HashSet<string>();
            foreach (var b in lib.Bodies)
            {
                if (string.IsNullOrEmpty(b.Id) || !bodyIds.Add(b.Id)) errors.Add("body id \"" + b.Id + "\" is empty or repeated");
                foreach (var biome in b.Biomes)
                    if (biomeIds != null && !Contains(biomeIds, biome)) errors.Add(b.Id + ": unknown biome \"" + biome + "\"");
                if (b.Length <= 0 || b.Radius <= 0 || b.Taper <= 0 || b.Flatten <= 0) errors.Add(b.Id + ": length, radius, taper and flatten must be positive");
                if (b.Length < 2 * b.Radius * Math.Max(1, b.Taper)) errors.Add(b.Id + ": body shorter than its ends");
                if (b.SizeMin <= 0 || b.SizeMax < b.SizeMin) errors.Add(b.Id + ": size range is empty");
                if (b.HerdMin < 1 || b.HerdMax < b.HerdMin) errors.Add(b.Id + ": herd range is empty");
                if (b.WalkSpeed <= 0) errors.Add(b.Id + ": walkSpeed must be positive");
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
        public float Phase, Side;
    }

    public sealed class CreatureSegmentInstance
    {
        public int Shape;
        public float Length, Radius, Flatten;
        public float[] Idle = new float[3], Reach = new float[3], Push = new float[3], Lift = new float[3];
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

        public static CreatureBody Assemble(CreatureDef def, CreatureLibrary lib, ulong seed)
        {
            var rng = Rng.Stream(seed, "creature-body");
            var used = UsedParts(def);
            var firstShape = new int[used.Count];
            for (int i = 0, k = 1; i < used.Count; i++) { firstShape[i] = k; k += lib.Part(used[i]).Segments.Count; }

            float size = rng.Range(def.SizeMin, def.SizeMax);
            float thick = 1 + rng.Range(-def.Jitter, def.Jitter);
            var body = new CreatureBody
            {
                Size = size, Length = def.Length * size, RearRadius = def.Radius * def.Taper * size * thick, FrontRadius = def.Radius * size * thick,
                Flatten = def.Flatten, WalkSpeed = def.WalkSpeed * (1 + rng.Range(-def.Jitter, def.Jitter)),
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
                            Phase = Frac(slot.Phase + (side < 0 && slot.Mirror ? slot.MirrorPhase : 0)), Side = side,
                        };
                        int shape = firstShape[used.IndexOf(slot.Part)];
                        for (int j = 0; j < part.Segments.Count; j++)
                        {
                            var s = part.Segments[j];
                            body.Segments.Add(new CreatureSegmentInstance
                            {
                                Shape = shape + j, Length = s.Length * scale * stretch, Radius = scale, Flatten = s.Flatten,
                                Idle = Pose(s.Idle, side), Reach = Pose(s.Reach, side), Push = Pose(s.Push, side), Lift = Pose(s.Lift, side),
                            });
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
                                Phase = Frac(slot.Phase + slot.MirrorPhase), Side = -side,
                            };
                            for (int j = 0; j < part.Segments.Count; j++)
                            {
                                var src = body.Segments[inst.FirstSegment + j];
                                var s = part.Segments[j];
                                body.Segments.Add(new CreatureSegmentInstance
                                {
                                    Shape = src.Shape, Length = src.Length, Radius = src.Radius, Flatten = src.Flatten,
                                    Idle = Pose(s.Idle, -side), Reach = Pose(s.Reach, -side), Push = Pose(s.Push, -side), Lift = Pose(s.Lift, -side),
                                });
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
}
