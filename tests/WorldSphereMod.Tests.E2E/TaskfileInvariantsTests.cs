using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public class TaskfileInvariantsTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var taskfile = Path.Combine(dir.FullName, "Taskfile.yaml");
            var buildProject = Path.Combine(dir.FullName, "WorldSphereMod.csproj");
            if (File.Exists(taskfile) && File.Exists(buildProject))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root with Taskfile.yaml and WorldSphereMod.csproj must be locatable from test cwd");
    }

    private static string ReadTaskfile()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "Taskfile.yaml");
        File.Exists(path).Should().BeTrue($"Taskfile.yaml must exist at {path}");
        return File.ReadAllText(path);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseTaskCommands(string taskfile)
    {
        var tasks = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var lines = taskfile.Replace("\r\n", "\n").Split('\n');
        var inTasks = false;
        string? currentTask = null;
        var currentCommands = new List<string>();

        static bool IsTaskHeader(string line, out string name)
        {
            var match = Regex.Match(line, @"^  (?<name>[A-Za-z0-9_.:-]+):\s*$");
            if (!match.Success)
            {
                name = string.Empty;
                return false;
            }

            name = match.Groups["name"].Value;
            return true;
        }

        void FlushCurrentTask()
        {
            if (currentTask != null)
            {
                tasks[currentTask] = new List<string>(currentCommands);
                currentCommands.Clear();
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (!inTasks)
            {
                if (line.TrimEnd() == "tasks:")
                {
                    inTasks = true;
                }

                continue;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (!line.StartsWith("  "))
            {
                break;
            }

            if (IsTaskHeader(line, out var taskName))
            {
                FlushCurrentTask();
                currentTask = taskName;
                continue;
            }

            if (currentTask == null)
            {
                continue;
            }

            if (!line.StartsWith("    "))
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed == "cmds:")
            {
                continue;
            }

            if (!trimmed.StartsWith("- "))
            {
                continue;
            }

            var command = trimmed.Substring(2);
            if (command == "|")
            {
                var block = new StringBuilder();
                while (i + 1 < lines.Length)
                {
                    var next = lines[i + 1];
                    if (next.Length == 0)
                    {
                        block.AppendLine();
                        i++;
                        continue;
                    }

                    if (next.StartsWith("      "))
                    {
                        block.AppendLine(next.Substring(6));
                        i++;
                        continue;
                    }

                    break;
                }

                currentCommands.Add(block.ToString().TrimEnd());
                continue;
            }

            currentCommands.Add(command);
        }

        FlushCurrentTask();

        return tasks.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<string>)kvp.Value, StringComparer.Ordinal);
    }

    private static string GetTaskCommand(IReadOnlyDictionary<string, IReadOnlyList<string>> taskCommands, string taskName)
    {
        taskCommands.Should().ContainKey(taskName, $"Taskfile.yaml must define the '{taskName}' task");
        return string.Join("\n", taskCommands[taskName]);
    }

    [Fact]
    public void Taskfile_exposes_guarded_test_entrypoints_for_all_current_suites()
    {
        var taskCommands = ParseTaskCommands(ReadTaskfile());

        var test = GetTaskCommand(taskCommands, "test");
        var testIntegration = GetTaskCommand(taskCommands, "test-integration");
        var testE2E = GetTaskCommand(taskCommands, "test-e2e");
        var testAll = GetTaskCommand(taskCommands, "test-all");

        test.Should().Contain("if [ -d \"tests/WorldSphereMod.Tests.Unit\" ]");
        testIntegration.Should().Contain("if [ -d \"tests/WorldSphereMod.Tests.Integration\" ]");
        testE2E.Should().Contain("if [ -d \"tests/WorldSphereMod.Tests.E2E\" ]");

        test.Should().Contain("dotnet test tests/WorldSphereMod.Tests.Unit/");
        testIntegration.Should().Contain("dotnet test tests/WorldSphereMod.Tests.Integration/");
        testE2E.Should().Contain("dotnet test tests/WorldSphereMod.Tests.E2E/");
        testAll.Should().Contain("task: test");
        testAll.Should().Contain("task: test-integration");
        testAll.Should().Contain("task: test-e2e");
    }

    [Fact]
    public void Taskfile_build_instructions_match_the_repo_build_contract()
    {
        var taskCommands = ParseTaskCommands(ReadTaskfile());
        var build = GetTaskCommand(taskCommands, "build");
        var buildTests = GetTaskCommand(taskCommands, "build-tests");

        build.Should().Contain("dotnet build WorldSphereMod.csproj -c Release",
            "the repo build contract is pinned in AGENTS.md and should not drift");
        buildTests.Should().Contain("dotnet build tests/WorldSphereMod.Tests.Unit -c Release",
            "the unit test build path should stay aligned with the documented command");
    }
}
