using System;
using UnityEngine;

namespace WorldSphereMod.Capture
{
    /// <summary>
    /// Samples the main camera frame (x,y,zoom) and records a <c>camera</c> event when it has moved
    /// meaningfully since the last recorded frame. Continuous mouse-drag panning would otherwise
    /// emit thousands of micro-events; debouncing by distance + a minimum interval keeps the flow
    /// replay-stable and the JSONL diffable (settle points only, not every intermediate pixel).
    ///
    /// Driven once per Unity frame from the existing bridge per-frame tick (BridgeSurvival.Run),
    /// so it inherits main-thread safety with zero new update loops.
    /// </summary>
    public static class CaptureCameraSampler
    {
        const float MinMoveSqr = 1.0f;    // world units² — ignore sub-tile jitter
        const float MinZoomDelta = 0.05f; // orthographic size delta
        const long MinIntervalMs = 150;   // debounce: at most ~6 camera events/sec

        static float _lastX, _lastY, _lastZoom = float.NaN;
        static long _lastMs;
        static bool _hasLast;

        /// <summary>Called each frame from the bridge tick. No-op unless capture is enabled.</summary>
        public static void Tick()
        {
            if (!CaptureRecorder.Enabled) return;
            RecordCameraFrame(force: false);
        }

        /// <summary>
        /// Record the current camera frame as a <c>camera</c> event if it moved enough (or
        /// <paramref name="force"/>, used by the zoom hook to capture the settle point immediately).
        /// </summary>
        public static void RecordCameraFrame(bool force)
        {
            try
            {
                float x, y, zoom;

                if (Core.IsWorld3D)
                {
                    // In 3D the 2D MapBox.camera is DISABLED; read the live WSM rig instead, the same
                    // source the camera-action / screenshot code uses. Position is the world anchor,
                    // Height is the surface-distance "zoom".
                    var mgr = NewCamera.CameraManager.Manager;
                    if (mgr == null) return;
                    Vector2 pos = NewCamera.CameraManager.Position;
                    x = pos.x; y = pos.y;
                    zoom = NewCamera.CameraManager.Height;
                }
                else
                {
                    Camera cam = SafeCamera();
                    if (cam == null) return;
                    x = cam.transform.position.x;
                    y = cam.transform.position.y;
                    zoom = cam.orthographic ? cam.orthographicSize : cam.fieldOfView;
                }

                long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

                if (!force && _hasLast)
                {
                    float dx = x - _lastX, dy = y - _lastY;
                    bool movedPos = (dx * dx + dy * dy) >= MinMoveSqr;
                    bool movedZoom = float.IsNaN(_lastZoom) || Mathf.Abs(zoom - _lastZoom) >= MinZoomDelta;
                    if (!movedPos && !movedZoom) return;
                    if ((now - _lastMs) < MinIntervalMs) return; // debounce drag spam
                }

                _lastX = x; _lastY = y; _lastZoom = zoom; _lastMs = now; _hasLast = true;

                CaptureRecorder.Record(
                    CaptureRecorder.Emit(CaptureEventTypes.Camera)
                        .Arg("x", Mathf.Round(x * 100f) / 100f)
                        .Arg("y", Mathf.Round(y * 100f) / 100f)
                        .Arg("zoom", Mathf.Round(zoom * 100f) / 100f));
            }
            catch (Exception ex) { Debug.LogWarning("[WSM3D][Capture] camera sample: " + ex.Message); }
        }

        static Camera SafeCamera()
        {
            try { Camera c = MapBox.instance != null ? MapBox.instance.camera : null; if (c != null) return c; } catch { }
            return Camera.main;
        }
    }
}
