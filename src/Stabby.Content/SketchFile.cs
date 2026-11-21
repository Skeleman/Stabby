using System;
using System.Collections.Generic;
using System.IO;
using Stabby.World;

namespace Stabby.Content
{
    public static class SketchFile
    {
        public const int SchemaVersion = 5;

        public static string Write(WorldSketch s, WorldConfig c)
        {
            var w = new JsonWriter().BeginObject();
            w.Key("schemaVersion").Value(SchemaVersion);
            w.Key("seed").Value(c.Seed);
            w.Key("size").Value(s.Size);
            w.Key("cellMeters").Value(c.CellMeters);
            Masses(w, "landmasses", s.Landmasses);
            Masses(w, "islets", s.Islets);
            w.Key("ranges").BeginArray();
            foreach (var r in s.Ranges) Line(w, r.X, r.Y, r.Width).Key("strength").Value(r.Strength).EndObject();
            w.EndArray();
            w.Key("corridors").BeginArray();
            foreach (var r in s.Corridors) Line(w, r.X, r.Y, r.Width).EndObject();
            w.EndArray();
            w.Key("regions").BeginArray();
            foreach (var r in s.Regions)
                w.Item().BeginObject().Key("biome").Value(r.Biome).Key("x").Value(r.X).Key("y").Value(r.Y).Key("radius").Value(r.Radius).EndObject();
            w.EndArray();
            w.Key("landmarks").BeginArray();
            foreach (var l in s.Landmarks)
                w.Item().BeginObject().Key("kind").Value(l.Kind).Key("x").Value(l.X).Key("y").Value(l.Y).EndObject();
            w.EndArray();
            Masses(w, "lakes", s.Lakes);
            w.Key("shores").BeginArray();
            foreach (var h in s.Shores) Spot(w, h.Cliff ? "cliff" : "beach", h.X, h.Y, h.Radius);
            w.EndArray();
            w.Key("highlands").BeginArray();
            foreach (var h in s.Highlands) Spot(w, h.Plateau ? "plateau" : "peak", h.X, h.Y, h.Radius);
            w.EndArray();
            return w.EndObject().ToString();
        }

        static void Masses(JsonWriter w, string key, List<LandmassSketch> list)
        {
            w.Key(key).BeginArray();
            foreach (var m in list)
            {
                w.Item().BeginObject().Key("x").Value(m.X).Key("y").Value(m.Y).Key("radius").Value(m.Radius);
                if (m.Lobes.Count > 0)
                {
                    w.Key("lobes").BeginArray();
                    foreach (var l in m.Lobes)
                        w.Item().BeginObject().Key("x").Value(l.X).Key("y").Value(l.Y).Key("radius").Value(l.Radius).EndObject();
                    w.EndArray();
                }
                w.EndObject();
            }
            w.EndArray();
        }

        static JsonWriter Line(JsonWriter w, float[] x, float[] y, float width)
        {
            w.Item().BeginObject();
            w.Key("x").Floats(x);
            w.Key("y").Floats(y);
            return w.Key("width").Value(width);
        }

        static void Spot(JsonWriter w, string kind, float x, float y, float radius) =>
            w.Item().BeginObject().Key("kind").Value(kind).Key("x").Value(x).Key("y").Value(y).Key("radius").Value(radius).EndObject();

        sealed class LineEntry
        {
            public float[] X, Y;
            public float Width;
            public float? Strength;
        }

        sealed class SpotEntry
        {
            public string Kind;
            public float X, Y, Radius;
        }

        public static WorldSketch Read(string json, WorldConfig c)
        {
            var d = Json.Parse(json, "sketch");
            if (d.IsNull) throw new InvalidDataException("empty sketch");
            int version = d.Int("schemaVersion", 0);
            ulong seed = d.ULong("seed", 0);
            int size = d.Int("size", 0);
            float cell = d.Float("cellMeters", 0);
            var landmasses = Masses(d, "landmasses");
            var islets = Masses(d, "islets");
            var ranges = Lines(d, "ranges");
            var corridors = Lines(d, "corridors");
            var regions = new List<RegionSketch>();
            foreach (var r in d.Objects("regions"))
            {
                regions.Add(new RegionSketch { Biome = r.String("biome", ""), X = r.Float("x", 0), Y = r.Float("y", 0), Radius = r.Float("radius", 0) });
                r.End();
            }
            var landmarks = new List<LandmarkSketch>();
            foreach (var l in d.Objects("landmarks"))
            {
                landmarks.Add(new LandmarkSketch { Kind = l.String("kind", ""), X = l.Float("x", 0), Y = l.Float("y", 0) });
                l.End();
            }
            var lakes = Masses(d, "lakes");
            var shores = Spots(d, "shores");
            var highlands = Spots(d, "highlands");
            d.End();

            if (size < 16 || cell <= 0) throw new InvalidDataException("sketch needs size and cellMeters");
            if (version != SchemaVersion) throw new InvalidDataException("sketch schemaVersion " + version + ", expected " + SchemaVersion);
            var errors = new List<string>();
            foreach (var l in ranges) LineCheck(l, errors);
            foreach (var l in corridors) LineCheck(l, errors);
            if (ranges.Exists(r => r.Strength == null)) errors.Add("ranges need strength");
            if (corridors.Exists(r => r.Strength != null)) errors.Add("corridors have no strength");
            if (shores.Exists(h => h.Kind != "cliff" && h.Kind != "beach")) errors.Add("shore kind is cliff or beach");
            if (highlands.Exists(h => h.Kind != "peak" && h.Kind != "plateau")) errors.Add("highland kind is peak or plateau");
            if (errors.Count > 0) throw new InvalidDataException(string.Join("; ", errors));

            c.Seed = seed; c.Size = size; c.CellMeters = cell;
            var s = new WorldSketch { Landmasses = landmasses, Islets = islets, Regions = regions, Landmarks = landmarks, Lakes = lakes };
            foreach (var r in ranges) s.Ranges.Add(new RangeSketch { X = r.X, Y = r.Y, Width = r.Width, Strength = r.Strength.Value });
            foreach (var r in corridors) s.Corridors.Add(new CorridorSketch { X = r.X, Y = r.Y, Width = r.Width });
            foreach (var h in shores) s.Shores.Add(new ShoreSketch { Cliff = h.Kind == "cliff", X = h.X, Y = h.Y, Radius = h.Radius });
            foreach (var h in highlands) s.Highlands.Add(new HighlandSketch { Plateau = h.Kind == "plateau", X = h.X, Y = h.Y, Radius = h.Radius });
            s.Build(c);
            return s;
        }

        static void LineCheck(LineEntry l, List<string> errors)
        {
            if (l.X.Length < 2 || l.X.Length != l.Y.Length) errors.Add("a line needs matching x and y with at least 2 points");
        }

        static List<LandmassSketch> Masses(Json d, string key)
        {
            var list = new List<LandmassSketch>();
            foreach (var m in d.Objects(key))
            {
                var mass = new LandmassSketch { X = m.Float("x", 0), Y = m.Float("y", 0), Radius = m.Float("radius", 0) };
                foreach (var l in m.Objects("lobes"))
                {
                    mass.Lobes.Add(new LobeSketch { X = l.Float("x", 0), Y = l.Float("y", 0), Radius = l.Float("radius", 0) });
                    l.End();
                }
                m.End();
                list.Add(mass);
            }
            return list;
        }

        static List<LineEntry> Lines(Json d, string key)
        {
            var list = new List<LineEntry>();
            foreach (var l in d.Objects(key))
            {
                list.Add(new LineEntry
                {
                    X = l.Floats("x", Array.Empty<float>()), Y = l.Floats("y", Array.Empty<float>()),
                    Width = l.Float("width", 0), Strength = l.FloatOrNull("strength"),
                });
                l.End();
            }
            return list;
        }

        static List<SpotEntry> Spots(Json d, string key)
        {
            var list = new List<SpotEntry>();
            foreach (var h in d.Objects(key))
            {
                list.Add(new SpotEntry { Kind = h.String("kind", ""), X = h.Float("x", 0), Y = h.Float("y", 0), Radius = h.Float("radius", 0) });
                h.End();
            }
            return list;
        }
    }
}
