using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using WorldSphereMod;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class ScreenshotSafetyTests : IDisposable
{
    private readonly List<AutoScreenshotDriver> _drivers = new List<AutoScreenshotDriver>();
    private readonly string _directory;

    public ScreenshotSafetyTests()
    {
        var root = Environment.GetEnvironmentVariable("WSM_SCREENSHOT_TEST_ROOT");
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("Set WSM_SCREENSHOT_TEST_ROOT to an explicit approved scratch directory.");
        _directory = Path.GetFullPath(Path.Combine(root, Guid.NewGuid().ToString("N")));
        Core.savedSettings = new SavedSettings { AutoScreenshotEnabled = true, AutoScreenshotPath = _directory };
        ScreenCapture.Captured.Clear();
    }

    public void Dispose()
    {
        foreach (var driver in _drivers) Invoke(driver, "OnDisable");
        Core.savedSettings = null;
        // Preserve test artifacts: no cleanup deletes.
    }

    private static void Invoke(AutoScreenshotDriver driver, string method) =>
        typeof(AutoScreenshotDriver).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(driver, null);

    private AutoScreenshotDriver CreateDriver()
    {
        var driver = new AutoScreenshotDriver();
        _drivers.Add(driver);
        Invoke(driver, "OnEnable");
        Invoke(driver, "Start");
        return driver;
    }

    private IEnumerator PendingCapture()
    {
        var driver = CreateDriver();
        var loop = driver.Started[0].Routine;
        Assert.True(loop.MoveNext());
        Assert.IsType<WaitForSeconds>(loop.Current);
        Assert.True(loop.MoveNext());
        return Assert.IsType<Coroutine>(loop.Current).Routine;
    }

    private void AssertNoWrites()
    {
        Assert.False(Directory.Exists(_directory));
        Assert.Empty(ScreenCapture.Captured);
    }

    [Fact]
    public void DefaultsAreOffAndHaveNoImplicitPath()
    {
        var settings = new SavedSettings();
        Assert.False(settings.AutoScreenshotEnabled);
        Assert.Equal(string.Empty, settings.AutoScreenshotPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledOrMissingSettingsNeverScheduleCapture(bool missing)
    {
        if (missing) Core.savedSettings = null;
        else Core.savedSettings.AutoScreenshotEnabled = false;
        var driver = CreateDriver();
        var loop = driver.Started[0].Routine;
        for (int i = 0; i < 4; i++)
        {
            Assert.True(loop.MoveNext());
            Assert.IsType<WaitForSeconds>(loop.Current);
        }
        Assert.Single(driver.Started);
        AssertNoWrites();
    }

    [Fact]
    public void DisableDuringIntervalPreventsCapture()
    {
        var driver = CreateDriver();
        var loop = driver.Started[0].Routine;
        Assert.True(loop.MoveNext());
        Core.savedSettings.AutoScreenshotEnabled = false;
        Assert.True(loop.MoveNext());
        Assert.IsType<WaitForSeconds>(loop.Current);
        Assert.Single(driver.Started);
        AssertNoWrites();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DisableBeforeOrDuringEitherFrameWaitCancels(int waits)
    {
        var capture = PendingCapture();
        for (int i = 0; i < waits; i++) Assert.True(capture.MoveNext());
        Core.savedSettings.AutoScreenshotEnabled = false;
        Assert.False(capture.MoveNext());
        AssertNoWrites();
    }

    [Fact]
    public void MissingSettingsDuringPendingCaptureCancels()
    {
        var capture = PendingCapture();
        Assert.True(capture.MoveNext());
        Core.savedSettings = null;
        Assert.False(capture.MoveNext());
        AssertNoWrites();
    }

    [Fact]
    public void ChangedPathDuringPendingCaptureCancels()
    {
        var capture = PendingCapture();
        Assert.True(capture.MoveNext());
        Core.savedSettings.AutoScreenshotPath = _directory + "-changed";
        Assert.False(capture.MoveNext());
        AssertNoWrites();
        Assert.False(Directory.Exists(_directory + "-changed"));
    }

    [Fact]
    public void DisableAndReenableComponentInvalidatesPendingCapture()
    {
        var capture = PendingCapture();
        Assert.True(capture.MoveNext());
        Invoke(_drivers[0], "OnDisable");
        Invoke(_drivers[0], "OnEnable");
        Assert.False(capture.MoveNext());
        AssertNoWrites();
    }

    [Fact]
    public void RepeatedInitializationAndDuplicateComponentsHaveOneLoop()
    {
        var first = CreateDriver();
        Invoke(first, "Start");
        Invoke(first, "OnEnable");
        var second = CreateDriver();
        Assert.Single(first.Started);
        Assert.Empty(second.Started);
        AssertNoWrites();
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/path")]
    [InlineData("C:relative")]
    [InlineData("C:\\")]
    [InlineData("C:\\invalid*path")]
    [InlineData("C:\\invalid?path")]
    [InlineData("C:\\invalid\0path")]
    [InlineData("C:\\bad:path")]
    [InlineData("C:\\NUL")]
    [InlineData("C:\\con.txt")]
    [InlineData("C:\\COM1")]
    [InlineData("C:\\trailing.")]
    [InlineData("C:\\trailing ")]
    [InlineData("\\\\server\\share\\capture")]
    [InlineData("\\\\?\\C:\\capture")]
    public void InvalidPathsFailWithoutFallback(string path)
    {
        Core.savedSettings.AutoScreenshotPath = path;
        var driver = CreateDriver();
        var loop = driver.Started[0].Routine;
        Assert.True(loop.MoveNext());
        Assert.False(loop.MoveNext());
        Assert.Single(driver.Started);
        AssertNoWrites();
    }

    [Fact]
    public void ExplicitPathCapturesOnlyAfterBothFrameWaits()
    {
        var capture = PendingCapture();
        Assert.True(capture.MoveNext());
        AssertNoWrites();
        Assert.True(capture.MoveNext());
        AssertNoWrites();
        Assert.False(capture.MoveNext());
        Assert.True(Directory.Exists(_directory));
        var path = Assert.Single(ScreenCapture.Captured);
        Assert.Equal(_directory, Path.GetDirectoryName(path));
    }

    [Fact]
    public void OverlongDirectoryFailsWithoutFallback()
    {
        Core.savedSettings.AutoScreenshotPath = Path.Combine(_directory, new string('x', 181));
        var driver = CreateDriver();
        var loop = driver.Started[0].Routine;
        Assert.True(loop.MoveNext());
        Assert.False(loop.MoveNext());
        AssertNoWrites();
    }

    [Fact]
    public void CorrectedPathAfterTerminalFailureRequiresComponentRestart()
    {
        Core.savedSettings.AutoScreenshotPath = string.Empty;
        var driver = CreateDriver();
        var loop = driver.Started[0].Routine;
        Assert.True(loop.MoveNext());
        Assert.False(loop.MoveNext());
        Core.savedSettings.AutoScreenshotPath = _directory;
        Invoke(driver, "Start");
        Assert.Single(driver.Started);
        Invoke(driver, "OnDisable");
        Invoke(driver, "OnEnable");
        loop = Assert.Single(driver.Started).Routine;
        Assert.True(loop.MoveNext());
        Assert.True(loop.MoveNext());
        var capture = Assert.IsType<Coroutine>(loop.Current).Routine;
        while (capture.MoveNext()) { }
        Assert.Single(ScreenCapture.Captured);
    }

    [Fact]
    public void TwoArgumentManualApiRemainsAvailable()
    {
        Assert.NotNull(typeof(ScreenshotCapture).GetMethod("CaptureCoroutine", new[]
        {
            typeof(string), typeof(Action<string, bool, string>)
        }));
    }

    [Fact]
    public void CancellationHasDistinctCompletionResult()
    {
        string error = null;
        var capture = ScreenshotCapture.CaptureCoroutine(Path.Combine(_directory, "capture.png"),
            (path, success, message) => { Assert.False(success); error = message; }, () => false);
        Assert.False(capture.MoveNext());
        Assert.Equal("capture_cancelled", error);
        AssertNoWrites();
    }

    [Fact]
    public void ManualExplicitCaptureDoesNotRequireAutomaticSettings()
    {
        Core.savedSettings = null;
        var capture = ScreenshotCapture.CaptureCoroutine(Path.Combine(_directory, "manual.png"), null);
        while (capture.MoveNext()) { }
        Assert.Single(ScreenCapture.Captured);
    }
}
