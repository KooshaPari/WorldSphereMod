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
            // All 6 faces wound CCW from OUTSIDE the cube (Unity left-handed). Previous
            // listing had 3 faces CW + 3 CCW, so RecalculateNormals derived inward
            // normals for half the cube — looked black under the sun light because
            // light hit the back of those normals from the viewer's side. Set normals
            // explicitly per face so winding-vs-normal can't drift.
            int[] triangles =
            {
                // back   (z=-h): outside-normal -Z
                0, 1, 2,  0, 2, 3,
                // front  (z=+h): outside-normal +Z
                4, 6, 5,  4, 7, 6,
                // left   (x=-h): outside-normal -X
                8, 9, 10, 8, 10, 11,
                // right  (x=+h): outside-normal +X
                12, 14, 13, 12, 15, 14,
                // top    (y=+h): outside-normal +Y
                16, 17, 18, 16, 18, 19,
                // bottom (y=-h): outside-normal -Y
                20, 22, 21, 20, 23, 22,
            };
            Vector3[] normals =
            {
                new Vector3( 0,  0, -1), new Vector3( 0,  0, -1), new Vector3( 0,  0, -1), new Vector3( 0,  0, -1),
                new Vector3( 0,  0,  1), new Vector3( 0,  0,  1), new Vector3( 0,  0,  1), new Vector3( 0,  0,  1),
                new Vector3(-1,  0,  0), new Vector3(-1,  0,  0), new Vector3(-1,  0,  0), new Vector3(-1,  0,  0),
                new Vector3( 1,  0,  0), new Vector3( 1,  0,  0), new Vector3( 1,  0,  0), new Vector3( 1,  0,  0),
                new Vector3( 0,  1,  0), new Vector3( 0,  1,  0), new Vector3( 0,  1,  0), new Vector3( 0,  1,  0),
                new Vector3( 0, -1,  0), new Vector3( 0, -1,  0), new Vector3( 0, -1,  0), new Vector3( 0, -1,  0),
            };
            Color[] colors = new Color[vertices.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = Color.magenta;
            }

            _mesh = new Mesh { name = "WSM3D.SanityTestCube" };
            _mesh.vertices = vertices;
            _mesh.triangles = triangles;
            _mesh.normals = normals;
            _mesh.colors = colors;
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
