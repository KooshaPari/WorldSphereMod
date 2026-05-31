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

        // Offscreen camera->RenderTexture capture path. Unlike CaptureCoroutine (which
        // uses ScreenCapture.CaptureScreenshot and grabs the full framebuffer, INCLUDING
        // WorldBox's debug-console text overlay / UI / SleekRender post layers), this
        // renders ONLY the 3D scene camera into a RenderTexture, so the saved PNG is the
        // clean 3D view with no overlay text. Used for vision-verification.
        public static IEnumerator CaptureCameraCoroutine(string outputPath, Action<string, bool, string> completed)
        {
            // Wait for the scene to finish rendering this frame so transforms/meshes are settled.
            yield return new WaitForEndOfFrame();

            string path = string.IsNullOrWhiteSpace(outputPath) ? BuildDefaultPath() : Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Camera camera = ResolveSceneCamera();
            if (camera == null)
            {
                completed?.Invoke(path, false, "scene_camera_not_found");
                yield break;
            }

            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);

            RenderTexture renderTexture = null;
            Texture2D texture = null;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                renderTexture = new RenderTexture(width, height, 24);
                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture.active = renderTexture;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();

                // EncodeToPNG lives in UnityEngine.ImageConversion (ImageConversionModule),
                // which is not in our referenced UnityEngine assembly, so resolve it by
                // reflection (same approach the screen-capture path uses for ScreenCapture).
                byte[] png = EncodeToPng(texture);
                if (png == null)
                {
                    completed?.Invoke(path, false, "image_conversion_unavailable");
                }
                else
                {
                    File.WriteAllBytes(path, png);
                    completed?.Invoke(path, true, string.Empty);
                }
            }
            catch (Exception ex)
            {
                completed?.Invoke(path, false, ex.Message);
            }
            finally
            {
                // Always restore camera.targetTexture or the game's on-screen view goes black.
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.Destroy(renderTexture);
                }
            }
        }

        // Resolve the active 3D scene camera. Prefer the mod's own static reference
        // (CameraManager.MainCamera, the "WorldSphere Camera" created in 3DCamera.cs)
        // when it is enabled in 3D mode, then Camera.main, then a heuristic over all
        // cameras (highest depth, enabled, not a UI/overlay camera).
        static Camera ResolveSceneCamera()
        {
            Camera modCamera = NewCamera.CameraManager.MainCamera;
            if (Core.IsWorld3D && modCamera != null && modCamera.enabled && modCamera.isActiveAndEnabled)
            {
                return modCamera;
            }

            if (Camera.main != null && Camera.main.enabled)
            {
                return Camera.main;
            }

            Camera best = null;
            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera candidate = cameras[i];
                if (candidate == null || !candidate.enabled || IsUiCamera(candidate))
                {
                    continue;
                }
                if (best == null || candidate.depth > best.depth)
                {
                    best = candidate;
                }
            }
            return best ?? modCamera;
        }

        static byte[] EncodeToPng(Texture2D texture)
        {
            Type conversionType = Type.GetType("UnityEngine.ImageConversion, UnityEngine")
                                  ?? Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
            if (conversionType == null)
            {
                return null;
            }
            var method = conversionType.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });
            if (method == null)
            {
                return null;
            }
            return method.Invoke(null, new object[] { texture }) as byte[];
        }

        static bool IsUiCamera(Camera camera)
        {
            string name = camera.name ?? string.Empty;
            if (name.IndexOf("UI", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Overlay", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Canvas", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // A camera whose culling mask covers ONLY the UI layer is a UI/overlay camera.
            int uiLayerMask = 1 << LayerMask.NameToLayer("UI");
            if ((camera.cullingMask & ~uiLayerMask) == 0)
            {
                return true;
            }
            return false;
        }
    }
}
