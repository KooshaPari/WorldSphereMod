using UnityEngine;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.LOD
{
    public static class FrustumCuller
    {
        static Plane[]? _planes;
        static int _frameCached = -1;

        public static void UpdatePlanes()
        {
            Camera cam = CameraManager.MainCamera;
            if (cam == null) return;
            if (_frameCached == Time.frameCount) return;
            _planes = GeometryUtility.CalculateFrustumPlanes(cam);
            _frameCached = Time.frameCount;
        }

        public static bool IsVisible(Vector3 worldPos, float radius)
        {
            if (_planes == null) return true;
            float d = radius * 2f;
            UnityEngine.Bounds bounds = new UnityEngine.Bounds(worldPos, new Vector3(d, d, d));
            return GeometryUtility.TestPlanesAABB(_planes, bounds);
        }
    }
}
