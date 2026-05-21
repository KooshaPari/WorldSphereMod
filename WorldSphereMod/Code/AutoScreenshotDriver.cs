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
        if (Core.savedSettings == null || !Core.savedSettings.AutoScreenshotEnabled)
        {
            Destroy(this);
            return;
        }

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
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var captureType = System.Type.GetType("UnityEngine.ScreenCapture, UnityEngine")
                                   ?? System.Type.GetType("UnityEngine.ScreenCapture, UnityEngine.CoreModule");
                if (captureType != null) captureType.GetMethod("CaptureScreenshot", new[] { typeof(string) })?.Invoke(null, new object[] { path });
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
            : Application.persistentDataPath;

        var fileName = $"in-mod-{Mathf.RoundToInt(Time.time * 1000f)}.png";
        return Path.Combine(basePath, fileName);
    }
}
}
