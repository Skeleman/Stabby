namespace Stabby.World
{
    public enum Wind { FromWest, FromEast, FromSouth, FromNorth }

    // Grid y grows northward. Heights are metres, time is years, precipitation is mm/yr.
    public sealed class WorldConfig
    {
        public const int GeneratorVersion = 7;

        public ulong Seed = 1;
        public int Size = 512;
        public float CellMeters = 60f;

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
        public int RangesMin = 1, RangesMax = 2;
        public int LandmarksPerIsland = 2;
        // The main valley's lake radius, in main-island radii.
        public float MainLakeRadius = 0.15f;

        public float LatitudeSouth = 25f, LatitudeNorth = 55f;
        public Wind PrevailingWind = Wind.FromWest;

        public int ErosionSteps = 100;
        public float TimeStepYears = 1e5f;
        public float MaxUplift = 2e-3f;
        public float Erodibility = 2e-5f;
        public float AreaExponent = 0.45f;
        public float Diffusion = 0.004f;
        public float MaxSlope = 1.2f;
        public float InitialRelief = 900f;
        // Seabed: a shelf ShelfDepth deep and ShelfMeters wide, then a slope down to
        // DeepDepth; beaches double the shelf, cliffs nearly remove it.
        public float ShelfDepth = 40f, ShelfMeters = 1200f, SlopeMeters = 3000f, DeepDepth = 300f;
        public float CliffHeight = 90f, CliffSlope = 3f, CliffFootDepth = 25f, CliffMeters = 600f;
        // Uplift (share of MaxUplift) that holds cliffs up against erosion.
        public float CliffUplift = 0.35f;
        public float BeachGrade = 0.015f, BeachMeters = 900f;
        // Ridges run on under the sea for this share of their length, cresting this deep.
        public float RidgeReach = 1f, RidgeCrestDepth = 6f;

        public float LakeMinDepth = 1f;
        public int LakeMinCells = 3;
        public float RiverMinDischarge = 0.05f;
        // Rivers drawn far wider than their catchments justify (Zach 11-20):
        // width = RiverWidthAtUnitFlow * Q^RiverWidthExponent, Q in m3/s.
        public float RiverWidthAtUnitFlow = 60f, RiverWidthExponent = 1f;

        // Above 0, erosion and hydrology use this uniform rainfall and climate
        // and biomes are skipped, so terrain can be judged on its own.
        public float UniformRainMm = 0f;

        public WorldConfig Clone() => (WorldConfig)MemberwiseClone();
    }
}
