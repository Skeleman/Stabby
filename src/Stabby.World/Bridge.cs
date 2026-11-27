namespace Stabby.World
{
    public static partial class Structures
    {
        public const float BridgeModuleM = 4f, BridgeU = 1 / BridgeModuleM, BridgeHalfM = 2.2f, BridgePileM = 12f, BridgeBeamZ = 1.6f, BridgeRailM = 1.0f;

        static void Bridge(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "road-bridge");
            float half = 0.5f * BridgeModuleM + 0.06f;
            for (float x = -half + 0.17f; x < half; x += rng.Range(0.3f, 0.36f))
                Box(l, key, f, x + rng.Range(-0.02f, 0.02f), -0.09f + rng.Range(-0.015f, 0.01f), rng.Range(-0.06f, 0.06f), rng.Range(0.26f, 0.31f), 0.08f, 2f * BridgeHalfM * rng.Range(0.97f, 1.02f), Plank, GZ, 0, 1, 1, rng.Range(-0.01f, 0.01f));
            Box(l, key, f, 0, -0.1f, 0, 2f * half, 0.08f, 2f * BridgeHalfM - 0.1f, Plank, GZ, 2, 2);
            for (int side = -1; side <= 1; side += 2)
            {
                float bz = side * BridgeBeamZ;
                Box(l, key, f, 0, -0.38f, bz, 2f * half + 0.1f, 0.28f, 0.26f, Timber, GX);
                Box(l, key, f, rng.Range(-0.1f, 0.1f), -0.68f, bz, rng.Range(0.5f, 0.7f), 0.3f, 0.3f, Timber, GZ, 0, 1);
                float pz = side * (BridgeHalfM - 0.08f);
                for (int p = -1; p <= 1; p += 2)
                {
                    float px = p * (half - 0.12f);
                    Box(l, key, f, px, -0.1f, pz, 0.14f, BridgeRailM + 0.1f, 0.14f, Timber, GV, 0, 1, 1, 0, rng.Range(-0.02f, 0.02f));
                    Col(l, f, px, pz, 0.12f, -0.1f, BridgeRailM);
                }
                Box(l, key, f, 0, BridgeRailM - 0.1f, pz, 2f * half + 0.04f, 0.11f, 0.12f, Timber, GX, 0, 1);
                Box(l, key, f, 0, 0.45f, pz, 2f * half, 0.08f, 0.08f, Timber, GX, 0, 0);
                for (float x = -1.5f; x <= 1.5f; x += 1f) Col(l, f, x, pz, 0.12f, -0.1f, BridgeRailM);
                float lean = side * rng.Range(0f, 0.025f), w = rng.Range(0.24f, 0.3f);
                Box(l, key, f, rng.Range(-0.05f, 0.05f), -BridgePileM, bz, w, BridgePileM - 0.5f, w, Timber, GV, 0, Tiers - 1, 1, 0, lean);
                Col(l, f, 0, bz, 0.5f * w, -BridgePileM, -0.5f);
                Touch(l, f, 0, -BridgePileM, bz, 0.6f * w);
            }
            Box(l, key, f, 0, -1.5f, 0, 0.16f, 0.2f, 2f * BridgeBeamZ + 0.3f, Timber, GZ, 0, 1, 1, 0, rng.Range(-0.03f, 0.03f));
            for (int a = -1; a <= 1; a += 2)
                for (int b = -1; b <= 1; b += 2) Col(l, f, a * 1.0f, b * 1.1f, 1.15f, -0.4f, -0.01f);
        }
    }
}
