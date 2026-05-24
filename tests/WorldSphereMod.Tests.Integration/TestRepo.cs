using System.IO;
using FluentAssertions;

internal static class TestRepo
{
    internal static string FindRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    internal static string ReadRelative(string relativePath)
    {
        var fullPath = Path.Combine(FindRoot(), relativePath);
        File.Exists(fullPath).Should().BeTrue($"expected file at {fullPath}");
        return File.ReadAllText(fullPath);
    }
}
