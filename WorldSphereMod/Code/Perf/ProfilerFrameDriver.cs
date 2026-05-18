using UnityEngine;

namespace WorldSphereMod.Perf
{
    /// <summary>
    /// MonoBehaviour mounted on <see cref="Mod.Object"/> in <c>Mod.Init</c>. Drives
    /// <see cref="FrameProfiler.Tick"/> once per frame in <c>LateUpdate</c>, after
    /// every other per-frame driver has run. No-op when
    /// <c>Core.savedSettings.ProfilerDump</c> is false — the guard lives inside
    /// <c>FrameProfiler.Tick</c>.
    /// </summary>
    public sealed class ProfilerFrameDriver : MonoBehaviour
    {
        void LateUpdate() => FrameProfiler.Tick(Time.deltaTime);
    }
}
