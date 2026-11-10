using System.Collections;
using System.Collections.Generic;

// Test doubles only. Tests drive the actual linked production iterators one yield
// at a time; these are not evidence of Unity scheduling or in-player behavior.
namespace UnityEngine
{
    public class Object { public static void Destroy(Object value) { } }
    public class Coroutine { public IEnumerator Routine; }
    public class MonoBehaviour : Object
    {
        public bool isActiveAndEnabled = true;
        public readonly List<Coroutine> Started = new List<Coroutine>();
        public Coroutine StartCoroutine(IEnumerator routine)
        {
            var result = new Coroutine { Routine = routine };
            Started.Add(result);
            return result;
        }
        public void StopAllCoroutines() { Started.Clear(); }
    }
    public class WaitForSeconds { public float Seconds; public WaitForSeconds(float value) { Seconds = value; } }
    public class WaitForEndOfFrame { }
    public static class Debug { public static void Log(object value) { } public static void LogWarning(object value) { } }
    public static class Application { public static string dataPath = "unused"; }
    public static class ScreenCapture
    {
        public static readonly List<string> Captured = new List<string>();
        public static void CaptureScreenshot(string path) { Captured.Add(path); }
    }
    public class Camera : Object
    {
        public static Camera main;
        public static Camera[] allCameras = new Camera[0];
        public bool enabled, isActiveAndEnabled;
        public string name;
        public int cullingMask;
        public float depth;
        public RenderTexture targetTexture;
        public void Render() { }
    }
    public class RenderTexture : Object
    {
        public static RenderTexture active;
        public RenderTexture(int width, int height, int depth) { }
        public void Release() { }
    }
    public class Texture2D : Object
    {
        public Texture2D(int width, int height, TextureFormat format, bool mipmap) { }
        public void ReadPixels(Rect rect, int x, int y) { }
        public void Apply() { }
    }
    public enum TextureFormat { RGB24 }
    public struct Rect { public Rect(int x, int y, int width, int height) { } }
    public static class Mathf { public static int Max(int x, int y) => System.Math.Max(x, y); }
    public static class Screen { public static int width = 1, height = 1; }
    public static class LayerMask { public static int NameToLayer(string name) => 5; }
}
namespace WorldSphereMod
{
    public static class Core { public static SavedSettings savedSettings; public static bool IsWorld3D; }
}
namespace NewCamera
{
    public static class CameraManager { public static UnityEngine.Camera MainCamera; }
}
