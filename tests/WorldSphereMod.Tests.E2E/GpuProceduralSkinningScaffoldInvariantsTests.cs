using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for ADR-0006 DrawProcedural GPU skinning scaffold
/// (docs/adr/ADR-0006-phase-6-step-9-drawprocedural-skinning.md).
/// </summary>
public sealed class GpuProceduralSkinningScaffoldInvariantsTests
{
    const string AdrRelativePath = "docs/adr/ADR-0006-phase-6-step-9-drawprocedural-skinning.md";

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    static string ReadRepoFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(fullPath).Should().BeTrue($"file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    static string ExtractMethodBody(string source, string signature)
    {
        int headerIndex = source.IndexOf(signature, StringComparison.Ordinal);
        headerIndex.Should().BeGreaterThanOrEqualTo(0, $"method signature should exist: {signature}");

        int openBrace = source.IndexOf('{', headerIndex);
        openBrace.Should().BeGreaterThanOrEqualTo(0, "method must open with a '{'");

        int depth = 0;
        for (int i = openBrace; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '{')
            {
                depth++;
                continue;
            }

            if (c != '}')
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return source.Substring(openBrace + 1, i - openBrace - 1);
            }
        }

        throw new InvalidOperationException("Unbalanced braces while extracting method body");
    }

    [Fact]
    public void Adr_0006_documents_implementation_status_and_gpu_scaffold()
    {
        var adr = ReadRepoFile(AdrRelativePath);

        adr.Should().Contain("## Implementation status");
        adr.Should().Contain("RigGpuSkinning.cs");
        adr.Should().Contain("GpuProceduralSkinning");
        adr.Should().Contain("GpuProceduralSkinningScaffoldInvariantsTests");
    }

    [Fact]
    public void SavedSettings_exposes_GpuProceduralSkinning_default_off()
    {
        var settings = ReadRepoFile("WorldSphereMod/Code/SavedSettings.cs");

        settings.Should().Contain("public bool GpuProceduralSkinning = false");
        settings.Should().Contain("ADR-0006");
    }

    [Fact]
    public void RigGpuSkinning_scaffold_documents_adr_stubs_and_instance_cap()
    {
        var scaffold = ReadRepoFile("WorldSphereMod/Code/Rig/RigGpuSkinning.cs");

        scaffold.Should().Contain("ADR-0006-phase-6-step-9-drawprocedural-skinning.md");
        scaffold.Should().Contain("public const int kMaxInstancesPerRig = 8");
        scaffold.Should().Contain("public static bool IsEnabled(SavedSettings");
        scaffold.Should().Contain("GpuProceduralSkinning");
        scaffold.Should().Contain("public static bool CanDispatchGPU()");
        scaffold.Should().Contain("public static void TickFrame()");
        scaffold.Should().Contain("public static void Clear()");
        scaffold.Should().Contain("public static void DispatchSkin(");
        scaffold.Should().Contain("public static void FlushDraws()");
        scaffold.Should().Contain("DrawProceduralIndirect");
        scaffold.Should().Contain("ComputeBuffer");
        scaffold.Should().Contain("PositionBuffer");
        scaffold.Should().Contain("IndirectArgsBuffer");
        scaffold.Should().Contain("PositionBufferElementCount");
        scaffold.Should().Contain("vertexCount * kMaxInstancesPerRig");
        scaffold.Should().Contain("1000 actors");
    }

    [Fact]
    public void RigDriver_Update_and_Clear_wire_gpu_scaffold_when_flags_enabled()
    {
        var rigDriver = ReadRepoFile("WorldSphereMod/Code/Rig/RigDriver.cs");
        var updateBody = ExtractMethodBody(rigDriver, "public static void Update()");
        var clearBody = ExtractMethodBody(rigDriver, "public static void Clear()");

        updateBody.Should().Contain("RigGpuSkinning.IsEnabled(Core.savedSettings)",
            "GPU scaffold must gate on SavedSettings, not hard-coded true");
        updateBody.Should().Contain("RigGpuSkinning.TickFrame()",
            "per-frame GPU hook must run from RigDriver.Update when enabled");
        clearBody.Should().Contain("RigGpuSkinning.Clear()",
            "GPU buffers must release when skeletal rigs are cleared");
    }
}
