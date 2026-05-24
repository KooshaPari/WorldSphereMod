using System;
using System.Text;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using Newtonsoft.Json;

namespace WorldSphereMod.Tests.Bench;

[MemoryDiagnoser]
public class AllBenches
{
    private const int TargetPayloadBytes = 600;
    private const string Wsm3dLine = "[WSM3D] InitProfiler Init = 0.1250s (125ms)";

    private string _settingsJson = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        var settings = CreateSavedSettings();
        var json = JsonConvert.SerializeObject(settings);

        // Approximate a 600-byte SavedSettings payload by whitespace padding only.
        // Whitespace is valid JSON and preserves round-trip behavior for helper-only benchmarks.
        var padLength = Math.Max(0, TargetPayloadBytes - Encoding.UTF8.GetByteCount(json));
        _settingsJson = json + new string(' ', padLength);
    }

    [Benchmark]
    public float WSMath_Clamp_Float()
    {
        return WSMath.Clamp(12.34f, 0f, 5.5f);
    }

    [Benchmark]
    public SavedSettings SavedSettings_JsonRoundTrip()
    {
        var roundTrip = JsonConvert.DeserializeObject<SavedSettings>(_settingsJson);
        if (roundTrip == null)
        {
            throw new InvalidOperationException("SavedSettings JSON round-trip produced null");
        }

        return roundTrip;
    }

    [Benchmark]
    public bool Wsm3d_LogRegex_CompileAndMatch()
    {
        var regex = new Regex(@"^\[WSM3D\]\s+\w+\s+\w+\s*=\s*[0-9]+\.[0-9]+s\s+\([0-9]+ms\)$", RegexOptions.CultureInvariant);
        return regex.IsMatch(Wsm3dLine);
    }

    private static SavedSettings CreateSavedSettings()
    {
        return new SavedSettings
        {
            Version = "2.0",
            Is3D = true,
            InvertedCameraMovement = false,
            PerlinNoise = true,
            UpsideDownMovement = true,
            RotateStuffToCamera = true,
            CameraRotatesWithWorld = true,
            FirstPerson = true,
            RenderRange = 2,
            TileHeight = 1,
            BuildingSize = 0.5f,
            CurrentShape = 1,
            VoxelEntities = false,
            ProceduralBuildings = false,
            BuildingStyleProcgen = false,
            CrossedQuadFoliage = true,
            MeshWater = false,
            HighShadows = false,
            SkeletalAnimation = false,
            WorldspaceUI = true,
            DayNightCycle = false,
            FogDensity = 0,
            PostFX = false,
            ParticleEffects = true,
            LODScale = 1.0f,
            WaterDetail = 1.0f,
            FoliageDensity = 1.0f,
            ProfilerDump = false
        };
    }
}

internal static class WSMath
{
    public static float Clamp(float value, float min, float max)
        => value < min ? min : (value > max ? max : value);

    public static int Clamp(int value, int min, int max)
        => value < min ? min : (value > max ? max : value);

    public static double Clamp(double value, double min, double max)
        => value < min ? min : (value > max ? max : value);
}

public class SavedSettings
{
    public string Version = "2.0";
    public bool Is3D = true;
    public bool InvertedCameraMovement = false;
    public bool PerlinNoise = true;
    public bool UpsideDownMovement = true;
    public bool RotateStuffToCamera = true;
    public bool CameraRotatesWithWorld = true;
    public bool FirstPerson = true;
    public float RenderRange = 2;
    public float TileHeight = 1;
    public float BuildingSize = 0.5f;
    public int CurrentShape = 1;
    public bool VoxelEntities = false;
    public bool ProceduralBuildings = false;
    public bool BuildingStyleProcgen = false;
    public bool CrossedQuadFoliage = true;
    public bool MeshWater = false;
    public bool HighShadows = false;
    public bool SkeletalAnimation = false;
    public bool WorldspaceUI = true;
    public bool DayNightCycle = false;
    public float FogDensity = 0.0f;
    public bool PostFX = false;
    public bool ParticleEffects = true;
    public float LODScale = 1.0f;
    public float WaterDetail = 1.0f;
    public float FoliageDensity = 1.0f;
    public bool ProfilerDump = false;
}
