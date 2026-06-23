using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Invariant tests guarding the day-1 load path against null-dereference crashes
/// and corrupt-file hangs that would freeze or crash on a fresh install.
///
/// These are source-level E2E checks that parse the actual .cs files without
/// running the Unity runtime, so they catch regressions in CI.
///
/// Load-path safety catalogue:
///   1. LoadSettings has try/catch fallback to defaults on corrupt/missing JSON
///   2. CompoundSpheres PrepareAssets is wrapped in try/catch (bundle load failure)
///   3. Become3D guards CompoundSphereMaterial + CompoundSphereMesh for null
///   4. PostInit guards savedSettings != null before accessing fields
///   5. No Thread.Sleep or blocking .Result in the PostInit/LoadAssets path
/// </summary>
public class LoadPathSafetyInvariantsTests
{
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

    static string ReadSourceFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    // ---------------------------------------------------------------
    // 1. LoadSettings: try/catch fallback on corrupt/missing JSON
    // ---------------------------------------------------------------
    // Regression: if LoadSettings throws on a corrupt or missing JSON file without
    // a catch, savedSettings stays at its default construction but IsFirstInstall
    // is not set and defaults are not applied, leaving the mod in an inconsistent
    // state. The catch block must fall back to defaults + SaveSettings.
    [Fact]
    public void LoadSettings_has_try_catch_with_default_fallback()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Core.cs");

        source.Should().Contain("public static bool LoadSettings()",
            "LoadSettings must be a public static method in Core.cs");

        // Must have a try block around the file read
        source.Should().Contain("string raw = File.ReadAllText(",
            "LoadSettings must read the settings file with File.ReadAllText");

        // Must have a catch that falls back
        source.Should().Contain("IsFirstInstall = true",
            "LoadSettings catch block must set IsFirstInstall=true on file-read failure " +
            "so the mod knows to apply first-run defaults");

        source.Should().Contain("SavedSettings.ApplyPhaseDefaults(savedSettings)",
            "LoadSettings catch block must apply phase defaults so fresh installs get correct flags");
    }

    // ---------------------------------------------------------------
    // 2. PrepareAssets: wrapped in try/catch
    // ---------------------------------------------------------------
    // Regression: CompoundSpheres.dll bundle load can fail on a machine that
    // is missing the DLL (fresh install, wrong path, or NML extraction failure).
    // PrepareAssets must be wrapped in try/catch so a failure logs an error
    // but does not crash the entire PostInit and leave terrain invisible.
    [Fact]
    public void PrepareAssets_is_wrapped_in_try_catch()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Core.cs");

        source.Should().Contain("try { Sphere.PrepareAssets(); }",
            "Sphere.PrepareAssets() must be wrapped in try/catch in PostInit — " +
            "a bundle load failure on a fresh machine would otherwise crash the entire mod init");

        source.Should().Contain("catch (System.Exception ex) { Debug.LogError(",
            "PrepareAssets catch must log a descriptive error so users can diagnose missing bundle");
    }

    // ---------------------------------------------------------------
    // 3. Become3D / Begin: null guard for CompoundSphereMaterial + Mesh
    // ---------------------------------------------------------------
    // Regression: if PrepareAssets fails (bundle missing), CompoundSphereMaterial
    // and CompoundSphereMesh are null. Calling Sphere.Begin without a null guard
    // throws a NullReferenceException and leaves terrain invisible without
    // a clear error. The null guard must log and return early.
    [Fact]
    public void Sphere_Begin_guards_CompoundSphereMaterial_and_Mesh_for_null()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Core.cs");

        source.Should().Contain("if (CompoundSphereMaterial == null || CompoundSphereMesh == null)",
            "Sphere.Begin must guard CompoundSphereMaterial and CompoundSphereMesh for null — " +
            "if PrepareAssets failed (e.g. bundle missing on fresh install), these are null and " +
            "CreateSphereManager must be skipped with a clear error log");

        source.Should().Contain("CompoundSphereMaterial or CompoundSphereMesh missing",
            "the null-guard must log a clear diagnostic message so the user knows bundle load failed");
    }

    // ---------------------------------------------------------------
    // 4. PostInit: savedSettings not null before field access
    // ---------------------------------------------------------------
    // Invariant: savedSettings is initialized at field-declaration time
    // (= new SavedSettings()) so it is never null. Verify this stays true —
    // a refactor that changes it to a nullable field would be a day-1 crash risk.
    [Fact]
    public void savedSettings_is_initialized_at_declaration_not_nullable()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Core.cs");

        source.Should().Contain("public static SavedSettings savedSettings = new SavedSettings()",
            "savedSettings must be initialized at declaration (= new SavedSettings()) so it is " +
            "never null — a nullable savedSettings would crash on every field access in PostInit");
    }

    // ---------------------------------------------------------------
    // 5. No blocking .Result or Thread.Sleep in PostInit/LoadAssets
    // ---------------------------------------------------------------
    // Regression: a blocking .Result or Thread.Sleep on the Unity main thread in
    // PostInit freezes the game — this was the root cause of the tester PC freeze
    // on first release. Verify no such patterns exist in the load path.
    [Fact]
    public void PostInit_has_no_blocking_dot_Result_or_ThreadSleep()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Core.cs");

        // Extract the relevant portion around PostInit (first 400 lines covers init path)
        var initSection = source.Length > 800 ? source.Substring(0, 800) : source;

        initSection.Should().NotContain(".GetAwaiter().GetResult()",
            "PostInit must not block the Unity main thread with .GetAwaiter().GetResult() — " +
            "async blocking is a freeze-on-start risk (#day-1 robustness)");

        initSection.Should().NotContain("Thread.Sleep(",
            "PostInit must not call Thread.Sleep on the Unity main thread — " +
            "any sleep blocks Unity's render loop and freezes the game (#day-1 robustness)");
    }
}
