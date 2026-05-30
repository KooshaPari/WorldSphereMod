namespace WorldSphereMod.Terrain
{
    // RETIRED (ADR-fork-terrain-water-slope P3): slope smoothing is now intrinsic to the
    // fork's height-field mesh (continuous corner-averaged heights + analytic gradient
    // normals + Perlin micro-displacement in HeightFieldRenderer). The main-mod slope-quad
    // overlay (and its WorldTilemap.redrawTiles Harmony patch) is removed. EnsureActive/
    // RequestRebuild are kept as no-ops so EffectPatches9 still compiles.
    public sealed class MountainSlopeSurface
    {
        public static MountainSlopeSurface? Instance => null;

        public static void EnsureActive() { }
        public static void RequestRebuild() { }
        public static void Destroy() { }
    }
}
