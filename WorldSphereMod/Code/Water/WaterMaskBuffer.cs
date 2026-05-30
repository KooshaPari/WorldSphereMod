namespace WorldSphereMod.Water
{
    // RETIRED (ADR-fork-terrain-water-slope P2): the per-tile water mask/depth/sea-level
    // computation moved into the fork's HeightFieldRenderer water sub-mesh, fed by
    // Core.ConfigureHeightField's ConfigureWater callbacks. No longer referenced.
    // Left as an empty type to avoid churn; will be deleted in a later cleanup pass.
    internal static class WaterMaskBuffer
    {
    }
}
