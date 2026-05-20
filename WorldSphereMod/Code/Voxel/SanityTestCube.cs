using UnityEngine;
using UnityEngine.Rendering;

namespace WorldSphereMod.Voxel
{
    public static class SanityTestCube
    {
        public static Vector3 LastActorPos;
        static bool _hasLastActorPos;
        static Vector3 _lastLoggedBasePos = new Vector3(float.NaN, float.NaN, float.NaN);
        static Mesh? _mesh;
        static MaterialPropertyBlock? _block;

        static readonly int _instanceColorId = Shader.PropertyToID("_InstanceColor");
        static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int _colorId = Shader.PropertyToID("_Color");
        static readonly Vector3 _heightOffset = Vector3.up * 50f;
        static readonly float _halfCubeSize = 10f;

        public static void Draw()
        {
            Material? material = VoxelRender.GetResolvedMaterial();
            if (material == null) return;
            if (!_hasLastActorPos) return;

            Mesh mesh = EnsureMesh();
            MaterialPropertyBlock block = EnsureBlock();
            Vector3 basePos = LastActorPos;
            Vector3 topPos = LastActorPos + _heightOffset;
            Matrix4x4 baseMatrix = Matrix4x4.TRS(basePos, Quaternion.identity, Vector3.one * _halfCubeSize);
            Matrix4x4 topMatrix = Matrix4x4.TRS(topPos, Quaternion.identity, Vector3.one * _halfCubeSize);

            if (!basePos.Equals(_lastLoggedBasePos))
            {
                Debug.Log($"[WSM3D] SanityTestCube: drawing 20-unit cube at base={basePos} and upOffset={topPos} using material {material.shader.name}");
                _lastLoggedBasePos = basePos;
            }

            Graphics.DrawMesh(
                mesh,
                baseMatrix,
                material,
                0,
                null,
                0,
                block,
                ShadowCastingMode.On,
                true,
                null,
                LightProbeUsage.Off);

            Graphics.DrawMesh(
                mesh,
                topMatrix,
                material,
                0,
                null,
                0,
                block,
                ShadowCastingMode.On,
                true,
                null,
                LightProbeUsage.Off);
        }

        public static void CaptureFirstActorPos(Vector3 actorWorldPos)
        {
            if (_hasLastActorPos) return;
            LastActorPos = actorWorldPos;
            _hasLastActorPos = true;
        }

        public static void Reset()
        {
            LastActorPos = Vector3.zero;
            _hasLastActorPos = false;
            _lastLoggedBasePos = new Vector3(float.NaN, float.NaN, float.NaN);
        }

        static Mesh EnsureMesh()
        {
            if (_mesh != null) return _mesh;

            // Unit cube (1x1x1 local). Combined with the TRS scale of _halfCubeSize
            // (10) at the draw call, world size becomes 10x10x10. Previous h=10 here
            // produced a 200-unit cube that filled the camera frustum from any sane
            // strategy-view altitude — looked like the world had turned black.
            const float h = 0.5f;
            Vector3[] vertices =
            {
                new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(-h, h, -h),
                new Vector3(-h, -h, h), new Vector3(h, -h, h), new Vector3(h, h, h), new Vector3(-h, h, h),
                new Vector3(-h, -h, -h), new Vector3(-h, h, -h), new Vector3(-h, h, h), new Vector3(-h, -h, h),
                new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(h, h, h), new Vector3(h, -h, h),
                new Vector3(-h, h, -h), new Vector3(h, h, -h), new Vector3(h, h, h), new Vector3(-h, h, h),
                new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, -h, h), new Vector3(-h, -h, h),
            };
            int[] triangles =
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                8, 10, 9, 8, 11, 10,
                12, 13, 14, 12, 14, 15,
                16, 18, 17, 16, 19, 18,
                20, 21, 22, 20, 22, 23,
            };
            Color[] colors = new Color[vertices.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = Color.white;
            }

            _mesh = new Mesh { name = "WSM3D.SanityTestCube" };
            _mesh.vertices = vertices;
            _mesh.triangles = triangles;
            _mesh.colors = colors;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            return _mesh;
        }

        static MaterialPropertyBlock EnsureBlock()
        {
            if (_block != null) return _block;

            _block = new MaterialPropertyBlock();
            _block.SetColor(_instanceColorId, Color.white);
            _block.SetColor(_baseColorId, Color.white);
            _block.SetColor(_colorId, Color.white);
            return _block;
        }
    }
}
