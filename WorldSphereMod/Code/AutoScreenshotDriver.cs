using System.Collections;
using System.IO;
using UnityEngine;

namespace WorldSphereMod
{

public sealed class AutoScreenshotDriver : MonoBehaviour
{
    private const int MaxScreenshots = 20;
    private int _screenshotCount;
    private const float DefaultIntervalSeconds = 30f;

    private void Start()
    {
        Debug.Log("[WSM3D] AutoScreenshotDriver.Start savedSettings=" + (Core.savedSettings != null) +
                  " enabled=" + (Core.savedSettings != null ? Core.savedSettings.AutoScreenshotEnabled : false));
        // Always-on diagnostic capture; ignore the SavedSettings gate while we're
        // debugging Phase 1 visibility. Cap of 20 captures prevents disk fill.
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
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var captureType = System.Type.GetType("UnityEngine.ScreenCapture, UnityEngine")
                                   ?? System.Type.GetType("UnityEngine.ScreenCapture, UnityEngine.CoreModule");
                if (captureType == null) { Debug.LogWarning("[WSM3D] AutoScreenshot ScreenCapture Type not found"); yield break; }
                var m = captureType.GetMethod("CaptureScreenshot", new[] { typeof(string) });
                if (m == null) { Debug.LogWarning("[WSM3D] AutoScreenshot CaptureScreenshot method not found"); yield break; }
                m.Invoke(null, new object[] { path });
                _screenshotCount++;
                Debug.Log("[WSM3D] AutoScreenshot saved " + path);
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
            : Application.persistentDataPath;

        var fileName = $"in-mod-{Mathf.RoundToInt(Time.time * 1000f)}.png";
        return Path.Combine(basePath, fileName);
    }
}
}
