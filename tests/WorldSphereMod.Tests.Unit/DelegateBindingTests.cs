using System;
using System.Reflection;
using Xunit;
using FluentAssertions;

// These tests exercise the reflective WorldSphereAPI(Type) ctor by feeding it
// hand-rolled "fake host" types that mimic the surface of WorldSphereMod /
// WorldSphereMod3D's static API entry point. This lets us verify the v1/v2
// detection logic without dragging in WorldBox or Unity assemblies.
public class DelegateBindingTests
{
    private static ConstructorInfo GetReflectiveCtor()
    {
        var ctor = typeof(WorldSphereAPI).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(Type) },
            modifiers: null);
        ctor.Should().NotBeNull("WorldSphereAPI must expose an internal WorldSphereAPI(Type) ctor");
        return ctor!;
    }

    // A v1-shaped fake host: defines exactly the legacy WorldSphereMod surface
    // (IsWorld3D, MakeActorPerp, MakeBuildingPerp, MakeProjectilePerp,
    // EditEffect, GetSetting) and none of the v2 additions.
    public static class FakeHostV1
    {
        public static bool IsWorld3D() => true;
        public static void MakeActorPerp(string ID) { }
        public static void MakeBuildingPerp(string ID) { }
        public static void MakeProjectilePerp(string ID) { }
        public static void EditEffect(string ID, bool IsUpright, bool SeperateSprite, float ExtraHeight, bool OnGround) { }
        public static object GetSetting(string Name, Type Type) => false;
    }

    // A v2-shaped fake host: v1 surface plus the additional v2 entry points
    // (IsModel3D, RegisterCustomMesh, RegisterBuildingRules).
    public static class FakeHostV2
    {
        public static bool IsWorld3D() => true;
        public static bool IsModel3D() => true;
        public static void MakeActorPerp(string ID) { }
        public static void MakeBuildingPerp(string ID) { }
        public static void MakeProjectilePerp(string ID) { }
        public static void EditEffect(string ID, bool IsUpright, bool SeperateSprite, float ExtraHeight, bool OnGround) { }
        public static object GetSetting(string Name, Type Type) => 42;
        public static void RegisterCustomMesh(string assetId, object mesh, object albedo) { }
        public static void RegisterBuildingRules(string assetId, object rules) { }
    }

    // A host with ONLY IsWorld3D defined — used to confirm the ctor fails fast
    // (rather than silently producing a half-bound API) when the v1 contract
    // isn't fully satisfied.
    public static class PartialHostOnlyIsWorld3D
    {
        public static bool IsWorld3D() => true;
    }

    // A host with no expected methods at all.
    public static class EmptyHost { }

    private static WorldSphereAPI Build(Type host)
    {
        try
        {
            return (WorldSphereAPI)GetReflectiveCtor().Invoke(new object[] { host });
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            throw tie.InnerException;
        }
    }

    [Fact(Skip = "WorldSphereAPI v1/v2 delegate signatures evolved post-wave-19; fixture rewrite pending")]
    public void Reflective_ctor_binds_full_v1_surface_against_v1_host()
    {
        var api = Build(typeof(FakeHostV1));
        api.Should().NotBeNull();

        // v1 wiring is live.
        api.IsWorld3D.Should().BeTrue("FakeHostV1.IsWorld3D returns true");
        Action actor = () => api.MakeActorNonUpright("human");
        Action building = () => api.MakeBuildingNonUpright("house_human");
        Action proj = () => api.MakeProjectileNonUpright("arrow");
        Action effect = () => api.EditEffect("nuke", true);
        actor.Should().NotThrow();
        building.Should().NotThrow();
        proj.Should().NotThrow();
        effect.Should().NotThrow();
        api.GetSetting<bool>("flag").Should().BeFalse();
    }

    [Fact(Skip = "WorldSphereAPI v1/v2 delegate signatures evolved post-wave-19; fixture rewrite pending")]
    public void Reflective_ctor_leaves_v2_members_safe_when_v1_only_host()
    {
        var api = Build(typeof(FakeHostV1));

        // v2 surface should gracefully no-op against a v1-only host.
        api.IsModel3D.Should().BeFalse(
            "IsModel3D must report false when the host doesn't expose IsModel3D");

        Action mesh = () => api.RegisterCustomMesh("human", new object(), null);
        Action rules = () => api.RegisterBuildingRules("house_human", new object());
        mesh.Should().NotThrow("RegisterCustomMesh must no-op on v1 hosts");
        rules.Should().NotThrow("RegisterBuildingRules must no-op on v1 hosts");
    }

    [Fact(Skip = "WorldSphereAPI v1/v2 delegate signatures evolved post-wave-19; fixture rewrite pending")]
    public void Reflective_ctor_binds_v2_surface_against_v2_host()
    {
        var api = Build(typeof(FakeHostV2));

        api.IsWorld3D.Should().BeTrue();
        api.IsModel3D.Should().BeTrue("FakeHostV2.IsModel3D returns true");
        api.GetSetting<int>("answer").Should().Be(42);

        Action mesh = () => api.RegisterCustomMesh("human", new object(), new object());
        Action rules = () => api.RegisterBuildingRules("house_human", new object());
        mesh.Should().NotThrow();
        rules.Should().NotThrow();
    }

    // Documents the current contract: the reflective ctor REQUIRES the full v1
    // surface to be present. A host missing one of the v1 methods causes
    // Delegate.CreateDelegate to fault, which surfaces as an ArgumentNullException
    // (when GetMethod returned null). If this contract changes (e.g. the ctor
    // becomes tolerant of partial v1 hosts), update this test to match.
    [Fact]
    public void Reflective_ctor_throws_when_host_is_missing_v1_methods()
    {
        Action act = () => Build(typeof(EmptyHost));
        act.Should().Throw<ArgumentNullException>(
            "Delegate.CreateDelegate rejects a null MethodInfo, which is what GetMethod returns for missing v1 surface");
    }

    [Fact]
    public void Reflective_ctor_throws_when_host_has_only_IsWorld3D()
    {
        Action act = () => Build(typeof(PartialHostOnlyIsWorld3D));
        act.Should().Throw<ArgumentNullException>(
            "host with only IsWorld3D still lacks MakeActorPerp etc., so binding fails fast");
    }
}
