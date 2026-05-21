using UnityEngine;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.LOD
{
    public static class FrustumCuller
    {
        static readonly Plane[] _planes = new Plane[6];
        static int _frameCached = -1;
        static bool _hasCachedPlanes;
        static Vector3 _lastCameraPosition;
        static Quaternion _lastCameraRotation;
        static float _lastCameraNear;
        static float _lastCameraFar;
        static float _lastCameraFov;
        static float _lastCameraAspect;

        public static void UpdatePlanes()
        {
            Camera cam = CameraManager.MainCamera;
            if (cam == null) return;

            if (_frameCached == Time.frameCount &&
                _hasCachedPlanes &&
                _lastCameraPosition == cam.transform.position &&
                _lastCameraRotation == cam.transform.rotation &&
                _lastCameraNear == cam.nearClipPlane &&
                _lastCameraFar == cam.farClipPlane &&
                _lastCameraFov == cam.fieldOfView &&
                _lastCameraAspect == cam.aspect)
            {
                return;
            }

            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            _frameCached = Time.frameCount;
            _hasCachedPlanes = true;
            _lastCameraPosition = cam.transform.position;
            _lastCameraRotation = cam.transform.rotation;
            _lastCameraNear = cam.nearClipPlane;
            _lastCameraFar = cam.farClipPlane;
            _lastCameraFov = cam.fieldOfView;
            _lastCameraAspect = cam.aspect;
        }

        public static bool IsVisible(Vector3 worldPos, float radius)
        {
            if (!_hasCachedPlanes) return true;
            float d = radius * 2f;
            UnityEngine.Bounds bounds = new UnityEngine.Bounds(worldPos, new Vector3(d, d, d));
            return GeometryUtility.TestPlanesAABB(_planes, bounds);
        }
    }
}
