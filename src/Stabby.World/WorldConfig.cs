namespace Stabby.World
{
    public enum Wind { FromWest, FromEast, FromSouth, FromNorth }

    // Grid y grows northward. Heights are metres, time is years, precipitation is mm/yr.
    public sealed class WorldConfig
    {
        public const int GeneratorVersion = 1;

        public ulong Seed = 1;
        public int Size = 256;
        public float CellMeters = 60f;

        public float LandFractionMin = 0.35f, LandFractionMax = 0.55f;
        public int RangesMin = 2, RangesMax = 4;

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

        public WorldConfig Clone() => (WorldConfig)MemberwiseClone();
    }
}
