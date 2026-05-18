using UnityEngine;
using WorldSphereMod.ProcGen;

namespace WorldSphereMod.Foliage
{
    /// <summary>
    /// Builds a foliage <see cref="Mesh"/> from a sprite. The two output shapes:
    ///   <see cref="BuildingShape.CrossedQuad"/> emits two perpendicular vertical
    ///   quads sharing the same sprite UVs — classic billboarded foliage. The
    ///   <see cref="BuildingShape.Single"/> shape emits one ground-aligned quad
    ///   (lies flat on the XZ plane) for rocks and other non-swaying decals.
    /// Vertex color encodes sway amplitude in the alpha channel; <c>uv2.x</c>
    /// carries the normalized height-along-quad used by the wind shader.
    /// </summary>
    public static class CrossedQuadMesher
    {
        public static Mesh Build(Sprite sprite, BuildingShape shape, float swayAmplitude)
        {
            if (sprite == null) return CreateEmpty();
            // Defensive: the upstream SpriteVoxelizer crashed on non-readable atlases.
            // Crossed-quad emission is UV-only so reads aren't strictly required, but
            // future shape extensions may sample; keep the guard consistent.
            if (sprite.texture != null && !sprite.texture.isReadable) return CreateEmpty();

            Rect r = sprite.rect;
            float ppu = Mathf.Max(1f, sprite.pixelsPerUnit);
            float quadW = r.width / ppu;
            float quadH = r.height / ppu;
            float halfW = quadW * 0.5f;

            // Pull UVs from the atlas-resolved sprite.uv array (4 corners, may be rotated/packed).
            Vector2[] su = sprite.uv;
            Vector2 uvBL, uvBR, uvTR, uvTL;
            if (su != null && su.Length >= 4)
            {
                uvBL = su[0]; uvBR = su[1]; uvTR = su[2]; uvTL = su[3];
            }
            else
            {
                // Fallback: compute from textureRect / texture size.
                Texture tex = sprite.texture;
                float texW = (tex != null && tex.width > 0) ? tex.width : 1f;
                float texH = (tex != null && tex.height > 0) ? tex.height : 1f;
                Rect tr = sprite.textureRect;
                float u0 = tr.x / texW;
                float v0 = tr.y / texH;
                float u1 = (tr.x + tr.width) / texW;
                float v1 = (tr.y + tr.height) / texH;
                uvBL = new Vector2(u0, v0);
                uvBR = new Vector2(u1, v0);
                uvTR = new Vector2(u1, v1);
                uvTL = new Vector2(u0, v1);
            }

            Color sway = new Color(1f, 1f, 1f, swayAmplitude);
            var mesh = new Mesh { name = $"crossquad:{(int)shape}:{sprite.name}" };

            if (shape == BuildingShape.Single)
            {
                // One ground-aligned quad on the XZ plane. y = 0 (flat on ground).
                var verts = new Vector3[4]
                {
                    new Vector3(-halfW, 0f, -halfW),
                    new Vector3( halfW, 0f, -halfW),
                    new Vector3( halfW, 0f,  halfW),
                    new Vector3(-halfW, 0f,  halfW),
                };
                var uvs = new Vector2[4] { uvBL, uvBR, uvTR, uvTL };
                // Ground quad: all four verts share y = 0; height fraction is 0 across the board.
                var uv2 = new Vector2[4] { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero };
                var cols = new Color[4] { sway, sway, sway, sway };
                var tris = new int[6] { 0, 1, 2, 0, 2, 3 };

                mesh.vertices = verts;
                mesh.uv = uvs;
                mesh.uv2 = uv2;
                mesh.colors = cols;
                mesh.triangles = tris;
            }
            else
            {
                // CrossedQuad (default for any non-Single shape passed in): two perpendicular
                // upright quads. Quad A spans X-axis, Quad B spans Z-axis (rotated 90° around Y).
                var verts = new Vector3[8]
                {
                    // Quad A: base at y=0 along +/-X
                    new Vector3(-halfW, 0f,    0f),  // 0 BL
                    new Vector3( halfW, 0f,    0f),  // 1 BR
                    new Vector3( halfW, quadH, 0f),  // 2 TR
                    new Vector3(-halfW, quadH, 0f),  // 3 TL
                    // Quad B: base at y=0 along +/-Z (perpendicular to A around Y axis)
                    new Vector3(0f,    0f,   -halfW), // 4 BL
                    new Vector3(0f,    0f,    halfW), // 5 BR
                    new Vector3(0f,    quadH, halfW), // 6 TR
                    new Vector3(0f,    quadH,-halfW), // 7 TL
                };
                var uvs = new Vector2[8]
                {
                    uvBL, uvBR, uvTR, uvTL,
                    uvBL, uvBR, uvTR, uvTL,
                };
                // uv2.x = 0 at base verts (y = 0), 1 at top verts (y = quadHeight). uv2.y unused.
                var uv2 = new Vector2[8]
                {
                    new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                };
                var cols = new Color[8] { sway, sway, sway, sway, sway, sway, sway, sway };
                var tris = new int[12]
                {
                    0, 2, 1, 0, 3, 2,  // Quad A (CCW from +Z front-facing)
                    4, 6, 5, 4, 7, 6,  // Quad B (CCW from +X front-facing)
                };

                mesh.vertices = verts;
                mesh.uv = uvs;
                mesh.uv2 = uv2;
                mesh.colors = cols;
                mesh.triangles = tris;
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateEmpty() => new Mesh { name = "crossquad:empty" };
    }
}
