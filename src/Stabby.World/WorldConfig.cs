namespace Stabby.World
{
    public enum Wind { FromWest, FromEast, FromSouth, FromNorth }

    // Grid y grows northward. Heights are metres, time is years, precipitation is mm/yr.
    public sealed class WorldConfig
    {
        public const int GeneratorVersion = 3;

        public ulong Seed = 1;
        public int Size = 512;
        public float CellMeters = 60f;

        // Islands: the second has SecondIslandScale times the first's radius,
        // so about half its area at 0.71.
        public int Landmasses = 2;
        public float MainIslandRadius = 0.24f, SecondIslandScale = 0.71f;
        public int RangesMin = 2, RangesMax = 3;
        public int LandmarksPerIsland = 2;

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
        public float SeaFloor = -30f;

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
