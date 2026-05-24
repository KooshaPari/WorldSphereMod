using System;
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Xunit;

public class LiveVerificationIntegrationTests
{
    private const string ScriptRelative = "Tools/wsm-ssim-compare.py";
    private const string LiveVerificationDocRelative = "docs/live-verification.md";

    private static string RepoRoot => TestRepo.FindRoot();

    [Fact]
    public void Wsm_ssim_compare_script_exists_at_Tools_wsm_ssim_compare_py()
    {
        var scriptPath = Path.Combine(RepoRoot, ScriptRelative.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(scriptPath).Should().BeTrue(
            "live verification must ship Tools/wsm-ssim-compare.py for SSIM gates");
    }

    [Fact]
    public void Live_verification_doc_documents_ssim_tool_and_default_threshold()
    {
        var doc = TestRepo.ReadRelative(LiveVerificationDocRelative);

        doc.Should().Contain("Tools/wsm-ssim-compare.py");
        doc.Should().Contain("wsm-live-verify.ps1");
        doc.Should().Contain("0.95");
        doc.Should().Contain("\"ok\"");
        doc.Should().Contain("\"ssim\"");
        doc.Should().Contain("\"threshold\"");
    }

    [Fact]
    public void Wsm_ssim_compare_help_works()
    {
        var scriptPath = Path.Combine(RepoRoot, ScriptRelative.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(scriptPath).Should().BeTrue(ScriptRelative);

        if (!TryRunPython(scriptPath, "--help", out var stdout, out var stderr, out var exitCode))
        {
            var script = File.ReadAllText(scriptPath);
            script.Should().Contain("--threshold");
            script.Should().Contain("default=0.95");
            script.Should().Contain("Compare two PNG screenshots");
            return;
        }

        exitCode.Should().Be(0, $"stderr: {stderr}");
        stdout.Should().Contain("SSIM-compare two PNG fixtures");
        stdout.Should().Contain("--threshold");
    }

    private static bool TryRunPython(
        string scriptPath,
        string arguments,
        out string stdout,
        out string stderr,
        out int exitCode)
    {
        stdout = string.Empty;
        stderr = string.Empty;
        exitCode = -1;

        foreach (var executable in new[] { "python", "python3", "py" })
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = executable == "py"
                        ? $"-3 \"{scriptPath}\" {arguments}"
                        : $"\"{scriptPath}\" {arguments}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = RepoRoot,
                };

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    continue;
                }

                stdout = process.StandardOutput.ReadToEnd();
                stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(TimeSpan.FromSeconds(15));
                exitCode = process.ExitCode;
                return true;
            }
            catch (Exception)
            {
                // try next python launcher
            }
        }

        return false;
    }
}
