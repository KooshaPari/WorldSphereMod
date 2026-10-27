using System.Collections;
using System.IO;
using UnityEngine;

namespace WorldSphereMod
{

public sealed class AutoScreenshotDriver : MonoBehaviour
{
    private const int MaxScreenshots = 20;
    private int _screenshotCount;
    private const float DefaultIntervalSeconds = 60f;
    private static AutoScreenshotDriver _owner;
    private bool _loopStarted;
    private int _generation;

    private void Start()
    {
        Debug.Log("[WSM3D] AutoScreenshotDriver.Start savedSettings=" + (Core.savedSettings != null) +
                  " enabled=" + (Core.savedSettings != null ? Core.savedSettings.AutoScreenshotEnabled : false));
        EnsureLoop();
    }

    private void OnEnable() => EnsureLoop();

    private void EnsureLoop()
    {
        if (_loopStarted || !isActiveAndEnabled || (_owner != null && _owner != this)) return;
        _owner = this;
        _loopStarted = true;
        StartCoroutine(CaptureLoop());
    }

    private void OnDisable()
    {
        _generation++;
        StopAllCoroutines();
        _loopStarted = false;
        if (_owner == this) _owner = null;
    }

    private IEnumerator CaptureLoop()
    {
        while (_screenshotCount < MaxScreenshots)
        {
            var interval = Core.savedSettings != null
                ? Core.savedSettings.AutoScreenshotIntervalSeconds
                : DefaultIntervalSeconds;

            if (interval <= 0f || float.IsNaN(interval) || float.IsInfinity(interval))
            {
                interval = DefaultIntervalSeconds;
            }

            yield return new WaitForSeconds(interval);

            if (Core.savedSettings == null || !Core.savedSettings.AutoScreenshotEnabled) continue;
            var path = GetNextScreenshotPath();
            if (path != null)
            {
                bool finished = false;
                bool ok = false;
                string error = string.Empty;
                var generation = _generation;
                var configuredDirectory = Path.GetDirectoryName(path);
                yield return StartCoroutine(ScreenshotCapture.CaptureCoroutine(path, (savedPath, success, message) =>
                {
                    ok = success;
                    error = message;
                    finished = true;
                    // Gated behind ProfilerDump: fires per capture interval, flooding the viewport during capture runs.
                    if (success && Core.savedSettings != null && Core.savedSettings.ProfilerDump)
                    {
                        Debug.Log("[WSM3D] AutoScreenshot saved " + savedPath);
                    }
                }, () => generation == _generation && isActiveAndEnabled && _owner == this &&
                    Core.savedSettings != null && Core.savedSettings.AutoScreenshotEnabled &&
                    TryGetDirectory(out var currentDirectory) && currentDirectory == configuredDirectory));
                if (error == "capture_cancelled") continue;
                if (!finished || !ok)
                {
                    Debug.LogWarning("[WSM3D] AutoScreenshot failed: " + error);
                    yield break;
                }
                _screenshotCount++;
            }
            else
            {
                yield break;
            }
        }
    }

    private string GetNextScreenshotPath()
    {
        if (!TryGetDirectory(out var basePath))
        {
            Debug.LogWarning("[WSM3D] AutoScreenshot requires an explicit valid absolute output directory; capture stopped.");
            return null;
        }

        var fileName = "in-mod-" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + System.Guid.NewGuid().ToString("N") + ".png";
        // The directory was canonicalized and validated above. Combining a fixed
        // safe leaf avoids a second unguarded path resolution.
        return Path.Combine(basePath, fileName);
    }

    private static bool TryGetDirectory(out string directory)
    {
        directory = null;
        var configured = Core.savedSettings?.AutoScreenshotPath;
        if (string.IsNullOrWhiteSpace(configured)) return false;
        try
        {
            // Path.IsPathRooted also accepts drive-relative Windows paths (C:foo),
            // so compare the fully qualified result instead. Never choose a fallback.
            if (!Path.IsPathRooted(configured) || configured.IndexOfAny(new[] { '*', '?', '\0' }) >= 0) return false;
            var full = Path.GetFullPath(configured);
            var trimmed = configured.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), trimmed,
                    System.StringComparison.OrdinalIgnoreCase)) return false;
            if (full.StartsWith(@"\\", System.StringComparison.Ordinal)) return false;
            if (string.Equals(full, Path.GetPathRoot(full), System.StringComparison.OrdinalIgnoreCase) || File.Exists(full)) return false;
            // Reserve space for the generated leaf and avoid legacy Windows path
            // truncation/device handling. Automatic capture is local-path only.
            if (full.Length > 180) return false;
            for (var cursor = new DirectoryInfo(full); cursor != null; cursor = cursor.Parent)
            {
                if (cursor.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 && cursor.Parent != null) return false;
                if (cursor.Parent != null)
                {
                    if (cursor.Name.EndsWith(".") || cursor.Name.EndsWith(" ")) return false;
                    var stem = cursor.Name.Split('.')[0].ToUpperInvariant();
                    if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                        stem == "CONIN$" || stem == "CONOUT$" ||
                        (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) &&
                         "123456789\u00b9\u00b2\u00b3".IndexOf(stem[3]) >= 0)) return false;
                }
                try
                {
                    // Inspect attributes even for dangling directory links.
                    if ((File.GetAttributes(cursor.FullName) & FileAttributes.ReparsePoint) != 0) return false;
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            directory = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return true;
        }
        catch (System.Exception ex) when (ex is System.ArgumentException || ex is System.NotSupportedException ||
                                          ex is System.IO.IOException || ex is System.Security.SecurityException ||
                                          ex is System.UnauthorizedAccessException)
        {
            return false;
        }
    }
}
}
