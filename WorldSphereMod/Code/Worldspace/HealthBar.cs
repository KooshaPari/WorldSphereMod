using UnityEngine;

namespace WorldSphereMod.Worldspace
{
    public sealed class HealthBar : MonoBehaviour
    {
        internal Actor? Actor;
        MeshRenderer? _renderer;
        MaterialPropertyBlock? _block;
        Material? _material;
        static readonly int _hpProp = Shader.PropertyToID("_HpFraction");

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
            mf.sharedMesh = BuildQuadMesh();
            var mr = go.AddComponent<MeshRenderer>();
            var shader = Shader.Find("Sprites/Default");
            var mat = shader != null ? new Material(shader) { name = "WSM3D.HpBar" } : null;
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var bar = go.AddComponent<HealthBar>();
            bar.Actor = a;
            bar._renderer = mr;
            bar._material = mat;
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

        void LateUpdate()
        {
            if (Actor == null || _renderer == null) return;
            // HP fraction via reflection: Actor.health and .max_health are common WorldBox fields.
            float hp = 1f;
            try
            {
                // Actor has no health/max_health fields; the canonical accessor is
                // getHealthRatio() which returns the [0,1] fraction directly.
                var t = Actor.GetType();
                var ratioM = t.GetMethod("getHealthRatio");
                if (ratioM != null)
                {
                    object? boxed = ratioM.Invoke(Actor, null);
                    if (boxed is float r) hp = Mathf.Clamp01(r);
                }
            }
            catch { /* leave hp=1 on reflection failure */ }

            // Drive bar via tint color (simple 2-tone) — full bar = green, low bar = red.
            Color c = Color.Lerp(Color.red, Color.green, hp);
            if (_renderer != null && _block != null)
            {
                _block.SetColor("_Color", c);
                _renderer.SetPropertyBlock(_block);
            }

            // Face camera horizontally (yaw only).
            var cam = WorldSphereMod.NewCamera.CameraManager.MainCamera;
            if (cam != null)
            {
                Vector3 fwd = transform.position - cam.transform.position;
                fwd.y = 0; if (fwd.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            }

            // Scale x by hp to show the fill.
            var s = transform.localScale; s.x = 0.8f * hp; transform.localScale = s;
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
