using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace WorldSphereMod
{
    public static class ScreenshotCapture
    {
        public static string BuildDefaultPath()
        {
            string root = Path.Combine(Application.dataPath, "..", "docs", "journeys", "scratch");
            string fileName = "in-mod-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".png";
            return Path.GetFullPath(Path.Combine(root, fileName));
        }

        public static IEnumerator CaptureCoroutine(string outputPath, Action<string, bool, string> completed)
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            string path = string.IsNullOrWhiteSpace(outputPath) ? BuildDefaultPath() : Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                Type captureType = Type.GetType("UnityEngine.ScreenCapture, UnityEngine")
                                   ?? Type.GetType("UnityEngine.ScreenCapture, UnityEngine.CoreModule");
                if (captureType == null)
                {
                    completed?.Invoke(path, false, "screen_capture_type_not_found");
                    yield break;
                }

                var method = captureType.GetMethod("CaptureScreenshot", new[] { typeof(string) });
                if (method == null)
                {
                    completed?.Invoke(path, false, "screen_capture_method_not_found");
                    yield break;
                }

                method.Invoke(null, new object[] { path });
                completed?.Invoke(path, true, string.Empty);
            }
            catch (Exception ex)
            {
                completed?.Invoke(path, false, ex.Message);
            }
        }
    }
}
