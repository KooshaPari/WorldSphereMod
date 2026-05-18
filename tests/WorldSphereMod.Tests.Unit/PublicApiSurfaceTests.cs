using System;
using System.Linq;
using System.Reflection;
using Xunit;
using FluentAssertions;

public class PublicApiSurfaceTests
{
    // Resolve the internal parameterless ctor on WorldSphereAPI so we can
    // exercise the public surface without a real WorldBox/WorldSphereMod3D host.
    private static WorldSphereAPI CreateBareInstance()
    {
        var ctor = typeof(WorldSphereAPI).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        ctor.Should().NotBeNull("WorldSphereAPI must expose an internal parameterless ctor for testing");
        return (WorldSphereAPI)ctor!.Invoke(null);
    }

    [Fact]
    public void Connect_returns_false_when_no_host_present()
    {
        var result = WorldSphereAPI.Connect(out var api);
        result.Should().BeFalse();
        api.Should().BeNull();
    }

    [Fact]
    public void WorldSphereAPI_internal_ctor_initializes_all_delegates_null()
    {
        var api = CreateBareInstance();

        var delegateFields = typeof(WorldSphereAPI)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType))
            .ToArray();

        delegateFields.Should().NotBeEmpty(
            "WorldSphereAPI is delegate-driven and must have at least one delegate field");

        foreach (var field in delegateFields)
        {
            field.GetValue(api).Should().BeNull(
                $"delegate field '{field.Name}' must be null on a freshly constructed API");
        }
    }

    [Fact]
    public void IsModel3D_returns_false_when_no_host()
    {
        var api = CreateBareInstance();
        // v2 surface: null-checked, must report "not a 3D model host".
        api.IsModel3D.Should().BeFalse();
    }

    [Fact]
    public void RegisterCustomMesh_is_noop_when_no_host()
    {
        var api = CreateBareInstance();
        Action act = () => api.RegisterCustomMesh("human", mesh: null!, albedo: null);
        act.Should().NotThrow("v2 RegisterCustomMesh must null-check the delegate");
    }

    [Fact]
    public void RegisterBuildingRules_is_noop_when_no_host()
    {
        var api = CreateBareInstance();
        Action act = () => api.RegisterBuildingRules("house_human", rules: new object());
        act.Should().NotThrow("v2 RegisterBuildingRules must null-check the delegate");
    }

    // v1 surface members (IsWorld3D, MakeActorNonUpright, MakeBuildingNonUpright,
    // MakeProjectileNonUpright, EditEffect, GetSetting<T>) invoke their delegate
    // unconditionally and therefore throw NullReferenceException when the API
    // was not produced by Connect(). These tests pin that behavior so any future
    // change to add null-guards (which would make them no-ops returning host-absent
    // defaults) shows up as a deliberate test change.
    [Fact]
    public void IsWorld3D_throws_when_no_host_v1_surface_is_unguarded()
    {
        var api = CreateBareInstance();
        Action act = () => { _ = api.IsWorld3D; };
        act.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void MakeActorNonUpright_throws_when_no_host_v1_surface_is_unguarded()
    {
        var api = CreateBareInstance();
        Action act = () => api.MakeActorNonUpright("human");
        act.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void MakeBuildingNonUpright_throws_when_no_host_v1_surface_is_unguarded()
    {
        var api = CreateBareInstance();
        Action act = () => api.MakeBuildingNonUpright("house_human");
        act.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void MakeProjectileNonUpright_throws_when_no_host_v1_surface_is_unguarded()
    {
        var api = CreateBareInstance();
        Action act = () => api.MakeProjectileNonUpright("arrow");
        act.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void EditEffect_throws_when_no_host_v1_surface_is_unguarded()
    {
        var api = CreateBareInstance();
        Action act = () => api.EditEffect("nuke", isUpright: true);
        act.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void GetSetting_throws_when_no_host_v1_surface_is_unguarded()
    {
        var api = CreateBareInstance();
        Action act = () => api.GetSetting<bool>("someFlag");
        act.Should().Throw<NullReferenceException>();
    }
}
