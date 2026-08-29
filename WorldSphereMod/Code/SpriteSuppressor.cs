namespace WorldSphereMod.Code
{
    /// <summary>
    /// Placeholder — the Camera.OnPreCull Harmony patch was causing
    /// "Undefined target method" crashes because OnPreCull is a Unity
    /// message, not a public method. Harmony cannot patch Unity messages.
    ///
    /// The SpriteSuppressor is disabled until a valid approach is found
    /// (e.g., ScriptableRenderFeature for URP, or Camera.onPreCull callback
    /// registration via C# delegates instead of Harmony patching).
    /// </summary>
    public static class SpriteSuppressor
    {
        public static void InvalidateCache() { }
    }
}
