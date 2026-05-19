using UnityEngine;
using UnityEngine.Rendering;

namespace WorldSphereMod.Voxel
{
    public static class SanityTestCube
    {
        static readonly Vector3 kPosition = new Vector3(210f, 5f, 280f);
        static Mesh? _mesh;
        static MaterialPropertyBlock? _block;
        static bool _logged;

        static readonly int _instanceColorId = Shader.PropertyToID("_InstanceColor");
        static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int _colorId = Shader.PropertyToID("_Color");

        public static void Draw()
        {
            Material? material = VoxelRender.GetResolvedMaterial();
            if (material == null) return;

            Mesh mesh = EnsureMesh();
            MaterialPropertyBlock block = EnsureBlock();
            Matrix4x4 matrix = Matrix4x4.TRS(kPosition, Quaternion.identity, Vector3.one);

            if (!_logged)
            {
                _logged = true;
                Debug.Log($"[WSM3D] SanityTestCube: drawing 10-unit cube at (210,5,280) using material {material.shader.name}");
            }

            Graphics.DrawMesh(
                mesh,
                matrix,
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

        static Mesh EnsureMesh()
        {
            if (_mesh != null) return _mesh;

            const float h = 5f;
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
