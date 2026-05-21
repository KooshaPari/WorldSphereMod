using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace WorldSphereMod
{
    public class AutoScreenshotDriver : MonoBehaviour
    {
        static MethodInfo _capture;

        IEnumerator Start()
        {
            yield return new WaitForSeconds(15f);
            int captures = 0;
            while (captures < 10)
            {
                yield return new WaitForSeconds(20f);
                string path = "C:/Users/koosh/Dev/WorldSphereMod/docs/journeys/scratch/in-mod-" +
                              DateTime.Now.ToString("HHmmss") + ".png";
                if (!Capture(path))
                {
                    Debug.LogWarning("[WSM3D] AutoScreenshot ScreenCapture API not found via reflection");
                    yield break;
                }
                Debug.Log("[WSM3D] AutoScreenshot saved " + path);
                captures++;
            }
        }

        static bool Capture(string path)
        {
            if (_capture == null)
            {
                Type t = Type.GetType("UnityEngine.ScreenCapture, UnityEngine")
                      ?? Type.GetType("UnityEngine.ScreenCapture, UnityEngine.CoreModule")
                      ?? Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule");
                if (t == null) return false;
                _capture = t.GetMethod("CaptureScreenshot", new[] { typeof(string) });
                if (_capture == null) return false;
            }
            _capture.Invoke(null, new object[] { path });
            return true;
        }
    }
}
