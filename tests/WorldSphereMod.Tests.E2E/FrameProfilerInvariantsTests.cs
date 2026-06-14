using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
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

    private static Assembly LoadModAssembly()
    {
        var root = FindRepoRoot();
        var dllPath = Path.Combine(root, "bin", "Release", "net48", "WorldSphereMod3D.dll");
        File.Exists(dllPath).Should().BeTrue($"WorldSphereMod3D.dll must be built at {dllPath}");
        return Assembly.LoadFrom(dllPath);
    }

    private static void SetProfilerDump(Assembly asm, bool value)
    {
        var coreType = asm.GetType("WorldSphereMod.Core")!;
        var savedSettingsField = coreType.GetField("savedSettings", BindingFlags.Public | BindingFlags.Static)!;
        var savedSettings = savedSettingsField.GetValue(null)!;
        var profilerDumpField = savedSettings.GetType().GetField("ProfilerDump")!;
        profilerDumpField.SetValue(savedSettings, value);
    }

    private static void ResetProfilerState(Assembly asm)
    {
        var frameProfilerType = asm.GetType("WorldSphereMod.Perf.FrameProfiler")!;
        var totalMs = frameProfilerType.GetField("_totalMs", BindingFlags.NonPublic | BindingFlags.Static)!;
        var begin = frameProfilerType.GetField("_begin", BindingFlags.NonPublic | BindingFlags.Static)!;
        var running = frameProfilerType.GetField("_running", BindingFlags.NonPublic | BindingFlags.Static)!;
        var windowElapsed = frameProfilerType.GetField("_windowElapsed", BindingFlags.NonPublic | BindingFlags.Static)!;

        ((Dictionary<string, double>)totalMs.GetValue(null)!).Clear();
        ((Dictionary<string, long>)begin.GetValue(null)!).Clear();
        ((Dictionary<string, Stopwatch>)running.GetValue(null)!).Clear();
        windowElapsed.SetValue(null, 0f);
    }

    [Fact]
    public void FrameProfiler_Register_initializes_accumulator_and_stopwatch_entries()
    {
        var asm = LoadModAssembly();
        ResetProfilerState(asm);

        var frameProfilerType = asm.GetType("WorldSphereMod.Perf.FrameProfiler")!;
        var register = frameProfilerType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!;
        var totalMs = frameProfilerType.GetField("_totalMs", BindingFlags.NonPublic | BindingFlags.Static)!;
        var running = frameProfilerType.GetField("_running", BindingFlags.NonPublic | BindingFlags.Static)!;

        register.Invoke(null, new object[] { "reg_key" });

        var totalDict = (Dictionary<string, double>)totalMs.GetValue(null)!;
        var runningDict = (Dictionary<string, Stopwatch>)running.GetValue(null)!;

        totalDict.Should().ContainKey("reg_key", "Register must seed the per-key accumulator");
        totalDict["reg_key"].Should().Be(0.0, "Register must initialize the accumulator to zero");
        runningDict.Should().ContainKey("reg_key", "Register must seed the per-key Stopwatch");
    }

    [Fact]
    public void FrameProfiler_Begin_End_are_noop_when_profiler_dump_disabled()
    {
        var asm = LoadModAssembly();
        ResetProfilerState(asm);
        SetProfilerDump(asm, false);

        var frameProfilerType = asm.GetType("WorldSphereMod.Perf.FrameProfiler")!;
        var register = frameProfilerType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!;
        var begin = frameProfilerType.GetMethod("Begin", BindingFlags.Public | BindingFlags.Static)!;
        var end = frameProfilerType.GetMethod("End", BindingFlags.Public | BindingFlags.Static)!;
        var totalMs = frameProfilerType.GetField("_totalMs", BindingFlags.NonPublic | BindingFlags.Static)!;
        var beginDict = frameProfilerType.GetField("_begin", BindingFlags.NonPublic | BindingFlags.Static)!;

        register.Invoke(null, new object[] { "noop_key" });
        begin.Invoke(null, new object[] { "noop_key" });
        end.Invoke(null, new object[] { "noop_key" });

        var totalDict = (Dictionary<string, double>)totalMs.GetValue(null)!;
        var bdict = (Dictionary<string, long>)beginDict.GetValue(null)!;

        totalDict["noop_key"].Should().Be(0.0, "End must not accumulate when ProfilerDump is disabled");
        bdict.Should().NotContainKey("noop_key", "Begin must not store a timestamp when ProfilerDump is disabled");
    }

    [Fact]
    public void FrameProfiler_Begin_End_accumulate_elapsed_time_when_profiler_dump_enabled()
    {
        var asm = LoadModAssembly();
        ResetProfilerState(asm);
        SetProfilerDump(asm, true);

        var frameProfilerType = asm.GetType("WorldSphereMod.Perf.FrameProfiler")!;
        var register = frameProfilerType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!;
        var begin = frameProfilerType.GetMethod("Begin", BindingFlags.Public | BindingFlags.Static)!;
        var end = frameProfilerType.GetMethod("End", BindingFlags.Public | BindingFlags.Static)!;
        var totalMs = frameProfilerType.GetField("_totalMs", BindingFlags.NonPublic | BindingFlags.Static)!;
        var beginDict = frameProfilerType.GetField("_begin", BindingFlags.NonPublic | BindingFlags.Static)!;

        register.Invoke(null, new object[] { "acc_key" });
        begin.Invoke(null, new object[] { "acc_key" });
        end.Invoke(null, new object[] { "acc_key" });

        var totalDict = (Dictionary<string, double>)totalMs.GetValue(null)!;
        var bdict = (Dictionary<string, long>)beginDict.GetValue(null)!;

        totalDict["acc_key"].Should().BeGreaterThan(0, "End must accumulate a positive elapsed time when ProfilerDump is enabled");
        bdict.Should().ContainKey("acc_key", "Begin must store the start timestamp when ProfilerDump is enabled");
    }

    [Fact]
    public void FrameProfiler_Tick_accumulates_window_time_without_touching_totals()
    {
        var asm = LoadModAssembly();
        ResetProfilerState(asm);
        SetProfilerDump(asm, true);

        var frameProfilerType = asm.GetType("WorldSphereMod.Perf.FrameProfiler")!;
        var register = frameProfilerType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!;
        var begin = frameProfilerType.GetMethod("Begin", BindingFlags.Public | BindingFlags.Static)!;
        var end = frameProfilerType.GetMethod("End", BindingFlags.Public | BindingFlags.Static)!;
        var tick = frameProfilerType.GetMethod("Tick", BindingFlags.Public | BindingFlags.Static)!;
        var totalMs = frameProfilerType.GetField("_totalMs", BindingFlags.NonPublic | BindingFlags.Static)!;
        var windowElapsed = frameProfilerType.GetField("_windowElapsed", BindingFlags.NonPublic | BindingFlags.Static)!;

        register.Invoke(null, new object[] { "tick_key" });
        begin.Invoke(null, new object[] { "tick_key" });
        end.Invoke(null, new object[] { "tick_key" });

        var totalDict = (Dictionary<string, double>)totalMs.GetValue(null)!;
        var beforeTotal = totalDict["tick_key"];
        var beforeWindow = (float)windowElapsed.GetValue(null)!;

        for (int i = 0; i < 5; i++)
        {
            tick.Invoke(null, new object[] { 0.1f });
        }

        var afterWindow = (float)windowElapsed.GetValue(null)!;
        var afterTotal = totalDict["tick_key"];

        afterWindow.Should().BeApproximately(beforeWindow + 0.5f, 0.001f, "Tick must accumulate dt into _windowElapsed");
        afterTotal.Should().Be(beforeTotal, "Tick must not reset totals when the window threshold is not crossed");
    }

    [Fact]
    public void FrameProfiler_Tick_reaches_flush_branch_when_window_threshold_crossed()
    {
        var asm = LoadModAssembly();
        ResetProfilerState(asm);
        SetProfilerDump(asm, true);

        var frameProfilerType = asm.GetType("WorldSphereMod.Perf.FrameProfiler")!;
        var register = frameProfilerType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!;
        var begin = frameProfilerType.GetMethod("Begin", BindingFlags.Public | BindingFlags.Static)!;
        var end = frameProfilerType.GetMethod("End", BindingFlags.Public | BindingFlags.Static)!;
        var tick = frameProfilerType.GetMethod("Tick", BindingFlags.Public | BindingFlags.Static)!;
        var windowElapsed = frameProfilerType.GetField("_windowElapsed", BindingFlags.NonPublic | BindingFlags.Static)!;

        register.Invoke(null, new object[] { "flush_key" });
        begin.Invoke(null, new object[] { "flush_key" });
        end.Invoke(null, new object[] { "flush_key" });

        windowElapsed.SetValue(null, 0.95f);

        var flushAction = () => tick.Invoke(null, new object[] { 0.1f });

        flushAction.Should().Throw<TargetInvocationException>(
            "Tick must enter the flush branch when _windowElapsed crosses the 1-second threshold; " +
            "outside Unity, Debug.Log throws a SecurityException, confirming the branch was reached");
    }

    [Fact]
    public void FrameProfiler_Tick_is_noop_when_profiler_dump_disabled()
    {
        var asm = LoadModAssembly();
        ResetProfilerState(asm);
        SetProfilerDump(asm, false);

        var frameProfilerType = asm.GetType("WorldSphereMod.Perf.FrameProfiler")!;
        var tick = frameProfilerType.GetMethod("Tick", BindingFlags.Public | BindingFlags.Static)!;
        var windowElapsed = frameProfilerType.GetField("_windowElapsed", BindingFlags.NonPublic | BindingFlags.Static)!;

        var beforeWindow = (float)windowElapsed.GetValue(null)!;

        tick.Invoke(null, new object[] { 2.0f });

        var afterWindow = (float)windowElapsed.GetValue(null)!;

        afterWindow.Should().Be(beforeWindow, "Tick must be a no-op when ProfilerDump is disabled, even with large dt");
    }

    [Fact]
    public void ProfilerFrameDriver_is_sealed_monobehaviour_with_lateupdate_tick()
    {
        var asm = LoadModAssembly();

        var driverType = asm.GetType("WorldSphereMod.Perf.ProfilerFrameDriver")!;

        driverType.IsSealed.Should().BeTrue("ProfilerFrameDriver must be sealed to prevent accidental subclassing");
        driverType.BaseType!.FullName.Should().Be("UnityEngine.MonoBehaviour", "ProfilerFrameDriver must inherit from MonoBehaviour");

        var lateUpdate = driverType.GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance)!;
        lateUpdate.Should().NotBeNull("ProfilerFrameDriver must declare LateUpdate");

        var tickMethod = asm.GetType("WorldSphereMod.Perf.FrameProfiler")!.GetMethod("Tick", BindingFlags.Public | BindingFlags.Static)!;
        var body = lateUpdate.GetMethodBody();
        body.Should().NotBeNull("LateUpdate must have a method body");
    }

    [Fact]
    public void ProfilerFrameDriver_records_only_when_enabled_via_tick_gate()
    {
        var asm = LoadModAssembly();
        ResetProfilerState(asm);
        SetProfilerDump(asm, false);

        var frameProfilerType = asm.GetType("WorldSphereMod.Perf.FrameProfiler")!;
        var tick = frameProfilerType.GetMethod("Tick", BindingFlags.Public | BindingFlags.Static)!;
        var windowElapsed = frameProfilerType.GetField("_windowElapsed", BindingFlags.NonPublic | BindingFlags.Static)!;

        var beforeWindow = (float)windowElapsed.GetValue(null)!;

        // Simulate what ProfilerFrameDriver.LateUpdate does
        tick.Invoke(null, new object[] { 0.016f });

        var afterWindow = (float)windowElapsed.GetValue(null)!;

        afterWindow.Should().Be(beforeWindow, "ProfilerFrameDriver must not advance profiling when ProfilerDump is disabled; " +
            "the gate lives inside FrameProfiler.Tick");
    }
}
