using System;
using System.IO;
using FluentAssertions;
using Xunit;

public sealed class ConditionalPatchDispatchInvariantsTests
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

    private static string ReadRepoFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(fullPath).Should().BeTrue($"file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void Adr_0007_conditional_patch_dispatch_is_proposed_and_documents_harmony_gate()
    {
        var adr = ReadRepoFile("docs/adr/ADR-0007-conditional-patch-dispatch.md");

        adr.Should().Contain("**Status:** Proposed");
        adr.Should().Contain("Conditional Harmony patch dispatch");
        adr.Should().Contain("PhasePatchGate");
        adr.Should().Contain("[Phase]");
    }

    [Fact]
    public void PhasePatchGate_scaffold_exposes_init_dispatch_predicate()
    {
        var gate = ReadRepoFile("WorldSphereMod/Code/PhasePatchGate.cs");

        gate.Should().Contain("ADR-0007-conditional-patch-dispatch.md");
        gate.Should().Contain("ShouldApplyHarmonyPatch(Type type, SavedSettings settings)");
        gate.Should().Contain("IsSettingsFlagEnabled(SavedSettings settings, string flagName)");
        gate.Should().Contain("GetCustomAttribute<PhaseAttribute>()");
    }

    [Fact]
    public void Core_Patch_uses_PhasePatchGate_for_conditional_harmony_dispatch()
    {
        var core = ReadRepoFile("WorldSphereMod/Code/Core.cs");

        core.Should().Contain("PhasePatchGate.ShouldApplyHarmonyPatch(type, savedSettings)",
            "init-time Harmony dispatch must use the central gate helper");
        core.Should().NotContain("flagField.GetValue(savedSettings)",
            "SavedSettings flag reads should not be duplicated inline in Core.Patch");
    }

    [Fact]
    public void PhaseAttribute_documents_conditional_patch_contract()
    {
        var phaseAttr = ReadRepoFile("WorldSphereMod/Code/PhaseAttribute.cs");

        phaseAttr.Should().Contain("conditionally apply patches");
        phaseAttr.Should().Contain("Core.Patch()");
    }
}
