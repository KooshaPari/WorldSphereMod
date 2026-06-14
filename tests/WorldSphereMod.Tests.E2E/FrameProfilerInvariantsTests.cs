using System.IO;
using FluentAssertions;
using Xunit;

[Trait("Category", "E2E")]
public class FrameProfilerInvariantsTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    private static string ReadSourceFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void FrameProfiler_is_public_static_with_register_begin_end_tick()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Perf/FrameProfiler.cs");

        source.Should().Contain("public static class FrameProfiler",
            "FrameProfiler must be a public static class so it can be called from hot paths without instance overhead");
        source.Should().Contain("public static void Register(string key)",
            "Register must be a public static entry point for systems that declare profiling keys upfront");
        source.Should().Contain("public static void Begin(string key)",
            "Begin must be a public static entry point to bracket timed work");
        source.Should().Contain("public static void End(string key)",
            "End must be a public static entry point to close a timed bracket and accumulate the sample");
        source.Should().Contain("public static void Tick(float dt)",
            "Tick must be a public static entry point to flush the rolling window");
    }

    [Fact]
    public void FrameProfiler_begin_end_are_gated_by_profiler_dump()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Perf/FrameProfiler.cs");

        source.Should().Contain("if (!Core.savedSettings.ProfilerDump) return;",
            "Begin and End must be gated by ProfilerDump so they are zero-cost when the profiler is disabled");
        source.Should().Contain("_begin[key] = Stopwatch.GetTimestamp();",
            "Begin must store the start timestamp in a static dictionary keyed by the profiling key");
        source.Should().Contain("if (!_begin.TryGetValue(key, out long start)) return;",
            "End must guard against missing start timestamps to avoid double-counting or unbalanced brackets");
        source.Should().Contain("double elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;",
            "End must convert Stopwatch ticks to milliseconds using the platform frequency");
        source.Should().Contain("_totalMs[key] += elapsedMs",
            "End must accumulate the elapsed time into the rolling total for the current window");
    }

    [Fact]
    public void FrameProfiler_tick_flushes_rolling_window_and_resets_totals()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Perf/FrameProfiler.cs");

        source.Should().Contain("const float kWindowSize = 1.0f;",
            "Tick must use a fixed 1-second rolling window so log output is readable and predictable");
        source.Should().Contain("_windowElapsed += dt;",
            "Tick must accumulate delta time into the window counter each frame");
        source.Should().Contain("if (_windowElapsed < kWindowSize) return;",
            "Tick must defer the flush until the window threshold is crossed");
        source.Should().Contain("[WSM-PROF]",
            "Tick must emit a recognizable log prefix so operators can grep for profiler output");
        source.Should().Contain("var keys = _totalMs.Keys.ToList();",
            "Tick must snapshot the keys before resetting totals to avoid mutating the dictionary during iteration");
        source.Should().Contain("_totalMs[keys[i]] = 0.0;",
            "Tick must zero every accumulated total after flushing so the next window starts clean");
        source.Should().Contain("_windowElapsed = 0f;",
            "Tick must reset the window accumulator after flushing so the next window begins at zero");
    }

    [Fact]
    public void FrameProfiler_register_initializes_accumulator_and_stopwatch_entries()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Perf/FrameProfiler.cs");

        source.Should().Contain("if (!_totalMs.ContainsKey(key)) _totalMs[key] = 0.0;",
            "Register must seed the per-key accumulator so End can safely add to it without a key-missing guard");
        source.Should().Contain("if (!_running.ContainsKey(key)) _running[key] = new Stopwatch();",
            "Register must seed the per-key Stopwatch so the profiler can reuse instances across frames");
    }

    [Fact]
    public void ProfilerFrameDriver_is_sealed_monobehaviour_that_drives_tick_in_lateupdate()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Perf/ProfilerFrameDriver.cs");

        source.Should().Contain("public sealed class ProfilerFrameDriver : MonoBehaviour",
            "ProfilerFrameDriver must be a sealed MonoBehaviour to prevent accidental subclassing");
        source.Should().Contain("void LateUpdate() => FrameProfiler.Tick(Time.deltaTime);",
            "ProfilerFrameDriver must call FrameProfiler.Tick in LateUpdate so profiling happens after all frame work is done");
    }

    [Fact]
    public void ProfilerFrameDriver_is_mounted_in_mod_init_sequence()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Mod.cs");

        source.Should().Contain("Object.AddComponent<WorldSphereMod.Perf.ProfilerFrameDriver>();",
            "ProfilerFrameDriver must be added to the mod GameObject during deferred init so it runs for every frame");
    }
}
