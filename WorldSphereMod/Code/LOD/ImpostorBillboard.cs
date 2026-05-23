using System.Collections.Generic;
using Object = UnityEngine.Object;
using UnityEngine;
using UnityEngine.Rendering;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.LOD
{
    public static class ImpostorBillboard
    {
        public const int MAX_ENTRIES = 512;
        public static int Capacity => MAX_ENTRIES;

        struct Entry
        {
            public Mesh Mesh;
            public ulong LastFrame;
        }

        static readonly Dictionary<int, Entry> _atlas = new Dictionary<int, Entry>();
        static ulong _frame;
        static long _hits;
        static long _misses;

        /// <summary>Cumulative cache-hit count since process start (or last Clear).</summary>
        public static long HitCount => System.Threading.Interlocked.Read(ref _hits);
        /// <summary>Cumulative cache-miss count since process start (or last Clear).</summary>
        public static long MissCount => System.Threading.Interlocked.Read(ref _misses);
        static Material? _material;
        static bool _materialAttempted;
        static bool _materialDebugLogged;
        static readonly int _colorId = Shader.PropertyToID("_Color");
        static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int _mainTexId = Shader.PropertyToID("_MainTex");
        static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int _srcBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int _dstBlendId = Shader.PropertyToID("_DstBlend");
        static readonly int _zWriteId = Shader.PropertyToID("_ZWrite");
        static readonly int _cutoffId = Shader.PropertyToID("_Cutoff");
        static readonly int _cullId = Shader.PropertyToID("_Cull");

        public static Material? GetMaterial()
        {
            if (_material != null)
            {
                // Keep the same material instance for all impostors.
                if (MeshInstanceBatcher.UseFallbackPath && _material.enableInstancing)
                {
                    _material.enableInstancing = false;
                }
                return _material;
            }
            if (_materialAttempted) return null;
            _materialAttempted = true;

            Shader? shader = null;
            if (WorldSphereMod.Core.Sphere.LoadedShaders.TryGetValue("Impostor", out var bundledImpostor) && bundledImpostor != null)
            {
                shader = bundledImpostor;
                Debug.Log("[WSM3D] Impostor material resolved via Core.Sphere.LoadedShaders cache.");
            }
            if (shader == null)
            {
                shader = Shader.Find("WSM3D/Impostor");
                if (shader != null) Debug.Log("[WSM3D] Impostor material resolved via Shader.Find('WSM3D/Impostor').");
            }
            if (shader == null)
            {
                string[] candidates =
                {
                    "Universal Render Pipeline/Unlit",
                    "Universal Render Pipeline/Lit",
                    "Universal Render Pipeline/Particles/Unlit",
                    "Standard",
                    "Sprites/Default",
                };

                foreach (var shaderName in candidates)
                {
                    shader = Shader.Find(shaderName);
                    if (shader != null)
                    {
                        Debug.Log($"[WSM3D] Impostor material fallback shader resolved via Shader.Find('{shaderName}').");
                        break;
                    }
                }
            }

            if (shader != null)
            {
                var mat = new Material(shader) { name = "WSM3D.Impostor" };
                mat.enableInstancing = true;
                if (!mat.enableInstancing)
                {
                    Object.Destroy(mat);
                }
                else
                {
                    ConfigureImpostorMaterial(mat, shader.name);
                    _material = mat;
                    LogImpostorMaterialPassDetails(_material, shader.name);
                    return _material;
                }
            }

            Debug.LogWarning("[WSM3D] ImpostorBillboard material resolution failed; impostor rendering will stay sprite-only.");
            return null;
        }

        static void LogImpostorMaterialPassDetails(Material material, string shaderName)
        {
            if (_materialDebugLogged) return;
            _materialDebugLogged = true;

            if (material == null)
            {
                Debug.LogWarning("[WSM3D] Impostor material diagnostics skipped: material is null.");
                return;
            }

            string shaderNameSafe = material.shader != null ? material.shader.name : "<null shader>";
            string keywords = material.shaderKeywords != null && material.shaderKeywords.Length > 0
                ? string.Join(", ", material.shaderKeywords)
                : "<none>";

            Debug.Log($"[WSM3D][MATERIAL] IMPOSTOR sourceCandidate='{shaderName}' resolvedShader='{shaderNameSafe}' passCount={material.passCount} renderQueue={material.renderQueue} renderType={material.GetTag("RenderType", false, "<none>")} queueOverride={material.GetTag("Queue", false, "<none>")}");
            Debug.Log($"[WSM3D][MATERIAL] IMPOSTOR shaderKeywords=[{keywords}]");

            for (int pass = 0; pass < material.passCount; pass++)
            {
                string passName = material.GetPassName(pass);
                Debug.Log($"[WSM3D][MATERIAL] IMPOSTOR pass[{pass}] name='{passName}'");
            }
        }

        public static Quaternion GetFacingRotation(Vector3 worldPos)
        {
            Camera? cam = Camera.main;
            if (cam == null) return Quaternion.identity;
            Vector3 toCam = cam.transform.position - worldPos;
            if (toCam.sqrMagnitude < 0.000001f) return Quaternion.identity;
            return Quaternion.LookRotation(toCam, Vector3.up);
        }

        static void ConfigureImpostorMaterial(Material material, string shaderName)
        {
            if (material == null) return;

            material.enableInstancing = true;
            material.renderQueue = (int)RenderQueue.Geometry + 1;
            material.SetInt(_cullId, (int)CullMode.Off);
            material.SetColor(_colorId, Color.white);
            material.SetColor(_baseColorId, Color.white);

            try
            {
                material.SetTexture(_mainTexId, Texture2D.whiteTexture);
            }
            catch { }
            try
            {
                material.SetTexture(_baseMapId, Texture2D.whiteTexture);
            }
            catch { }

            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            material.SetFloat(_cutoffId, 0f);
            material.SetInt(_srcBlendId, (int)BlendMode.One);
            material.SetInt(_dstBlendId, (int)BlendMode.Zero);
            material.SetInt(_zWriteId, 1);
            material.color = Color.white;
            // EMISSION belt+suspenders matching VoxelRender (cb6852d) +
            // FoliageMaterial (fbe2e71). Without it, Impostor billboards
            // render pitch-black when LodSelector drops actors to Impostor
            // tier at strategy zoom — user reported entire scene of black
            // 2.5D billboards. Self-emit at 1.5× white so impostors stay
            // visible regardless of scene lighting.
            try
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(1.5f, 1.5f, 1.5f, 1f));
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            catch { }

            if (string.Equals(shaderName, "Sprites/Default", System.StringComparison.OrdinalIgnoreCase))
            {
                material.SetOverrideTag("RenderType", "Opaque");
            }
        }

        public static Mesh? GetOrCreate(Sprite sprite)
        {
            if (sprite == null) return null;
            int key = sprite.GetInstanceID();
            if (_atlas.TryGetValue(key, out var entry) && entry.Mesh != null)
            {
                entry.LastFrame = _frame;
                _atlas[key] = entry;
                System.Threading.Interlocked.Increment(ref _hits);
                return entry.Mesh;
            }
            System.Threading.Interlocked.Increment(ref _misses);
            Mesh m = BuildQuad(sprite);
            m.RecalculateBounds();
            _atlas[key] = new Entry { Mesh = m, LastFrame = _frame };
            if (_atlas.Count > Capacity) Evict();
            return m;
        }

        public static int Count => _atlas.Count;

        public static void Tick()
        {
            _frame++;
        }

        public static void Clear()
        {
            foreach (var e in _atlas.Values) if (e.Mesh != null) Object.DestroyImmediate(e.Mesh);
            _atlas.Clear();
            System.Threading.Interlocked.Exchange(ref _hits, 0);
            System.Threading.Interlocked.Exchange(ref _misses, 0);
        }

        public static void Reset()
        {
            if (_material != null) Object.DestroyImmediate(_material);
            _material = null;
            _materialAttempted = false;
            _materialDebugLogged = false;
        }

        static Mesh BuildQuad(Sprite sprite)
        {
            float w = sprite.rect.width / sprite.pixelsPerUnit;
            float h = sprite.rect.height / sprite.pixelsPerUnit;
            float hx = w * 0.5f;
            var verts = new List<Vector3>
            {
                new Vector3(-hx, 0, 0), new Vector3(hx, 0, 0),
                new Vector3(hx, h, 0), new Vector3(-hx, h, 0),
            };
            Vector2[] uvs = sprite.uv;
            if (uvs == null || uvs.Length != 4)
            {
                uvs = new Vector2[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            }
            var mesh = new Mesh { name = "WSM3D.Impostor." + sprite.name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, new List<Vector2>(uvs));
            mesh.SetTriangles(new int[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Evict()
        {
            // Caller does not hold a lock. Remove least-recently-used entries until capped.
            if (_atlas.Count <= MAX_ENTRIES)
            {
                return;
            }

            while (_atlas.Count > MAX_ENTRIES)
            {
                int lruKey = -1;
                ulong lruFrame = ulong.MaxValue;
                foreach (var kv in _atlas)
                {
                    if (kv.Value.LastFrame < lruFrame)
                    {
                        lruFrame = kv.Value.LastFrame;
                        lruKey = kv.Key;
                    }
                }

                if (lruKey < 0)
                {
                    break;
                }

                var lruEntry = _atlas[lruKey];
                if (lruEntry.Mesh != null)
                {
                    Object.DestroyImmediate(lruEntry.Mesh);
                }

                _atlas.Remove(lruKey);
            }
        }
    }
}
