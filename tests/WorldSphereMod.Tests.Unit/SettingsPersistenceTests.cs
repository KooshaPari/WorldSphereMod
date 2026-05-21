using System.IO;
using Xunit;
using FluentAssertions;

/// <summary>
/// FR-WSM-013: Settings Persistence Across Launches.
/// Static-source verification that the PlayerConfig.dict ↔ SavedSettings
/// mirror landed (commit 5a60013) — required for default-true phases to
/// survive kill+launch cycles.
/// </summary>
public class SettingsPersistenceTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static string ReadTabSource() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(),
            "WorldSphereMod", "Code", "WorldSphereTab.cs"));

    [Fact]
    public void RegisterToggleButton_MirrorsEnabledToPlayerConfig()
    {
        var src = ReadTabSource();
        // FR-WSM-013 acceptance: registration must unconditionally write
        // PlayerConfig.dict[ID].boolVal = Enabled (not just set false).
        src.Should().Contain("PlayerConfig.dict[ID].boolVal = Enabled");
    }

    [Fact]
    public void RegisterToggleButton_MirrorsToSavedSettingsViaReflection()
    {
        var src = ReadTabSource();
        // The reflection mirror code that writes SavedSettings.<ID> alongside
        // PlayerConfig.dict. Required so the two sources of truth agree at
        // registration time.
        src.Should().Contain("typeof(SavedSettings).GetField(ID)");
        src.Should().Contain("field.SetValue(Core.savedSettings, Enabled)");
    }

    [Fact]
    public void NoLeftoverConditionalFalseShortcut()
    {
        var src = ReadTabSource();
        // Old buggy pattern was: `if (!Enabled) PlayerConfig.dict[ID].boolVal = false;`
        // which left PlayerConfig default false when Enabled=true. Make sure
        // that conditional pattern is gone in favor of the unconditional mirror.
        src.Should().NotContain("if (!Enabled)\n            {\n                PlayerConfig.dict[ID].boolVal = false;");
    }
}
