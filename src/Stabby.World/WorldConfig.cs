namespace Stabby.World
{
    public enum Wind { FromWest, FromEast, FromSouth, FromNorth }

    public sealed class WorldConfig
    {
        public const int GeneratorVersion = 203;

        public ulong Seed = 1;
        public const float MapMeters = 23040f;
        public int Size = 512;
        public float CellMeters = MapMeters / 512;
        public static float CellMetersFor(int size) => MapMeters / size;

        public int Landmasses = 2;
        public float MainIslandRadius = 0.26f, SecondIslandScale = 0.71f;
        public int MainLobes = 6, SecondLobes = 4;
        public float ChannelFraction = 0.08f;
        public int IsletsMin = 3, IsletsMax = 5;
        public float IsletRadiusMin = 0.05f, IsletRadiusMax = 0.075f, IsletSpread = 0.22f;
        public float IsletRidgeChance = 0.5f;
        public float IsletClusterChance = 0.8f, IsletClusterGap = 0.06f;
        public int RangesMin = 1, RangesMax = 1;
        public int LandmarksPerIsland = 2;
        public float MainLakeRadius = 0.125f;
        public int PondsMin = 1, PondsMax = 4;
        public float PondRadiusMin = 0.035f, PondRadiusMax = 0.07f;

        public float SeaLevelTempC = 9f, LapseRatePerMeter = 0.014f;
        public Wind PrevailingWind = Wind.FromWest;
        public float BaseRainMm = 750f, OrographicRainMm = 25000f, MaxRainMm = 4000f;
        public float RainoutMm = 2.4e7f, SeaRecharge = 0.25f, LandRecharge = 0.01f, UpwindMeters = 300f;

        public float ErosionRainMm = 450f;
        public int ErosionSteps = 100;
        public int ErosionSize = 512, FineSteps = 8;
        public float TimeStepYears = 1e5f;
        public float MaxUplift = 1.9e-3f;
        public float Erodibility = 2.6e-5f;
        public float AreaExponent = 0.45f;
        public float Diffusion = 0.004f;
        public float MaxSlope = 1.2f;
        public float InitialRelief = 700f;
        public float ShelfDepth = 40f, ShelfMeters = 900f, SlopeMeters = 2250f, DeepDepth = 300f;
        public float PlateauHeight = 60f, CaprockHardness = 0.995f;
        public float CliffRise = 90f, CliffTiltMeters = 1800f, CliffStepMeters = 18f;
        public float CliffSlope = 3f, CliffFootDepth = 25f, StrataSlope = 2f, StrataContrast = 0.5f;
        public float PeakStrength = 1.8f, PeakRadius = 0.2f, SummitHardness = 0.75f, ShoulderStrength = 0.8f;
        public float RangeTaper = 0.7f, RangeNarrowing = 0.7f, RangeCrestNoise = 0.35f;
        public float HighlandRadiusMin = 0.16f, HighlandRadiusMax = 0.24f, TablelandFall = 0.06f, TablelandRelief = 12f, TablelandShare = 0.5f;
        public float BeachGrade = 0.015f, BeachMeters = 675f, NearShoreDepth = 3f, NearShoreMeters = 60f;
        public float RidgeReach = 1f, RidgeCrestDepth = 6f;

        public float LakeMinDepth = 1.5f;
        public float LakeMinAreaM2 = 15000f;
        public float RiverMinDischarge = 0.028f;
        public float RiverWidthAtUnitFlow = 85f, RiverWidthExponent = 1f;
        public float RiverFlatSlope = 0.02f, RiverLowlandWiden = 1f, RiverBankRise = 3f, RiverBankRisePerWidth = 0.05f, RiverBankWidths = 1.5f, RiverLeveePeak = 0.15f;
        public float MeanderMaxCells = 0.9f, MeanderWidths = 2.5f, MeanderWavelengthWidths = 12f, MeanderMinWavelengthCells = 4f;
        public float MeanderReachHalfWidths = 1.6f, RiverSmoothWidths = 3f;
        public float RiverWidthWobble = 0.15f, RiverWobbleWavelengthWidths = 3f, RiverJitterWidths = 0.05f, RiverJitterWavelengthWidths = 1.5f;
        public float RiverStageFraction = 0.35f;
        public float RiverDepthMin = 0.3f, RiverDepthAtUnitFlow = 3.2f, RiverDepthExponent = 0.6f, RiverDepthPerWidth = 0.2f;

        public float UniformRainMm;

        public WorldConfig Clone() => (WorldConfig)MemberwiseClone();
    }
}
