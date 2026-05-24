using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Worldspace
{
    /// <summary>
    /// Phase 7 Step 3. Static manager that draws a flat ring (annulus) under each
    /// selected <see cref="Actor"/>. Mesh is shared between actors with matching
    /// quantised outer radius (cf. <see cref="VoxelMeshCache"/>'s per-key sharing)
    /// so 500 selected actors at the same size cost a single mesh.
    ///
    /// Step 4 wires <see cref="Show"/>/<see cref="Hide"/> from selection Harmony
    /// hooks; <see cref="UpdateAll"/> is called once per frame from
    /// <see cref="WorldUIRenderer.LateUpdate"/>, and <see cref="Clear"/> from the
    /// world-unload path.
    /// </summary>
    public static class SelectionRing
    {
        static readonly Dictionary<Actor, GameObject> _rings = new Dictionary<Actor, GameObject>();
        static readonly Dictionary<float, Mesh> _torusByRadius = new Dictionary<float, Mesh>();
        static Material? _material;

        public static void Show(Actor a)
        {
            if (a == null || a.asset == null) return;
            if (_rings.ContainsKey(a)) return;
            if (!EnsureMaterial()) return;

            float r = QuantizeRadius(GetActorOuterRadius(a));
            Mesh m = GetOrBuildTorus(r);

            var go = new GameObject("SelectionRing:" + a.GetHashCode());
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = m;
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _rings[a] = go;
        }

        public static void Hide(Actor a)
        {
            if (a == null) return;
            if (_rings.TryGetValue(a, out var go))
            {
                _rings.Remove(a);
                if (go != null) Object.Destroy(go);
            }
        }

        public static void UpdateAll()
        {
            if (_rings.Count == 0) return;
            List<Actor>? toRemove = null;
            foreach (var kv in _rings)
            {
                Actor a = kv.Key;
                GameObject go = kv.Value;
                if (a == null || go == null)
                {
                    (toRemove ??= new List<Actor>()).Add(a);
                    continue;
                }
                // Sit the ring just above the terrain under the actor, then lay it
                // tangent to the cylindrical sphere so it doesn't z-fight or punch
                // through hills on curved worlds.
                Vector3 p = Tools.To3DTileHeight(a.current_position, 0.005f);
                go.transform.position = p;
                go.transform.rotation = Tools.GetRotation(a.current_position.AsIntClamped()) * Quaternion.Euler(90f, 0f, 0f);
            }
            if (toRemove != null)
            {
                for (int i = 0; i < toRemove.Count; i++) Hide(toRemove[i]);
            }
        }

        public static void Clear()
        {
            foreach (var kv in _rings) if (kv.Value != null) Object.Destroy(kv.Value);
            foreach (var m in _torusByRadius.Values) if (m != null) Object.Destroy(m);
            _torusByRadius.Clear();
            if (_material != null) { Object.Destroy(_material); _material = null; }
            _rings.Clear();
        }

        // ---- internals ----

        static float QuantizeRadius(float r)
        {
            return Mathf.Round(r / 0.05f) * 0.05f;
        }

        static float GetActorOuterRadius(Actor a)
        {
            // Conservative size derivation; tweakable. Phase 7 docs target
            // `stats.size * 0.6f + 0.2f`, but stats wiring lands with Step 4.
            float baseR = 0.4f;
            return baseR + 0.2f;
        }

        static Mesh GetOrBuildTorus(float outerR)
        {
            if (_torusByRadius.TryGetValue(outerR, out var m)) return m;
            m = BuildTorus(outerR, outerR - 0.04f, 64);
            _torusByRadius[outerR] = m;
            return m;
        }

        static Mesh BuildTorus(float outerR, float innerR, int segments)
        {
            var verts = new Vector3[segments * 2];
            var tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / segments * Mathf.PI * 2f;
                float cx = Mathf.Cos(t), cz = Mathf.Sin(t);
                verts[i * 2] = new Vector3(cx * outerR, 0f, cz * outerR);
                verts[i * 2 + 1] = new Vector3(cx * innerR, 0f, cz * innerR);
            }
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int o0 = i * 2, o1 = i * 2 + 1, n0 = next * 2, n1 = next * 2 + 1;
                int b = i * 6;
                tris[b + 0] = o0; tris[b + 1] = n0; tris[b + 2] = o1;
                tris[b + 3] = o1; tris[b + 4] = n0; tris[b + 5] = n1;
            }
            var mesh = new Mesh { name = $"selring:{outerR:F2}" };
            mesh.SetVertices(new List<Vector3>(verts));
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static bool EnsureMaterial()
        {
            if (_material != null) return true;
            Shader? s = Resources.Load<Shader>("Shaders/SelectionRing");
            if (s == null) s = Shader.Find("Sprites/Default");
            if (s == null) return false;
            _material = new Material(s) { name = "WSM3D.SelectionRing", color = new Color(0.2f, 1f, 0.4f, 0.6f) };
            return true;
        }
    }
}
