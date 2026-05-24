using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

public class WorldSphereApiIntegrationTests
{
    private static readonly string[] ExpectedV2CapabilityStrings =
    {
        "IsWorld3D",
        "IsModel3D",
        "RegisterCustomMesh",
        "RegisterBuildingRules",
    };

    private const string HostApiVersionConstant = "2.0";
    private static WorldSphereAPI CreateBareInstance()
    {
        var ctor = typeof(WorldSphereAPI).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        ctor.Should().NotBeNull();
        return (WorldSphereAPI)ctor!.Invoke(null);
    }

    [Fact]
    public void WorldSphereAPI_assembly_loads_without_Unity_references()
    {
        var apiAssembly = typeof(WorldSphereAPI).Assembly;

        apiAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Should().NotContain(
                name => name != null && name.StartsWith("UnityEngine", StringComparison.Ordinal),
                "WorldSphereAPI must stay Unity-free so integration tests run on plain dotnet");
    }

    [Fact]
    public void Connect_returns_false_when_no_game_host_is_loaded()
    {
        var connected = WorldSphereAPI.Connect(out var api);

        connected.Should().BeFalse("no WorldSphereMod3D assembly is present in the test runner");
        api.Should().BeNull();
    }

    [Fact]
    public void Public_surface_exposes_v1_and_v2_members()
    {
        var apiType = typeof(WorldSphereAPI);
        var expectedMethods = new[]
        {
            "Connect",
            "GetVersion",
            "GetCapabilities",
            "HasFeature",
            "RegisterCustomMesh",
            "RegisterBuildingRules",
            "MakeActorNonUpright",
            "MakeBuildingNonUpright",
            "MakeProjectileNonUpright",
            "EditEffect",
            "GetSetting",
        };

        foreach (var method in expectedMethods)
        {
            apiType.GetMember(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Should().NotBeEmpty($"WorldSphereAPI must expose {method}");
        }

        apiType.GetProperty("IsWorld3D", BindingFlags.Public | BindingFlags.Instance)
            .Should().NotBeNull();
        apiType.GetProperty("IsModel3D", BindingFlags.Public | BindingFlags.Instance)
            .Should().NotBeNull();
    }

    [Fact]
    public void Discovery_surface_returns_safe_defaults_without_host()
    {
        var api = CreateBareInstance();

        api.GetVersion().Should().Be("unknown");
        api.GetCapabilities().Should().BeEmpty();
        api.HasFeature("RegisterCustomMesh").Should().BeFalse();
        api.IsModel3D.Should().BeFalse();
    }

    [Fact]
    public void Connect_probes_fork_and_upstream_assembly_names()
    {
        var connectSource = TestRepo.ReadRelative("WorldSphereAPI/WorldSphereAPI.cs");

        connectSource.Should().Contain("WorldSphereMod3D",
            "Connect must probe the fork assembly name first");
        connectSource.Should().Contain("THE_3D_WORLDBOX_MOD",
            "Connect must fall back to upstream WorldSphereMod assembly name");
        connectSource.Should().Contain("WorldSphereMod.API.WorldSphereModAPI",
            "Connect must target the host static API type");
    }

    [Fact]
    public void Host_api_declares_v2_capability_strings()
    {
        var hostSource = TestRepo.ReadRelative("WorldSphereMod/Code/WorldSphereAPI.cs");

        foreach (var capability in ExpectedV2CapabilityStrings)
        {
            hostSource.Should().Contain($"\"{capability}\"",
                $"host GetCapabilities must advertise v2 capability '{capability}'");
        }
    }

    [Fact]
    public void Host_api_HasFeature_switch_covers_v2_capability_strings()
    {
        var hostSource = TestRepo.ReadRelative("WorldSphereMod/Code/WorldSphereAPI.cs");

        foreach (var capability in ExpectedV2CapabilityStrings)
        {
            hostSource.Should().Contain($"case \"{capability}\":",
                $"host HasFeature must recognise v2 capability '{capability}'");
        }
    }

    [Fact]
    public void Host_api_GetVersion_returns_v2_constant()
    {
        var hostSource = TestRepo.ReadRelative("WorldSphereMod/Code/WorldSphereAPI.cs");

        hostSource.Should().Contain($"return \"{HostApiVersionConstant}\";",
            "host GetVersion must report the v2 API version constant");
    }

    [Fact]
    public void Core_settings_schema_version_constant_is_documented()
    {
        var coreSource = TestRepo.ReadRelative("WorldSphereMod/Code/Core.cs");

        coreSource.Should().Contain("SettingsVersion",
            "Core must expose a settings schema version constant for migrations");
        coreSource.Should().MatchRegex(
            @"SettingsVersion\s*=\s*""\d+\.\d+""",
            "SettingsVersion must be a quoted semver-like string");
    }

    [Fact]
    public void RegisterCustomMesh_signature_exists_on_proxy_and_host()
    {
        var proxyMethod = typeof(WorldSphereAPI).GetMethod(
            "RegisterCustomMesh",
            BindingFlags.Instance | BindingFlags.Public);
        proxyMethod.Should().NotBeNull("WorldSphereAPI must expose RegisterCustomMesh");
        proxyMethod!.GetParameters().Select(p => p.ParameterType)
            .Should().Equal(new[] { typeof(string), typeof(object), typeof(object) },
                "proxy RegisterCustomMesh must accept (string assetId, object mesh, object albedo)");

        var hostSource = TestRepo.ReadRelative("WorldSphereMod/Code/WorldSphereAPI.cs");
        hostSource.Should().Contain(
            "public static void RegisterCustomMesh(string assetId, object mesh, object albedo)",
            "host RegisterCustomMesh must match the Unity-free delegate signature");

        var proxySource = TestRepo.ReadRelative("WorldSphereAPI/WorldSphereAPI.cs");
        proxySource.Should().Contain(
            "delegate void RegisterCustomMesh(string assetId, object mesh, object albedo);",
            "proxy delegate must mirror the host RegisterCustomMesh signature");
    }

    [Fact]
    public void Proxy_TryBindOptional_binds_RegisterCustomMesh_from_host()
    {
        var proxySource = TestRepo.ReadRelative("WorldSphereAPI/WorldSphereAPI.cs");

        proxySource.Should().Contain(
            "TryBindOptional(WorldSpherePort, \"RegisterCustomMesh\", out registerCustomMesh)",
            "reflective ctor must optionally bind RegisterCustomMesh from the host");
    }

    [Theory]
    [MemberData(nameof(V2CapabilityStrings))]
    public void Bare_instance_HasFeature_returns_false_for_v2_capabilities(string capability)
    {
        var api = CreateBareInstance();
        api.HasFeature(capability).Should().BeFalse();
    }

    public static IEnumerable<object[]> V2CapabilityStrings() =>
        ExpectedV2CapabilityStrings.Select(c => new object[] { c });
}
