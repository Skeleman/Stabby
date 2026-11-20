namespace Stabby.World
{
    public enum Wind { FromWest, FromEast, FromSouth, FromNorth }

    // Grid y grows northward. Heights are metres, time is years, precipitation is mm/yr.
    public sealed class WorldConfig
    {
        public const int GeneratorVersion = 17;

        public ulong Seed = 1;
        // The main island is about 12 km across, between Punchy's 8 and the old 16 (Zach 11-20).
        public const float MapMeters = 23040f;
        public int Size = 512;
        public float CellMeters = MapMeters / 512;
        public static float CellMetersFor(int size) => MapMeters / size;

        // Islands: the second has SecondIslandScale times the first's radius,
        // so about half its area at 0.71.
        public int Landmasses = 2;
        public float MainIslandRadius = 0.26f, SecondIslandScale = 0.71f;
        public int MainLobes = 6, SecondLobes = 4;
        // Narrowest sea between the main islands, as a share of the map.
        public float ChannelFraction = 0.08f;
        // Minor islands, about 5-8% of the main island's area, within IsletSpread
        // of a main island's reach.
        public int IsletsMin = 3, IsletsMax = 5;
        public float IsletRadiusMin = 0.05f, IsletRadiusMax = 0.075f, IsletSpread = 0.22f;
        public float IsletRidgeChance = 0.5f;
        // Chance an islet joins the cluster: within IsletClusterGap (of the map) of another islet's coast.
        public float IsletClusterChance = 0.8f, IsletClusterGap = 0.06f;
        // The main island always gets a two-ridge valley; these count the second's ranges.
        public int RangesMin = 1, RangesMax = 1;
        public int LandmarksPerIsland = 2;
        // The main valley's lake radius, in main-island radii.
        public float MainLakeRadius = 0.125f;
        // Small lakes per main or second island, radius in that island's radii.
        public int PondsMin = 1, PondsMax = 4;
        public float PondRadiusMin = 0.035f, PondRadiusMax = 0.07f;

        // The map spans ~23 km, too little for latitude to matter, so climate varies with height,
        // wind and shelter only. The lapse rate is steeper than Earth's 0.0065 so a 1.1 km island
        // still climbs from temperate forest through boreal to tundra on its tableland.
        public float SeaLevelTempC = 9f, LapseRatePerMeter = 0.014f;
        public Wind PrevailingWind = Wind.FromWest;
        // Moisture is 1 off the sea. A full charge rains BaseRainMm a year on flat land plus
        // OrographicRainMm per unit of upwind slope (capped at MaxRainMm), slope measured over UpwindMeters; every metre of rain
        // a cell takes costs RainoutMm-metres of moisture, and each km of sea restores SeaRecharge.
        public float BaseRainMm = 750f, OrographicRainMm = 25000f, MaxRainMm = 4000f;
        public float RainoutMm = 2.4e7f, SeaRecharge = 0.25f, LandRecharge = 0.01f, UpwindMeters = 300f;

        public float ErosionRainMm = 450f;
        public int ErosionSteps = 100;
        public float TimeStepYears = 1e5f;
        public float MaxUplift = 1.9e-3f;
        public float Erodibility = 2.6e-5f;
        public float AreaExponent = 0.45f;
        public float Diffusion = 0.004f;
        public float MaxSlope = 1.2f;
        public float InitialRelief = 700f;
        // Seabed: a shelf ShelfDepth deep and ShelfMeters wide, then a slope down to
        // DeepDepth; beaches double the shelf, cliffs nearly remove it.
        public float ShelfDepth = 40f, ShelfMeters = 900f, SlopeMeters = 2250f, DeepDepth = 300f;
        // A cliff shore is a raised steppe, PlateauHeight at the cliff and CliffRise more
        // CliffTiltMeters inland, in treads CliffStepMeters high; hard caprock
        // (CaprockHardness off erodibility) keeps that shape under erosion.
        public float PlateauHeight = 60f, CaprockHardness = 0.995f;
        public float CliffRise = 90f, CliffTiltMeters = 1800f, CliffStepMeters = 18f;
        public float CliffSlope = 3f, CliffFootDepth = 25f;
        // The main ridges start at a summit cone (radius in island radii) beside a tableland
        // levelled after erosion, and fall to 1 - RangeTaper of their strength and
        // 1 - RangeNarrowing of their width at their far ends; RangeCrestNoise varies both into peaks.
        // SummitHardness takes that share off the cone's erodibility so it outstands the ridges.
        public float PeakStrength = 1.8f, PeakRadius = 0.2f, SummitHardness = 0.75f, ShoulderStrength = 0.8f;
        public float RangeTaper = 0.7f, RangeNarrowing = 0.7f, RangeCrestNoise = 0.35f;
        public float HighlandRadiusMin = 0.16f, HighlandRadiusMax = 0.24f, TablelandFall = 0.06f, TablelandRelief = 12f, TablelandShare = 0.5f;
        public float BeachGrade = 0.015f, BeachMeters = 675f;
        // Ridges run on under the sea for this share of their length, cresting this deep.
        public float RidgeReach = 1f, RidgeCrestDepth = 6f;

        // Area in m2, so a world keeps the same lakes at any resolution; shallower or
        // smaller pits fill in rather than dotting the land with ponds.
        public float LakeMinDepth = 1.5f;
        public float LakeMinAreaM2 = 15000f;
        public float RiverMinDischarge = 0.028f;
        // Rivers drawn far wider than their catchments justify (Zach 11-20):
        // width = RiverWidthAtUnitFlow * Q^RiverWidthExponent, Q in m3/s.
        public float RiverWidthAtUnitFlow = 85f, RiverWidthExponent = 1f;

        // Above 0, erosion and hydrology use this uniform rainfall and climate
        // and biomes are skipped, so terrain can be judged on its own.
        public float UniformRainMm = 0f;

        public WorldConfig Clone() => (WorldConfig)MemberwiseClone();
    }
}
