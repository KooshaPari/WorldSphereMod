using UnityEngine;

namespace WorldSphereMod.Worldspace
{
    public sealed class HealthBar : MonoBehaviour
    {
        internal Actor? Actor;
        MeshRenderer? _renderer;
        MaterialPropertyBlock? _block;
        static readonly int _hpProp = Shader.PropertyToID("_HpFraction");

        static Mesh? _sharedMesh;
        static Material? _sharedMat;

        static System.Reflection.MethodInfo? _ratioMethod;
        static bool _ratioMethodResolved;

        public static HealthBar? Attach(Actor a, Transform rigRoot)
        {
            if (a == null || rigRoot == null) return null;
            var existing = rigRoot.GetComponentInChildren<HealthBar>();
            if (existing != null) return existing;

            var go = new GameObject("hpbar");
            go.transform.SetParent(rigRoot, false);
            go.transform.localPosition = new Vector3(0, 0.35f, 0);
            go.transform.localScale = new Vector3(0.8f, 0.08f, 1f);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = GetSharedMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GetSharedMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var bar = go.AddComponent<HealthBar>();
            bar.Actor = a;
            bar._renderer = mr;
            bar._block = new MaterialPropertyBlock();
            return bar;
        }

        public static void Detach(Actor a)
        {
            if (a == null) return;
            var rigs = WorldUIRenderer.Instance?.Rigs;
            if (rigs == null) return;
            if (!rigs.TryGetValue(a, out var rig) || rig == null) return;
            var bar = rig.GetComponentInChildren<HealthBar>();
            if (bar != null) Object.Destroy(bar.gameObject);
        }

        public static void Reset()
        {
            if (_sharedMesh != null) { Object.Destroy(_sharedMesh); _sharedMesh = null; }
            if (_sharedMat != null) { Object.Destroy(_sharedMat); _sharedMat = null; }
        }

        static float GetHpRatio(Actor a)
        {
            if (!_ratioMethodResolved)
            {
                _ratioMethod = a.GetType().GetMethod("getHealthRatio");
                _ratioMethodResolved = true;
            }
            if (_ratioMethod == null) return 1f;
            try { return _ratioMethod.Invoke(a, null) is float r ? Mathf.Clamp01(r) : 1f; }
            catch { return 1f; }
        }

        void LateUpdate()
        {
            if (Actor == null || _renderer == null) return;
            float hp = GetHpRatio(Actor);

            Color c = Color.Lerp(Color.red, Color.green, hp);
            if (_renderer != null && _block != null)
            {
                _block.SetColor("_Color", c);
                _renderer.SetPropertyBlock(_block);
            }

            var cam = WorldSphereMod.NewCamera.CameraManager.MainCamera;
            if (cam != null)
            {
                Vector3 fwd = transform.position - cam.transform.position;
                fwd.y = 0; if (fwd.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            }

            var s = transform.localScale; s.x = 0.8f * hp; transform.localScale = s;
        }

        static Mesh GetSharedMesh()
        {
            if (_sharedMesh != null) return _sharedMesh;
            _sharedMesh = BuildQuadMesh();
            return _sharedMesh;
        }

        static Material? GetSharedMaterial()
        {
            if (_sharedMat != null) return _sharedMat;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) _sharedMat = new Material(shader) { name = "WSM3D.HpBar" };
            return _sharedMat;
        }

        static Mesh BuildQuadMesh()
        {
            var m = new Mesh { name = "WSM3D.HpBarQuad" };
            m.SetVertices(new System.Collections.Generic.List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0),
            });
            m.SetTriangles(new int[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.SetUVs(0, new System.Collections.Generic.List<Vector2> {
                new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1)
            });
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
