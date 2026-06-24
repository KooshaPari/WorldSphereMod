using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// ADR-0013 guard for Core.Sphere.SafeShaders. The wsm3d-shaders bundle ships 10 BRP
/// shaders, but only OpaqueVertexColor survives bundle deserialization with a valid
/// Shader.name on this Unity 2022.3 cross-patch build. The other shaders trigger a
/// native ManagedStream crash that C# try/catch cannot intercept and take the whole
/// mod offline. SafeShaders must therefore stay EXACTLY { OpaqueVertexColor }, with the
/// ADR-0013 rationale comment present so a future contributor doesn't re-expand it.
/// </summary>
public sealed class SafeShadersGuardTests
{
    const string CoreRelative = "WorldSphereMod/Code/Core.cs";

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

    static string ReadSource(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue($"source file must exist at {path}");
        return File.ReadAllText(path);
    }

    static string ExtractArrayInitializer(string source)
    {
        // Match: public static readonly string[] SafeShaders = new[] { ... };
        var match = Regex.Match(source,
            @"SafeShaders\s*=\s*new\[\]\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline);
        match.Success.Should().BeTrue("SafeShaders must be declared as an array initializer");
        return match.Groups["body"].Value;
    }

    [Fact]
    public void SafeShaders_contains_exactly_OpaqueVertexColor()
    {
        var body = ExtractArrayInitializer(ReadSource(CoreRelative));

        var entries = Regex.Matches(body, "\"(?<name>[^\"]+)\"");
        entries.Count.Should().Be(1,
            "SafeShaders must contain exactly one shader (ADR-0013: only OpaqueVertexColor deserializes safely)");
        entries[0].Groups["name"].Value.Should().Be("OpaqueVertexColor",
            "the single SafeShaders entry must be OpaqueVertexColor");
    }

    [Fact]
    public void SafeShaders_declaration_cites_ADR_0013_and_warns_against_expansion()
    {
        var source = ReadSource(CoreRelative);

        source.Should().Contain("ADR-0013",
            "the SafeShaders region must reference ADR-0013 so the crash rationale is discoverable");
        source.Should().Contain("DO NOT ADD MORE SHADERS to SafeShaders",
            "a do-not-expand warning must guard the SafeShaders enumeration loop");
    }
}
