using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using WorldSphereMod.NewCamera;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.Worldspace
{
    public sealed class HealthBar : MonoBehaviour
    {
        internal Actor? Actor;
        bool _use3dMode;

        const float kFullLength = 1f;
        const float kThickness = 0.1f;
        const float kHeadOffset = 0.2f;

        static Mesh? _sharedQuadMesh;
        static Mesh? _sharedBoxMesh;
        static Material? _sharedLegacyMat;
        static readonly Dictionary<Actor, SpriteRenderer> _suppressedHealthBars = new();

        static MethodInfo? _ratioMethod;
        static bool _ratioMethodResolved;
        static readonly Dictionary<System.Type, MemberInfo?> _healthBarMemberCache = new();

        public static HealthBar? Attach(Actor a, Transform rigRoot)
        {
            if (a == null || rigRoot == null) return null;
            var existing = rigRoot.GetComponentInChildren<HealthBar>();
            if (existing != null) return existing;

            GameObject go = new GameObject("hpbar");
            go.transform.SetParent(rigRoot, false);
            go.transform.localPosition = new Vector3(0, 0.35f, 0);
            go.transform.localScale = new Vector3(0.8f, 0.08f, 1f);

            var bar = go.AddComponent<HealthBar>();
            bar.Actor = a;
            bar._use3dMode = Core.savedSettings.WorldspaceHealth3D;
            if (bar._use3dMode)
            {
                bar.SuppressUpstreamBillboard();
            }
            else
            {
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = GetSharedLegacyMesh();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = GetSharedLegacyMaterial();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            return bar;
        }

        public static void Detach(Actor a)
        {
            if (a == null) return;
            var rigs = WorldUIRenderer.Instance?.Rigs;
            if (rigs == null) return;
            if (!rigs.TryGetValue(a, out var rig) || rig == null) return;
            var bar = rig.GetComponentInChildren<HealthBar>();
            bar?.RestoreUpstreamBillboard();
            if (bar != null) Object.Destroy(bar.gameObject);
        }

        public static void Reset()
        {
            foreach (var kv in _suppressedHealthBars)
            {
                if (kv.Key == null || kv.Value == null) continue;
                kv.Value.enabled = true;
            }
            _suppressedHealthBars.Clear();

            if (_sharedQuadMesh != null) { Object.Destroy(_sharedQuadMesh); _sharedQuadMesh = null; }
            if (_sharedBoxMesh != null) { Object.Destroy(_sharedBoxMesh); _sharedBoxMesh = null; }
            if (_sharedLegacyMat != null) { Object.Destroy(_sharedLegacyMat); _sharedLegacyMat = null; }
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
            if (Actor == null) return;
            float hp = GetHpRatio(Actor);
            hp = Mathf.Clamp01(hp);

            if (_use3dMode)
            {
                Submit3DBar(hp);
                return;
            }

            MeshRenderer? renderer = GetComponent<MeshRenderer>();
            if (renderer == null) return;

            Color c = Color.Lerp(Color.red, Color.green, hp);
            renderer.sharedMaterial.color = c;

            var cam = CameraManager.MainCamera;
            if (cam != null)
            {
                Vector3 fwd = transform.position - cam.transform.position;
                fwd.y = 0; if (fwd.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            }

            Vector3 s = transform.localScale;
            s.x = 0.8f * hp;
            transform.localScale = s;
        }

        void OnDestroy()
        {
            RestoreUpstreamBillboard();
        }

        void Submit3DBar(float hp)
        {
            Mesh? mesh = GetSharedMesh();
            Material? mat = VoxelRender.GetResolvedMaterial();
            Camera? cam = CameraManager.MainCamera;
            if (mesh == null || mat == null || cam == null) return;

            Vector3 barBasePos = transform.position + Vector3.up * kHeadOffset;
            Vector3 look = barBasePos - cam.transform.position;
            look.y = 0f;
            if (look.sqrMagnitude < 0.0001f)
            {
                look = Vector3.forward;
            }
            Quaternion rot = Quaternion.LookRotation(look, Vector3.up);

            MeshInstanceBatcher.Submit(mesh, mat, Matrix4x4.TRS(barBasePos, rot, new Vector3(kFullLength, kThickness, kThickness)), Color.red);

            float hpWidth = Mathf.Clamp01(hp);
            if (hpWidth > 0.001f)
            {
                Vector3 fgOffset = rot * new Vector3(kFullLength * (hpWidth * 0.5f - 0.5f), 0f, 0f);
                MeshInstanceBatcher.Submit(mesh, mat, Matrix4x4.TRS(barBasePos + fgOffset, rot, new Vector3(kFullLength * hpWidth, kThickness, kThickness)), Color.green);
            }
        }

        void SuppressUpstreamBillboard()
        {
            if (Actor == null) return;
            if (_suppressedHealthBars.ContainsKey(Actor)) return;
            SpriteRenderer? sr = ResolveUpstreamHealthBarRenderer(Actor);
            if (sr == null) return;
            sr.enabled = false;
            _suppressedHealthBars[Actor] = sr;
        }

        void RestoreUpstreamBillboard()
        {
            if (Actor == null) return;
            if (_suppressedHealthBars.TryGetValue(Actor, out SpriteRenderer sr))
            {
                if (sr != null)
                {
                    sr.enabled = true;
                }
                _suppressedHealthBars.Remove(Actor);
            }
        }

        static Mesh GetSharedMesh()
        {
            if (_sharedBoxMesh != null) return _sharedBoxMesh;
            _sharedBoxMesh = BuildBoxMesh();
            return _sharedBoxMesh;
        }

        static Material? GetSharedLegacyMaterial()
        {
            if (_sharedLegacyMat != null) return _sharedLegacyMat;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) _sharedLegacyMat = new Material(shader) { name = "WSM3D.HpBar" };
            return _sharedLegacyMat;
        }

        static Mesh GetSharedLegacyMesh()
        {
            if (_sharedQuadMesh != null) return _sharedQuadMesh;
            _sharedQuadMesh = BuildQuadMesh();
            return _sharedQuadMesh;
        }

        static SpriteRenderer? ResolveUpstreamHealthBarRenderer(Actor actor)
        {
            if (actor == null) return null;
            System.Type actorType = actor.GetType();
            if (_healthBarMemberCache.TryGetValue(actorType, out MemberInfo? member))
            {
                return ResolveFromMember(actor, member);
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] exactNames = { "health_bar", "healthBar", "healthbar", "_health_bar", "_healthBar", "_healthbar", "HealthBar", "Healthbar", "health_bar_sprite", "health_bar_renderer", "healthBarRenderer", "HealthBarRenderer" };

            foreach (string name in exactNames)
            {
                var field = actorType.GetField(name, flags);
                if (field != null)
                {
                    var sr = ResolveFromMember(actor, field);
                    if (sr != null)
                    {
                        _healthBarMemberCache[actorType] = field;
                        return sr;
                    }
                }

                var property = actorType.GetProperty(name, flags);
                if (property != null)
                {
                    var sr = ResolveFromMember(actor, property);
                    if (sr != null)
                    {
                        _healthBarMemberCache[actorType] = property;
                        return sr;
                    }
                }
            }

            foreach (var field in actorType.GetFields(flags))
            {
                string n = field.Name.ToLowerInvariant();
                if (!n.Contains("health") || !n.Contains("bar")) continue;
                var sr = ResolveFromMember(actor, field);
                if (sr != null)
                {
                    _healthBarMemberCache[actorType] = field;
                    return sr;
                }
            }

            foreach (var property in actorType.GetProperties(flags))
            {
                string n = property.Name.ToLowerInvariant();
                if (!n.Contains("health") || !n.Contains("bar")) continue;
                var sr = ResolveFromMember(actor, property);
                if (sr != null)
                {
                    _healthBarMemberCache[actorType] = property;
                    return sr;
                }
            }

            _healthBarMemberCache[actorType] = null;
            return null;
        }

        static SpriteRenderer? ResolveFromMember(Actor actor, MemberInfo? member)
        {
            if (member == null || actor == null) return null;
            object? raw = null;
            try
            {
                if (member is FieldInfo fi) raw = fi.GetValue(actor);
                else if (member is PropertyInfo pi) raw = pi.GetValue(actor);
            }
            catch
            {
                return null;
            }

            if (raw == null) return null;
            if (raw is SpriteRenderer sr) return sr;
            if (raw is GameObject go) return go.GetComponentInChildren<SpriteRenderer>(true);
            if (raw is Component c) return c.GetComponentInChildren<SpriteRenderer>(true);
            return null;
        }

        static Mesh BuildQuadMesh()
        {
            var m = new Mesh { name = "WSM3D.HpBarQuad" };
            m.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0),
            });
            m.SetTriangles(new int[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.SetUVs(0, new List<Vector2> {
                new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1)
            });
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh BuildBoxMesh()
        {
            var m = new Mesh { name = "WSM3D.HpBarBox" };
            Vector3[] v =
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f,  0.5f,  0.5f),
                new Vector3(-0.5f,  0.5f,  0.5f),
            };
            int[] t =
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                2, 3, 7, 2, 7, 6,
                1, 2, 6, 1, 6, 5,
                3, 0, 4, 3, 4, 7
            };
            m.vertices = v;
            m.triangles = t;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
