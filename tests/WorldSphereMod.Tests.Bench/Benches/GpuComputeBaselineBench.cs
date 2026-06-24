using System;
using BenchmarkDotNet.Attributes;

namespace WorldSphereMod.Tests.Bench;

// Baseline benchmark for #199 GPU-compute go-live.
//
// Measures the per-tile CPU transform work that the CSMatrices compute kernel
// replaces. Two shapes (cylindrical + flat) at N=1024 tiles. Results serve as
// the CPU-reference ceiling against which a future GPU dispatch benchmark can
// be compared once Unity runtime is available.
//
// Run: dotnet run -c Release --project tests/WorldSphereMod.Tests.Bench -- --filter *GpuCompute*
[MemoryDiagnoser]
[SimpleJob]
public class GpuComputeBaselineBench
{
    private const int TileCount = 1024;
    private const float Radius = 163.0f;   // typical cylindrical world radius (1024/(2*PI))
    private const float ZDisp = 100f;

    private (float X, float Y, float Height)[] _tiles = Array.Empty<(float, float, float)>();

    // Output arrays — pre-allocated to avoid allocation noise in benchmark.
    private (float x, float y, float z)[] _posOut = Array.Empty<(float, float, float)>();
    private (float x, float y, float z, float w)[] _rotOut = Array.Empty<(float, float, float, float)>();

    [GlobalSetup]
    public void Setup()
    {
        _tiles = new (float, float, float)[TileCount];
        _posOut = new (float, float, float)[TileCount];
        _rotOut = new (float, float, float, float)[TileCount];
        var rng = new Random(42);
        for (int i = 0; i < TileCount; i++)
        {
            _tiles[i] = ((float)(rng.NextDouble() * 1024),
                         (float)(rng.NextDouble() * 1024),
                         (float)(rng.NextDouble() * 2));
        }
    }

    // --- Cylindrical shape (CurrentShape == 0, default) ---
    // Mirrors the CPU path in CompoundSphereScripts + the CSMatrices kernel:
    //   phi = -X / radius
    //   pos = (r*cos(phi), r*sin(phi), Y + ZDisp)   where r = radius + h
    //   rot = AngleAxis(atan2(pos.y, pos.x) * RAD2DEG, +Z) * ConstRot
    [Benchmark(Description = "CPU cylindrical transform — 1024 tiles")]
    public void CpuCylindrical_1024()
    {
        for (int i = 0; i < TileCount; i++)
        {
            var (X, Y, h) = _tiles[i];
            float phi = -X / Radius;
            float r = Radius + h;
            float px = r * MathF.Cos(phi);
            float py = r * MathF.Sin(phi);
            float pz = Y + ZDisp;
            _posOut[i] = (px, py, pz);

            // rotation: atan2 + quaternion multiply (approximated as angle computation)
            float ang = MathF.Atan2(py, px) * (180f / MathF.PI);
            // AngleAxis(ang, Z) * ConstRot — represent as scalar angle only for CPU baseline
            _rotOut[i] = (0f, 0f, MathF.Sin(ang * MathF.PI / 360f), MathF.Cos(ang * MathF.PI / 360f));
        }
    }

    // --- Flat shape (CurrentShape == 1) ---
    // pos = (X, h, Y + ZDisp)
    // rot = ToUpright (constant, no per-tile trig)
    [Benchmark(Description = "CPU flat transform — 1024 tiles")]
    public void CpuFlat_1024()
    {
        for (int i = 0; i < TileCount; i++)
        {
            var (X, Y, h) = _tiles[i];
            _posOut[i] = (X, h, Y + ZDisp);
            // flat rotation is constant (Euler 90,0,0) — no per-tile trig
            _rotOut[i] = (0.7071068f, 0f, 0f, 0.7071068f);
        }
    }

    // --- Aggregate: both shapes interleaved (realistic mixed world) ---
    [Benchmark(Description = "CPU mixed (cyl+flat) transform — 1024 tiles")]
    public void CpuMixed_1024()
    {
        for (int i = 0; i < TileCount; i++)
        {
            var (X, Y, h) = _tiles[i];
            if ((i & 1) == 0)
            {
                float phi = -X / Radius;
                float r = Radius + h;
                float px = r * MathF.Cos(phi);
                float py = r * MathF.Sin(phi);
                _posOut[i] = (px, py, Y + ZDisp);
                float ang = MathF.Atan2(py, px) * (180f / MathF.PI);
                _rotOut[i] = (0f, 0f, MathF.Sin(ang * MathF.PI / 360f), MathF.Cos(ang * MathF.PI / 360f));
            }
            else
            {
                _posOut[i] = (X, h, Y + ZDisp);
                _rotOut[i] = (0.7071068f, 0f, 0f, 0.7071068f);
            }
        }
    }
}
