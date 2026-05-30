namespace WorldSphereMod.Water
{
    // RETIRED (ADR-fork-terrain-water-slope P2): the WaterSurface billboard lifecycle
    // + its Harmony patches (Sphere.Begin/Finish/UpdateBaseLayer/UpdateScale postfixes
    // and the SphereTileColor alpha-suppression) are removed. Water is built in the fork
    // mesh now. UpdateLifecycle is kept as a no-op so EffectPatches9 still compiles.
    public static class WaterRender
    {
        public static void UpdateLifecycle() { }
    }
}
