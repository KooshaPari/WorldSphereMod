using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// VISUAL SINK for <see cref="RenderErrorRegistry"/>. Draws a distinct-colored unit
    /// cube per failure TYPE at the failing object's position — GMod ERROR-prop style.
    ///
    /// CRITICAL: markers route through <see cref="VoxelRender.Submit"/> (the known-good
    /// OpaqueVertexColor/Standard material). They therefore CANNOT themselves render the
    /// Hidden/InternalError magenta — a magenta marker means ShaderFailed by design, not
    /// a marker bug. The cube mesh + winding mirror the proven SanityTestCube.
    /// </summary>
    public static class RenderErrorMarkers
    {
        // Distinct color per failure type. Matches the enum doc-comments.
        static Color ColorFor(RenderErrorType t)
        {
            switch (t)
            {
                case RenderErrorType.ShaderFailed:    return Color.magenta;
                case RenderErrorType.MeshBuildFailed: return Color.red;
                case RenderErrorType.VoxelNotReady:   return new Color(1f, 1f, 0f, 0.6f); // yellow translucent
                case RenderErrorType.MaterialNull:    return new Color(1f, 0.55f, 0f, 1f); // orange
                case RenderErrorType.Unsupported:     return Color.cyan;
                case RenderErrorType.SpriteNull:      return new Color(0.6f, 0.6f, 0.6f, 1f); // grey
                default:                              return Color.white;
            }
        }

        // Marker world size ~ actor-size so it reads at strategy zoom without filling the frame.
        static readonly float _markerSize = 6f;
        static readonly List<RenderErrorRegistry.Marker> _scratch = new List<RenderErrorRegistry.Marker>(256);
        static Mesh? _mesh;

        /// <summary>
        /// Drain this frame's queued markers and submit a typed colored cube per failure.
        /// Call from the frame driver BEFORE VoxelRender.Flush so markers batch with the
        /// frame's other voxel submissions. No-op when the material isn't ready (the
        /// failure is still recorded in telemetry regardless).
        /// </summary>
        public static void DrawQueued()
        {
            // RenderErrorProps OFF → telemetry already recorded, but no visual prop:
            // drop the queued markers so they don't leak into a later on-frame.
            if (Core.savedSettings == null || !Core.savedSettings.RenderErrorProps)
            {
                RenderErrorRegistry.ClearFrameMarkers();
                return;
            }
            if (VoxelRender.GetResolvedMaterial() == null) { RenderErrorRegistry.ClearFrameMarkers(); return; }

            _scratch.Clear();
            RenderErrorRegistry.DrainFrameMarkers(_scratch);
            if (_scratch.Count == 0) return;

            Mesh mesh = EnsureMesh();
            for (int i = 0; i < _scratch.Count; i++)
            {
                RenderErrorRegistry.Marker m = _scratch[i];
                Matrix4x4 trs = Matrix4x4.TRS(m.Pos, Quaternion.identity, Vector3.one * _markerSize);
                // Route through the known-good voxel material so markers can't render magenta themselves.
                VoxelRender.Submit(mesh, trs, ColorFor(m.Type));
            }
        }

        // Unit cube (1x1x1) with explicit outward normals + per-vertex white color,
        // identical winding to SanityTestCube so the proven material renders it solid.
        static Mesh EnsureMesh()
        {
            if (_mesh != null) return _mesh;
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
                0, 1, 2,  0, 2, 3,
                4, 6, 5,  4, 7, 6,
                8, 9, 10, 8, 10, 11,
                12, 14, 13, 12, 15, 14,
                16, 17, 18, 16, 18, 19,
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
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;

            _mesh = new Mesh { name = "WSM3D.RenderErrorMarker" };
            _mesh.vertices = vertices;
            _mesh.triangles = triangles;
            _mesh.normals = normals;
            _mesh.colors = colors;
            _mesh.RecalculateBounds();
            return _mesh;
        }

        public static void Reset()
        {
            // Mesh is reusable across reloads; just drop any queued markers.
            RenderErrorRegistry.ClearFrameMarkers();
        }
    }
}
