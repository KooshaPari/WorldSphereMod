using UnityEngine;
using UnityEngine.Rendering;
using WorldSphereMod.NewCamera;
namespace WorldSphereMod.Worldspace
{
    /// <summary>
    /// Phase 7 — kingdom-colored badge quad to the left of the nameplate rig.
    /// </summary>
    public sealed class FactionBadge : MonoBehaviour
    {
        internal Actor? Actor;
        MeshRenderer? _renderer;
        static Mesh? _quad;
        static Material? _mat;

        const float kSize = 0.18f;
        const float kLeftOffset = -0.55f;
        const float kHeadOffset = 0.42f;

        public static FactionBadge? Attach(Actor a, Transform rigRoot)
        {
            if (a == null || rigRoot == null) return null;

            var existing = rigRoot.GetComponentInChildren<FactionBadge>(true);
            if (existing != null) return existing;

            GameObject go = new GameObject("faction-badge");
            go.transform.SetParent(rigRoot, false);
            go.transform.localPosition = new Vector3(kLeftOffset, kHeadOffset, 0f);
            go.transform.localScale = Vector3.one * kSize;

            var badge = go.AddComponent<FactionBadge>();
            badge.Actor = a;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = GetQuad();
            badge._renderer = go.AddComponent<MeshRenderer>();
            badge._renderer.sharedMaterial = GetMaterial();
            badge._renderer.shadowCastingMode = ShadowCastingMode.Off;
            badge._renderer.receiveShadows = false;
            return badge;
        }

        public static void Detach(Actor a)
        {
            if (a == null || WorldUIRenderer.Instance == null) return;
            if (!WorldUIRenderer.Instance.Rigs.TryGetValue(a, out Transform rig) || rig == null) return;
            var badge = rig.GetComponentInChildren<FactionBadge>(true);
            if (badge != null) Object.Destroy(badge.gameObject);
        }

        public static void Reset()
        {
            if (_mat != null) { Object.Destroy(_mat); _mat = null; }
            if (_quad != null) { Object.Destroy(_quad); _quad = null; }
            FactionBadgeAtlasBuilder.Reset();
        }

        public void SetVisible(bool visible)
        {
            if (_renderer != null) _renderer.enabled = visible;
        }

        void LateUpdate()
        {
            if (Actor == null || _renderer == null) return;
            if (!FactionBadgeAtlasBuilder.TryGetBadgeColor(Actor, out Color c))
            {
                _renderer.enabled = false;
                return;
            }

            var cam = CameraManager.MainCamera;
            if (cam != null)
            {
                float d = Vector3.Distance(cam.transform.position, transform.position);
                float near = Core.savedSettings?.BadgeFadeNear ?? 10f;
                float far = Core.savedSettings?.BadgeFadeFar ?? 20f;
                float alpha = 1f - Mathf.InverseLerp(near, far, d);
                c.a *= Mathf.Clamp01(alpha);
                if (c.a < 0.02f)
                {
                    _renderer.enabled = false;
                    return;
                }

                Vector3 look = transform.position - cam.transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            }

            _renderer.enabled = true;
            _renderer.sharedMaterial.color = c;
        }

        static Mesh GetQuad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "WSM3D.FactionBadgeQuad" };
            _quad.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            });
            _quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            _quad.SetUVs(0, new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            });
            _quad.RecalculateNormals();
            _quad.RecalculateBounds();
            return _quad;
        }

        static Material GetMaterial()
        {
            if (_mat != null) return _mat;
            Shader? sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            _mat = new Material(sh!) { name = "WSM3D.FactionBadge" };
            return _mat;
        }
    }
}
