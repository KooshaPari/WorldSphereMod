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

    private void Start()
    {
        Debug.Log("[WSM3D] AutoScreenshotDriver.Start savedSettings=" + (Core.savedSettings != null) +
                  " enabled=" + (Core.savedSettings != null ? Core.savedSettings.AutoScreenshotEnabled : false));
        StartCoroutine(CaptureLoop());
    }

    private IEnumerator CaptureLoop()
    {
        while (_screenshotCount < MaxScreenshots)
        {
            var interval = Core.savedSettings != null
                ? Core.savedSettings.AutoScreenshotIntervalSeconds
                : DefaultIntervalSeconds;

            if (interval <= 0f)
            {
                interval = DefaultIntervalSeconds;
            }

            yield return new WaitForSeconds(interval);

            var path = GetNextScreenshotPath();
            if (path != null)
            {
                bool finished = false;
                bool ok = false;
                string error = string.Empty;
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
                }));
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
        var basePath = Core.savedSettings != null && !string.IsNullOrWhiteSpace(Core.savedSettings.AutoScreenshotPath)
            ? Core.savedSettings.AutoScreenshotPath
            : Path.Combine(Application.dataPath, "..", "docs", "journeys", "scratch");

        var fileName = "in-mod-" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".png";
        return Path.GetFullPath(Path.Combine(basePath, fileName));
    }
}
}
