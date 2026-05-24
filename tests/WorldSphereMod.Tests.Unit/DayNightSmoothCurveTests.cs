using System;
using FluentAssertions;
using Xunit;

/// <summary>
/// Continuity invariants for the Phase 8 four-segment day/night color curve
/// (mirrors <c>SunRig.SampleSkyCurve</c> / <c>SunColor</c> keyframes).
/// </summary>
public class DayNightSmoothCurveTests
{
    readonly (float r, float g, float b) _night = (0.17f, 0.23f, 0.40f);
    readonly (float r, float g, float b) _dawn = (1.0f, 0.61f, 0.31f);
    readonly (float r, float g, float b) _noon = (1.0f, 0.96f, 0.88f);
    readonly (float r, float g, float b) _dusk = (1.0f, 0.42f, 0.21f);

    static (float r, float g, float b) Lerp((float r, float g, float b) a, (float r, float g, float b) b, float u) =>
        (a.r + (b.r - a.r) * u, a.g + (b.g - a.g) * u, a.b + (b.b - a.b) * u);

    (float r, float g, float b) Sample(float t)
    {
        if (t < 0.25f) return Lerp(_night, _dawn, t / 0.25f);
        if (t < 0.5f) return Lerp(_dawn, _noon, (t - 0.25f) / 0.25f);
        if (t < 0.75f) return Lerp(_noon, _dusk, (t - 0.5f) / 0.25f);
        return Lerp(_dusk, _night, (t - 0.75f) / 0.25f);
    }

    static float MaxChannelDelta((float r, float g, float b) a, (float r, float g, float b) b) =>
        Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));

    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    [InlineData(1f)]
    public void Segment_boundaries_are_continuous(float t)
    {
        const float eps = 1e-4f;
        var at = Sample(t);
        var before = Sample(Math.Max(0f, t - eps));
        var after = Sample(Math.Min(1f, t + eps));
        MaxChannelDelta(at, before).Should().BeLessThan(0.02f, $"color should not jump approaching t={t}");
        MaxChannelDelta(at, after).Should().BeLessThan(0.02f, $"color should not jump leaving t={t}");
    }

    [Fact]
    public void Dense_samples_never_step_more_than_small_delta()
    {
        const int steps = 240;
        const float maxStep = 0.05f;
        var prev = Sample(0f);
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            var cur = Sample(t);
            MaxChannelDelta(prev, cur).Should().BeLessThan(maxStep, $"t={t} should advance smoothly along the curve");
            prev = cur;
        }
    }

    [Fact]
    public void Wrap_midnight_matches_night_keyframe()
    {
        var atMidnight = Sample(0f);
        var nearWrap = Sample(1f - 1e-5f);
        MaxChannelDelta(atMidnight, nearWrap).Should().BeLessThan(0.02f, "cycle should close without a hard cut at midnight");
    }
}
