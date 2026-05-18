using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.ProcGen
{
    public static class BuildingMeshGen
    {
        const byte AlphaThreshold = 16;

        public static Mesh Generate(BuildingAsset asset, BuildingRules rules)
        {
            if (rules == null) rules = BuildingRules.Default;

            Sprite? sprite = ResolveSprite(asset);
            if (sprite == null || sprite.texture == null)
            {
                return UnitCube($"procgen:fallback:{asset?.id ?? "null"}");
            }
            // Atlased textures imported without Read/Write enabled throw on GetPixels32.
            // Bail with a stub cube rather than crashing the render pass.
            if (!sprite.texture.isReadable)
            {
                return UnitCube($"procgen:unreadable:{asset?.id ?? "null"}");
            }

            Rect texRect = sprite.textureRect;
            int w = Mathf.Max(1, (int)texRect.width);
            int h = Mathf.Max(1, (int)texRect.height);
            int x0 = (int)texRect.x;
            int y0 = (int)texRect.y;
            int texW = sprite.texture.width;

            Color32[] full = sprite.texture.GetPixels32();
            Color32[] pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int srcRow = (y0 + y) * texW + x0;
                int dstRow = y * w;
                for (int x = 0; x < w; x++) pixels[dstRow + x] = full[srcRow + x];
            }

            RectInt bbox = DetectFootprint(pixels, w, h);
            if (bbox.width <= 0 || bbox.height <= 0)
            {
                // Blank/transparent sprite — return null so the cache doesn't poison
                // this asset id with a unit-cube fallback for the session. Common during
                // building construction animations where some frames are temporarily empty.
                return null;
            }

            float ppu = Mathf.Max(1f, sprite.pixelsPerUnit);
            float buildingScale = (Core.savedSettings != null) ? Core.savedSettings.BuildingSize : 0.5f;
            // px-to-world: matches the sprite quad render path (sprite.pixelsPerUnit) and applies
            // the same BuildingSize multiplier QuantumSprites uses for tRenderScales.
            float pxToWorld = buildingScale / ppu;

            float halfX = bbox.width * 0.5f * pxToWorld;
            float halfZ = (rules.FootprintDepth > 0f)
                ? rules.FootprintDepth * 0.5f
                : halfX;

            int stories = InferStories(pixels, w, h, bbox, rules);

            float tileHeight = (Core.savedSettings != null) ? Core.savedSettings.TileHeight : 1f;
            float storyHeight = tileHeight * stories * buildingScale;
            if (storyHeight <= 0f) storyHeight = bbox.height * pxToWorld;

            List<DoorSpec> openings = InferOpenings(pixels, w, h, bbox, rules);

            Color wallColor = SampleWallColor(pixels, w, bbox, stories);
            InferRoof(pixels, w, bbox, rules, wallColor, out RoofStyle roofStyle, out Color roofColor);

            return BuildMesh(asset, halfX, halfZ, storyHeight, bbox, openings, pxToWorld, wallColor, roofColor, roofStyle, rules.PerpendicularRoof);
        }

        static Sprite? ResolveSprite(BuildingAsset asset)
        {
            if (asset == null) return null;
            try { asset.checkSpritesAreLoaded(); } catch { /* asset table not ready */ }
            BuildingSprites? bs = asset.building_sprites;
            if (bs == null) return null;
            var anims = bs.animation_data;
            if (anims == null) return null;
            for (int i = 0; i < anims.Count; i++)
            {
                var a = anims[i];
                if (a == null) continue;
                if (a.main != null && a.main.Length > 0 && a.main[0] != null) return a.main[0];
                if (a.list_main != null && a.list_main.Count > 0 && a.list_main[0] != null) return a.list_main[0];
            }
            return bs.construction;
        }

        static RectInt DetectFootprint(Color32[] px, int w, int h)
        {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (px[row + x].a > AlphaThreshold)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (maxX < 0) return new RectInt(0, 0, 0, 0);
            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        static int InferStories(Color32[] px, int w, int h, RectInt bbox, BuildingRules rules)
        {
            if (rules.Stories >= 1) return Mathf.Clamp(rules.Stories, 1, 4);
            if (bbox.height < 6) return 1;

            float[] hue = new float[bbox.height];
            float[] lum = new float[bbox.height];
            for (int yy = 0; yy < bbox.height; yy++)
            {
                int y = bbox.yMin + yy;
                int row = y * w;
                float sumH = 0f, sumL = 0f;
                int count = 0;
                for (int xx = 0; xx < bbox.width; xx++)
                {
                    int x = bbox.xMin + xx;
                    Color32 c32 = px[row + x];
                    if (c32.a <= AlphaThreshold) continue;
                    Color c = c32;
                    Color.RGBToHSV(c, out float H, out float _, out float V);
                    sumH += H * 360f;
                    sumL += V;
                    count++;
                }
                if (count > 0) { hue[yy] = sumH / count; lum[yy] = sumL / count; }
                else { hue[yy] = -1f; lum[yy] = 0f; }
            }

            // Seed bandHue from the first non-transparent row, not hue[0]. A transparent
            // top row leaves hue[0]=-1, which would make the first opaque row always
            // trigger a phantom band break (inflated story count on padded sprites).
            int seed = 0;
            while (seed < bbox.height && hue[seed] < 0f) seed++;
            if (seed >= bbox.height) return 1;
            int bands = 1;
            float bandHue = hue[seed];
            float bandLumSum = lum[seed];
            int bandLen = 1;
            for (int yy = seed + 1; yy < bbox.height; yy++)
            {
                if (hue[yy] < 0f) continue;
                float dH = Mathf.Min(Mathf.Abs(hue[yy] - bandHue), 360f - Mathf.Abs(hue[yy] - bandHue));
                float bandLumAvg = bandLumSum / Mathf.Max(1, bandLen);
                bool hueBreak = dH >= 15f;
                bool lumDip = (bandLumAvg - lum[yy]) > 0.1f;
                if (hueBreak || lumDip)
                {
                    bands++;
                    bandHue = hue[yy];
                    bandLumSum = lum[yy];
                    bandLen = 1;
                }
                else
                {
                    bandHue = (bandHue * bandLen + hue[yy]) / (bandLen + 1);
                    bandLumSum += lum[yy];
                    bandLen++;
                }
            }
            return Mathf.Clamp(bands, 1, 4);
        }

        static List<DoorSpec> InferOpenings(Color32[] px, int w, int h, RectInt bbox, BuildingRules rules)
        {
            var result = new List<DoorSpec>();
            bool useRulesDoors = rules.Doors != null && rules.Doors.Length > 0;
            bool useRulesWindows = rules.Windows != null && rules.Windows.Length > 0;
            if (useRulesDoors) result.AddRange(rules.Doors!);
            if (useRulesWindows) result.AddRange(rules.Windows!);
            if (useRulesDoors && useRulesWindows) return result;

            int lowerH = Mathf.Max(1, Mathf.RoundToInt(bbox.height * 0.4f));
            int yStart = bbox.yMin;
            int yEnd = bbox.yMin + lowerH;

            float wallSum = 0f;
            int wallCount = 0;
            for (int yy = 0; yy < bbox.height; yy++)
            {
                int y = bbox.yMin + yy;
                int row = y * w;
                for (int xx = 0; xx < bbox.width; xx++)
                {
                    Color32 c32 = px[row + bbox.xMin + xx];
                    if (c32.a <= AlphaThreshold) continue;
                    Color.RGBToHSV((Color)c32, out _, out _, out float V);
                    wallSum += V;
                    wallCount++;
                }
            }
            float wallMean = wallCount > 0 ? wallSum / wallCount : 0.5f;
            float darkCutoff = Mathf.Min(0.25f, wallMean * 0.5f);

            bool[,] dark = new bool[bbox.width, lowerH];
            for (int yy = 0; yy < lowerH; yy++)
            {
                int y = yStart + yy;
                int row = y * w;
                for (int xx = 0; xx < bbox.width; xx++)
                {
                    Color32 c32 = px[row + bbox.xMin + xx];
                    if (c32.a <= AlphaThreshold) continue;
                    Color.RGBToHSV((Color)c32, out _, out _, out float V);
                    if (V < darkCutoff) dark[xx, yy] = true;
                }
            }

            bool[,] visited = new bool[bbox.width, lowerH];
            for (int yy = 0; yy < lowerH; yy++)
            for (int xx = 0; xx < bbox.width; xx++)
            {
                if (!dark[xx, yy] || visited[xx, yy]) continue;
                int minXr = xx, maxXr = xx, minYr = yy, maxYr = yy;
                var stack = new Stack<(int, int)>();
                stack.Push((xx, yy));
                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    if (cx < 0 || cx >= bbox.width || cy < 0 || cy >= lowerH) continue;
                    if (visited[cx, cy] || !dark[cx, cy]) continue;
                    visited[cx, cy] = true;
                    if (cx < minXr) minXr = cx;
                    if (cx > maxXr) maxXr = cx;
                    if (cy < minYr) minYr = cy;
                    if (cy > maxYr) maxYr = cy;
                    stack.Push((cx + 1, cy));
                    stack.Push((cx - 1, cy));
                    stack.Push((cx, cy + 1));
                    stack.Push((cx, cy - 1));
                }
                int rw = maxXr - minXr + 1;
                int rh = maxYr - minYr + 1;
                if (rw * rh < 4) continue;

                bool isDoor = rh > rw && rh >= 4;
                bool isWindow = rw > rh && rh <= 6;
                if (!isDoor && !isWindow) continue;
                if (isDoor && useRulesDoors) continue;
                if (isWindow && useRulesWindows) continue;

                result.Add(new DoorSpec
                {
                    x = bbox.xMin + minXr,
                    y = bbox.yMin + minYr,
                    w = rw,
                    h = rh,
                });
            }
            return result;
        }

        static Color SampleWallColor(Color32[] px, int w, RectInt bbox, int stories)
        {
            int bandStart = bbox.yMin + Mathf.RoundToInt(bbox.height * 0.4f);
            int bandEnd = bbox.yMin + Mathf.RoundToInt(bbox.height * 0.7f);
            if (bandEnd <= bandStart) bandEnd = bandStart + 1;
            return AverageColor(px, w, bbox.xMin, bandStart, bbox.width, bandEnd - bandStart);
        }

        static Color SampleTopColor(Color32[] px, int w, RectInt bbox)
        {
            int slice = Mathf.Max(1, Mathf.RoundToInt(bbox.height * 0.15f));
            int yStart = bbox.yMin + bbox.height - slice;
            return AverageColor(px, w, bbox.xMin, yStart, bbox.width, slice);
        }

        // "Roof pixel" = visibly distinct from the wall color and either coloured (any hue
        // with mild saturation) or a desaturated dark/light tone that still contrasts with
        // the wall band. Previously restricted to warm hues (0-40° or 340-360°) which
        // missed grey stone, green thatch, and blue slate roofs — most WorldBox buildings.
        static bool IsRoofPixel(Color32 c32, out float hueDeg, Color wallRef)
        {
            hueDeg = -1f;
            if (c32.a <= AlphaThreshold) return false;
            Color c = c32;
            Color.RGBToHSV(c, out float H, out float _, out float _2);
            hueDeg = H * 360f;
            float dr = c.r - wallRef.r;
            float dg = c.g - wallRef.g;
            float db = c.b - wallRef.b;
            float dist = Mathf.Sqrt(dr * dr + dg * dg + db * db);
            return dist > 0.18f;
        }

        static void InferRoof(Color32[] px, int w, RectInt bbox, BuildingRules rules,
            Color wallRef, out RoofStyle style, out Color color)
        {
            if (rules.Roof != RoofStyle.Inferred)
            {
                style = rules.Roof;
                color = SampleTopColor(px, w, bbox);
                return;
            }

            int bandH = Mathf.Max(1, Mathf.RoundToInt(bbox.height * 0.2f));
            int bandYStart = bbox.yMin + bbox.height - bandH;

            int[] topRun = new int[bbox.width];
            int columnsWithRoof = 0;
            // Hue histogram: 36 bins of 10 degrees each (0..360).
            int[] hueBins = new int[36];
            long sumR = 0, sumG = 0, sumB = 0;
            int roofPixelCount = 0;

            for (int xx = 0; xx < bbox.width; xx++)
            {
                int x = bbox.xMin + xx;
                int run = 0;
                bool inRun = true;
                for (int yy = bandH - 1; yy >= 0; yy--)
                {
                    int y = bandYStart + yy;
                    Color32 c = px[y * w + x];
                    if (IsRoofPixel(c, out float hueDeg, wallRef))
                    {
                        int bin = Mathf.Clamp((int)(hueDeg / 10f), 0, 35);
                        hueBins[bin]++;
                        sumR += c.r; sumG += c.g; sumB += c.b;
                        roofPixelCount++;
                        if (inRun) run++;
                    }
                    else
                    {
                        if (c.a > AlphaThreshold) inRun = false;
                    }
                }
                topRun[xx] = run;
                if (run > 0) columnsWithRoof++;
            }

            if (roofPixelCount == 0)
            {
                style = RoofStyle.Flat;
                color = SampleTopColor(px, w, bbox);
                return;
            }

            float invN = 1f / (roofPixelCount * 255f);
            color = new Color(sumR * invN, sumG * invN, sumB * invN, 1f);

            float coverage = (float)columnsWithRoof / Mathf.Max(1, bbox.width);
            if (coverage < 0.6f)
            {
                style = RoofStyle.Flat;
                return;
            }

            float mean = 0f;
            for (int i = 0; i < bbox.width; i++) mean += topRun[i];
            mean /= Mathf.Max(1, bbox.width);

            float variance = 0f;
            for (int i = 0; i < bbox.width; i++)
            {
                float d = topRun[i] - mean;
                variance += d * d;
            }
            variance /= Mathf.Max(1, bbox.width);
            float stddev = Mathf.Sqrt(variance);
            float uniformity = mean > 0f ? stddev / mean : 1f;

            // Center-vs-edge heuristic for hipped: middle third averages noticeably taller than edges.
            int third = Mathf.Max(1, bbox.width / 3);
            float edgeSum = 0f;
            int edgeN = 0;
            float centerSum = 0f;
            int centerN = 0;
            for (int i = 0; i < bbox.width; i++)
            {
                if (i < third || i >= bbox.width - third) { edgeSum += topRun[i]; edgeN++; }
                else { centerSum += topRun[i]; centerN++; }
            }
            float edgeMean = edgeN > 0 ? edgeSum / edgeN : 0f;
            float centerMean = centerN > 0 ? centerSum / centerN : 0f;

            if (uniformity < 0.35f)
            {
                style = RoofStyle.Gable;
            }
            else if (centerMean > edgeMean * 1.4f && edgeMean > 0f)
            {
                style = RoofStyle.Hipped;
            }
            else
            {
                style = RoofStyle.Flat;
            }
        }

        static Color AverageColor(Color32[] px, int w, int x0, int y0, int rw, int rh)
        {
            float r = 0f, g = 0f, b = 0f;
            int count = 0;
            for (int yy = 0; yy < rh; yy++)
            {
                int row = (y0 + yy) * w;
                for (int xx = 0; xx < rw; xx++)
                {
                    Color32 c = px[row + x0 + xx];
                    if (c.a <= AlphaThreshold) continue;
                    r += c.r; g += c.g; b += c.b;
                    count++;
                }
            }
            if (count == 0) return new Color(0.7f, 0.7f, 0.7f, 1f);
            float inv = 1f / (count * 255f);
            return new Color(r * inv, g * inv, b * inv, 1f);
        }

        enum Side { Front = 0, Right = 1, Back = 2, Left = 3 }

        static Mesh BuildMesh(BuildingAsset asset, float halfX, float halfZ, float height,
            RectInt bbox, List<DoorSpec> openings, float pxToWorld,
            Color wallColor, Color roofColor, RoofStyle roofStyle, bool perpendicularRoof)
        {
            var verts = new List<Vector3>(64);
            var cols = new List<Color>(64);
            var tris = new List<int>(96);

            // Bucket openings by which side they touch. Doors → Front (-Z), bias by horizontal position
            // for windows so all 4 walls can host some.
            var openingsBySide = new List<DoorSpec>[4]
            {
                new List<DoorSpec>(), new List<DoorSpec>(), new List<DoorSpec>(), new List<DoorSpec>()
            };
            for (int i = 0; i < openings.Count; i++)
            {
                var o = openings[i];
                Side s = PickSide(o, bbox);
                openingsBySide[(int)s].Add(o);
            }

            EmitWall(Side.Front, halfX, halfZ, height, openingsBySide[0], bbox, pxToWorld, wallColor, verts, cols, tris);
            EmitWall(Side.Right, halfX, halfZ, height, openingsBySide[1], bbox, pxToWorld, wallColor, verts, cols, tris);
            EmitWall(Side.Back, halfX, halfZ, height, openingsBySide[2], bbox, pxToWorld, wallColor, verts, cols, tris);
            EmitWall(Side.Left, halfX, halfZ, height, openingsBySide[3], bbox, pxToWorld, wallColor, verts, cols, tris);

            switch (roofStyle)
            {
                case RoofStyle.Gable:
                    EmitGableRoof(halfX, halfZ, height, roofColor, perpendicularRoof, verts, cols, tris);
                    break;
                case RoofStyle.Hipped:
                    EmitHippedRoof(halfX, halfZ, height, roofColor, perpendicularRoof, verts, cols, tris);
                    break;
                default:
                    EmitRoofCap(halfX, halfZ, height, roofColor, verts, cols, tris);
                    break;
            }

            var mesh = new Mesh { name = $"procgen:{asset?.id ?? "null"}" };
            if (verts.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Side PickSide(DoorSpec o, RectInt bbox)
        {
            int cx = o.x + o.w / 2;
            int relX = cx - bbox.xMin;
            int third = Mathf.Max(1, bbox.width / 3);
            if (relX < third) return Side.Left;
            if (relX >= bbox.width - third) return Side.Right;
            return (o.h > o.w) ? Side.Front : Side.Back;
        }

        static void EmitWall(Side side, float halfX, float halfZ, float height,
            List<DoorSpec> sideOpenings, RectInt bbox, float pxToWorld, Color wallColor,
            List<Vector3> verts, List<Color> cols, List<int> tris)
        {
            // Local 2D wall coords: u runs along the wall's surface (x in mesh terms), v = vertical (y).
            // Wall length in world units:
            float wallLen = (side == Side.Front || side == Side.Back) ? (halfX * 2f) : (halfZ * 2f);

            if (sideOpenings == null || sideOpenings.Count == 0)
            {
                EmitFlatQuad(side, halfX, halfZ, height, 0f, wallLen, 0f, height, 0f, wallColor, verts, cols, tris);
                return;
            }

            // Pick the largest opening on this side and inset only that one (simple but stable;
            // multi-cutout face splitting is left to a later pass).
            DoorSpec pick = sideOpenings[0];
            int pickArea = pick.w * pick.h;
            for (int i = 1; i < sideOpenings.Count; i++)
            {
                int a = sideOpenings[i].w * sideOpenings[i].h;
                if (a > pickArea) { pickArea = a; pick = sideOpenings[i]; }
            }

            float oU, oV, oW, oH;
            MapOpeningToWall(side, pick, bbox, halfX, halfZ, height, pxToWorld, wallLen,
                out oU, out oV, out oW, out oH);

            float pad = wallLen * 0.05f;
            oU = Mathf.Clamp(oU, pad, wallLen - oW - pad);
            oV = Mathf.Clamp(oV, 0.01f, height - oH - 0.01f);
            if (oW <= 0f || oH <= 0f)
            {
                EmitFlatQuad(side, halfX, halfZ, height, 0f, wallLen, 0f, height, 0f, wallColor, verts, cols, tris);
                return;
            }

            // 4 surround quads: left strip, right strip, bottom strip (under), top strip (over).
            EmitFlatQuad(side, halfX, halfZ, height, 0f, oU, 0f, height, 0f, wallColor, verts, cols, tris);
            EmitFlatQuad(side, halfX, halfZ, height, oU + oW, wallLen, 0f, height, 0f, wallColor, verts, cols, tris);
            EmitFlatQuad(side, halfX, halfZ, height, oU, oU + oW, 0f, oV, 0f, wallColor, verts, cols, tris);
            EmitFlatQuad(side, halfX, halfZ, height, oU, oU + oW, oV + oH, height, 0f, wallColor, verts, cols, tris);

            float insetDepth = Mathf.Min(halfX, halfZ) * 0.05f;
            Color darker = new Color(wallColor.r * 0.4f, wallColor.g * 0.4f, wallColor.b * 0.4f, 1f);
            EmitFlatQuad(side, halfX, halfZ, height, oU, oU + oW, oV, oV + oH, insetDepth, darker, verts, cols, tris);
        }

        static void MapOpeningToWall(Side side, DoorSpec o, RectInt bbox,
            float halfX, float halfZ, float height, float pxToWorld, float wallLen,
            out float u, out float v, out float w, out float h)
        {
            float frac = 1f;
            int relX = o.x - bbox.xMin;
            int relY = o.y - bbox.yMin;
            switch (side)
            {
                case Side.Front:
                case Side.Back:
                    u = relX * pxToWorld;
                    w = o.w * pxToWorld;
                    break;
                default:
                    u = relX * pxToWorld;
                    w = o.w * pxToWorld;
                    break;
            }
            v = relY * pxToWorld;
            h = o.h * pxToWorld;
            float clampW = wallLen * 0.7f;
            if (w > clampW) { frac = clampW / w; w *= frac; }
            if (h > height * 0.9f) h = height * 0.9f;
            if (u + w > wallLen) u = Mathf.Max(0f, wallLen - w);
            if (v + h > height) v = Mathf.Max(0f, height - h);
        }

        static void EmitFlatQuad(Side side, float halfX, float halfZ, float height,
            float u0, float u1, float v0, float v1, float inset, Color color,
            List<Vector3> verts, List<Color> cols, List<int> tris)
        {
            if (u1 - u0 <= 1e-4f || v1 - v0 <= 1e-4f) return;

            Vector3 a, b, c, d;
            switch (side)
            {
                case Side.Front:
                    {
                        float x0 = -halfX + u0;
                        float x1 = -halfX + u1;
                        float z = -halfZ + inset;
                        a = new Vector3(x0, v0, z);
                        b = new Vector3(x1, v0, z);
                        c = new Vector3(x1, v1, z);
                        d = new Vector3(x0, v1, z);
                        break;
                    }
                case Side.Back:
                    {
                        float x0 = halfX - u0;
                        float x1 = halfX - u1;
                        float z = halfZ - inset;
                        a = new Vector3(x0, v0, z);
                        b = new Vector3(x1, v0, z);
                        c = new Vector3(x1, v1, z);
                        d = new Vector3(x0, v1, z);
                        break;
                    }
                case Side.Right:
                    {
                        float z0 = -halfZ + u0;
                        float z1 = -halfZ + u1;
                        float x = halfX - inset;
                        a = new Vector3(x, v0, z0);
                        b = new Vector3(x, v0, z1);
                        c = new Vector3(x, v1, z1);
                        d = new Vector3(x, v1, z0);
                        break;
                    }
                default: // Left
                    {
                        float z0 = halfZ - u0;
                        float z1 = halfZ - u1;
                        float x = -halfX + inset;
                        a = new Vector3(x, v0, z0);
                        b = new Vector3(x, v0, z1);
                        c = new Vector3(x, v1, z1);
                        d = new Vector3(x, v1, z0);
                        break;
                    }
            }

            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            cols.Add(color); cols.Add(color); cols.Add(color); cols.Add(color);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        static void EmitRoofCap(float halfX, float halfZ, float height, Color color,
            List<Vector3> verts, List<Color> cols, List<int> tris)
        {
            int i = verts.Count;
            verts.Add(new Vector3(-halfX, height,  halfZ));
            verts.Add(new Vector3( halfX, height,  halfZ));
            verts.Add(new Vector3( halfX, height, -halfZ));
            verts.Add(new Vector3(-halfX, height, -halfZ));
            cols.Add(color); cols.Add(color); cols.Add(color); cols.Add(color);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        static void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color,
            List<Vector3> verts, List<Color> cols, List<int> tris)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            cols.Add(color); cols.Add(color); cols.Add(color); cols.Add(color);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        static void AddTri(Vector3 a, Vector3 b, Vector3 c, Color color,
            List<Vector3> verts, List<Color> cols, List<int> tris)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            cols.Add(color); cols.Add(color); cols.Add(color);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        static void EmitGableRoof(float halfX, float halfZ, float height, Color color,
            bool perpendicular, List<Vector3> verts, List<Color> cols, List<int> tris)
        {
            // Default: ridge runs along the long axis. PerpendicularRoof flips it onto the short axis.
            bool xIsLong = halfX >= halfZ;
            bool ridgeAlongX = perpendicular ? !xIsLong : xIsLong;
            float ridgeY = height + Mathf.Min(halfX, halfZ);

            Vector3 wFL = new Vector3(-halfX, height, -halfZ);
            Vector3 wFR = new Vector3( halfX, height, -halfZ);
            Vector3 wBR = new Vector3( halfX, height,  halfZ);
            Vector3 wBL = new Vector3(-halfX, height,  halfZ);

            if (ridgeAlongX)
            {
                Vector3 r0 = new Vector3(-halfX, ridgeY, 0f);
                Vector3 r1 = new Vector3( halfX, ridgeY, 0f);
                AddQuad(wFR, wFL, r0, r1, color, verts, cols, tris);
                AddQuad(wBL, wBR, r1, r0, color, verts, cols, tris);
                AddTri(wFL, wBL, r0, color, verts, cols, tris);
                AddTri(wBR, wFR, r1, color, verts, cols, tris);
            }
            else
            {
                Vector3 r0 = new Vector3(0f, ridgeY, -halfZ);
                Vector3 r1 = new Vector3(0f, ridgeY,  halfZ);
                AddQuad(wBR, wFR, r0, r1, color, verts, cols, tris);
                AddQuad(wFL, wBL, r1, r0, color, verts, cols, tris);
                AddTri(wFR, wFL, r0, color, verts, cols, tris);
                AddTri(wBL, wBR, r1, color, verts, cols, tris);
            }
        }

        static void EmitHippedRoof(float halfX, float halfZ, float height, Color color,
            bool perpendicular, List<Vector3> verts, List<Color> cols, List<int> tris)
        {
            bool xIsLong = halfX >= halfZ;
            bool ridgeAlongX = perpendicular ? !xIsLong : xIsLong;
            float longHalf = ridgeAlongX ? halfX : halfZ;
            float ridgeY = height + Mathf.Min(halfX, halfZ);
            float ridgeHalf = longHalf * 0.4f;

            Vector3 wFL = new Vector3(-halfX, height, -halfZ);
            Vector3 wFR = new Vector3( halfX, height, -halfZ);
            Vector3 wBR = new Vector3( halfX, height,  halfZ);
            Vector3 wBL = new Vector3(-halfX, height,  halfZ);

            if (ridgeAlongX)
            {
                Vector3 r0 = new Vector3(-ridgeHalf, ridgeY, 0f);
                Vector3 r1 = new Vector3( ridgeHalf, ridgeY, 0f);
                AddQuad(wFR, wFL, r0, r1, color, verts, cols, tris);
                AddQuad(wBL, wBR, r1, r0, color, verts, cols, tris);
                AddTri(wFL, wBL, r0, color, verts, cols, tris);
                AddTri(wBR, wFR, r1, color, verts, cols, tris);
            }
            else
            {
                Vector3 r0 = new Vector3(0f, ridgeY, -ridgeHalf);
                Vector3 r1 = new Vector3(0f, ridgeY,  ridgeHalf);
                AddQuad(wBR, wFR, r0, r1, color, verts, cols, tris);
                AddQuad(wFL, wBL, r1, r0, color, verts, cols, tris);
                AddTri(wFR, wFL, r0, color, verts, cols, tris);
                AddTri(wBL, wBR, r1, color, verts, cols, tris);
            }
        }

        static Mesh UnitCube(string name)
        {
            var m = new Mesh { name = name };
            Vector3[] verts =
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
            int[] tris =
            {
                0, 2, 1, 0, 3, 2,
                1, 2, 6, 1, 6, 5,
                5, 6, 7, 5, 7, 4,
                4, 7, 3, 4, 3, 0,
                3, 7, 6, 3, 6, 2,
                4, 0, 1, 4, 1, 5,
            };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
