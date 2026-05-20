using UnityEngine;
using WorldSphereMod.ProcGen;

namespace WorldSphereMod.Foliage
{
    internal enum CrossedQuadVariant
    {
        Generic,
        Oak,
        Pine,
        Palm
    }

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
        public static Mesh Build(Sprite sprite, BuildingShape shape, float swayAmplitude, CrossedQuadVariant variant)
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
            GetProfile(shape, variant, out float baseWidthScale, out float topWidthScale, out float heightScale, out float baseLift);
            float scaledHeight = quadH * heightScale;
            float baseHalfW = halfW * baseWidthScale;
            float topHalfW = halfW * topWidthScale;
            float y0 = baseLift;
            float y1 = baseLift + scaledHeight;

            var mesh = new Mesh { name = $"crossquad:{(int)shape}:{(int)variant}:{sprite.name}" };

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
                    // Quad A: base at y=y0 along +/-X
                    new Vector3(-baseHalfW, y0,    0f),  // 0 BL
                    new Vector3( baseHalfW, y0,    0f),  // 1 BR
                    new Vector3( topHalfW,  y1,    0f),  // 2 TR
                    new Vector3(-topHalfW,  y1,    0f),  // 3 TL
                    // Quad B: base at y=y0 along +/-Z (perpendicular to A around Y axis)
                    new Vector3(0f,    y0,   -baseHalfW), // 4 BL
                    new Vector3(0f,    y0,    baseHalfW), // 5 BR
                    new Vector3(0f,    y1,    topHalfW), // 6 TR
                    new Vector3(0f,    y1,   -topHalfW), // 7 TL
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

        static void GetProfile(BuildingShape shape, CrossedQuadVariant variant, out float baseWidthScale, out float topWidthScale, out float heightScale, out float baseLift)
        {
            baseWidthScale = 1f;
            topWidthScale = 1f;
            heightScale = 1f;
            baseLift = 0f;

            if (shape == BuildingShape.Single) return;

            switch (variant)
            {
                case CrossedQuadVariant.Oak:
                    baseWidthScale = 1.15f;
                    topWidthScale = 0.82f;
                    heightScale = 0.95f;
                    break;
                case CrossedQuadVariant.Pine:
                    baseWidthScale = 0.68f;
                    topWidthScale = 0.22f;
                    heightScale = 1.42f;
                    break;
                case CrossedQuadVariant.Palm:
                    baseWidthScale = 0.42f;
                    topWidthScale = 1.02f;
                    heightScale = 1.18f;
                    baseLift = 0.08f;
                    break;
            }
        }
    }
}
