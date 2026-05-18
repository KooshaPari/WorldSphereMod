using UnityEngine;

namespace WorldSphereMod.Rig
{
    /// <summary>
    /// 12-bone humanoid skeleton. Phase 6 Step 2: bind-pose only. Animation-driven
    /// rotations land in Step 3.
    ///
    /// Bone order matches the first 12 entries of <see cref="BoneId"/> (Root..RLegLower).
    /// Bind-pose offsets are local to each bone's parent in voxel-space units (Y-up); a
    /// runtime scale multiplier is applied in <see cref="Evaluate"/> to match the actor's
    /// sprite pixels-per-unit. <see cref="SegmentVoxels"/> is the deterministic
    /// pixel-region heuristic from docs/phase6-architecture.md section 3.
    /// </summary>
    public static class HumanoidRig
    {
        public static readonly BoneDefinition[] Bones = new BoneDefinition[12]
        {
            // BoneId.Root      = 0
            new BoneDefinition(-1, new Vector3(0f,  0f,   0f), default(RectInt)),
            // BoneId.Hips      = 1 — child of Root
            new BoneDefinition(0,  new Vector3(0f,  0.5f, 0f), default(RectInt)),
            // BoneId.Spine     = 2 — child of Hips
            new BoneDefinition(1,  new Vector3(0f,  0.3f, 0f), default(RectInt)),
            // BoneId.Head      = 3 — child of Spine
            new BoneDefinition(2,  new Vector3(0f,  0.4f, 0f), default(RectInt)),
            // BoneId.LArmUpper = 4 — child of Spine
            new BoneDefinition(2,  new Vector3(-0.3f,  0.2f, 0f), default(RectInt)),
            // BoneId.LArmLower = 5 — child of LArmUpper
            new BoneDefinition(4,  new Vector3(-0.05f,-0.3f, 0f), default(RectInt)),
            // BoneId.RArmUpper = 6 — child of Spine
            new BoneDefinition(2,  new Vector3( 0.3f,  0.2f, 0f), default(RectInt)),
            // BoneId.RArmLower = 7 — child of RArmUpper
            new BoneDefinition(6,  new Vector3( 0.05f,-0.3f, 0f), default(RectInt)),
            // BoneId.LLegUpper = 8 — child of Hips
            new BoneDefinition(1,  new Vector3(-0.15f,-0.3f, 0f), default(RectInt)),
            // BoneId.LLegLower = 9 — child of LLegUpper
            new BoneDefinition(8,  new Vector3(0f, -0.4f, 0f), default(RectInt)),
            // BoneId.RLegUpper = 10 — child of Hips
            new BoneDefinition(1,  new Vector3( 0.15f,-0.3f, 0f), default(RectInt)),
            // BoneId.RLegLower = 11 — child of RLegUpper
            new BoneDefinition(10, new Vector3(0f, -0.4f, 0f), default(RectInt)),
        };

        /// <summary>
        /// Deterministic per-pixel bone assignment over a sprite's <see cref="Color32"/>
        /// buffer in pixel space (Y=0 bottom). Returns an array of length
        /// <paramref name="spriteW"/>*<paramref name="spriteH"/> indexed as
        /// <c>idx = y*spriteW + x</c>. Pixels outside the alpha bbox or not matched by any
        /// region rule fall through as <see cref="BoneId.Root"/> (sentinel for unused).
        /// </summary>
        public static BoneId[] SegmentVoxels(int spriteW, int spriteH, Color32[] pixels)
        {
            var result = new BoneId[Mathf.Max(0, spriteW * spriteH)];
            if (spriteW <= 0 || spriteH <= 0 || pixels == null || pixels.Length < spriteW * spriteH)
            {
                return result;
            }

            // 1. Alpha-threshold bounding box.
            int x0 = spriteW, x1 = -1, y0 = spriteH, y1 = -1;
            for (int y = 0; y < spriteH; y++)
            {
                int row = y * spriteW;
                for (int x = 0; x < spriteW; x++)
                {
                    if (pixels[row + x].a > 16)
                    {
                        if (x < x0) x0 = x;
                        if (x > x1) x1 = x;
                        if (y < y0) y0 = y;
                        if (y > y1) y1 = y;
                    }
                }
            }
            if (x1 < x0 || y1 < y0) return result;

            float W = x1 - x0;
            float H = y1 - y0;
            // Region thresholds in pixel space (inclusive lower bounds where appropriate).
            float headRowMin   = y1 - 0.20f * H;   // y >= this → head band (top 20%)
            float upper40Min   = y1 - 0.40f * H;   // y >= this → also eligible for desaturated-head pickup
            float leftArmMax   = x0 + 0.15f * W;   // x <  this → left arm column
            float rightArmMin  = x1 - 0.15f * W;   // x >  this → right arm column
            float legRowMax    = y0 + 0.30f * H;   // y <= this → leg band (bottom 30%)
            float colMid       = x0 + 0.50f * W;   // vertical split for leg sides

            for (int y = y0; y <= y1; y++)
            {
                int row = y * spriteW;
                for (int x = x0; x <= x1; x++)
                {
                    int idx = row + x;
                    Color32 c = pixels[idx];
                    if (c.a <= 16) continue;

                    // 3. Head: top 20% of bbox rows.
                    if (y >= headRowMin)
                    {
                        result[idx] = BoneId.Head;
                        continue;
                    }
                    // Plus: any pixel in upper 40% with low saturation (skin-tone proxy).
                    if (y >= upper40Min)
                    {
                        Color.RGBToHSV(new Color(c.r / 255f, c.g / 255f, c.b / 255f), out _, out float s, out _);
                        if (s < 0.25f)
                        {
                            result[idx] = BoneId.Head;
                            continue;
                        }
                    }

                    // 5. Leg band (bottom 30%): split by column midpoint, then upper/lower half.
                    if (y <= legRowMax)
                    {
                        float legMid = y0 + 0.15f * H; // midpoint of leg band (bottom 30% halved)
                        bool isLeftSide = x < colMid;
                        bool isUpperHalf = y > legMid;
                        if (isLeftSide)
                        {
                            result[idx] = isUpperHalf ? BoneId.LLegUpper : BoneId.LLegLower;
                        }
                        else
                        {
                            result[idx] = isUpperHalf ? BoneId.RLegUpper : BoneId.RLegLower;
                        }
                        continue;
                    }

                    // 4. Arm columns: left/right 15% bands above the leg band, not already
                    // assigned to head. Split each band into upper / lower half (non-head rows).
                    // Non-head rows here span [legRowMax+1 .. headRowMin-1].
                    float nonHeadLow  = legRowMax;
                    float nonHeadHigh = headRowMin;
                    float armMid = 0.5f * (nonHeadLow + nonHeadHigh);
                    if (x < leftArmMax)
                    {
                        result[idx] = (y > armMid) ? BoneId.LArmUpper : BoneId.LArmLower;
                        continue;
                    }
                    if (x > rightArmMin)
                    {
                        result[idx] = (y > armMid) ? BoneId.RArmUpper : BoneId.RArmLower;
                        continue;
                    }

                    // 6. Torso residual: inside arm boundary, above leg band, below head.
                    // Upper half → Spine, lower half → Hips.
                    float torsoMid = 0.5f * (nonHeadLow + nonHeadHigh);
                    result[idx] = (y > torsoMid) ? BoneId.Spine : BoneId.Hips;
                }
            }

            return result;
        }

        /// <summary>
        /// Step 8: 2D-to-3D projection. Projects sprite-scale animation signals from
        /// <see cref="AnimationFrameData"/> onto per-bone local TRS matrices per the
        /// mapping table in docs/phase6-architecture.md section 4. Missing/renamed fields
        /// are tolerated by <see cref="SafeReadFloat"/>/<see cref="SafeReadVector2"/> —
        /// the mapping is best-effort, so unknown signals silently degrade to identity
        /// and the rig falls back to bind-pose for those bones.
        /// </summary>
        public static Matrix4x4[] Evaluate(AnimationFrameData? fd, float scale)
        {
            var m = new Matrix4x4[Bones.Length];
            for (int i = 0; i < m.Length; i++) m[i] = Matrix4x4.identity;

            if (fd == null) return m;

            // size_unit drives "lying down" detection — y/x < 0.6 means actor is prone.
            Vector2 size = SafeReadVector2(fd, "size_unit");
            if (size.x > 0.001f && size.y / size.x < 0.6f)
            {
                m[(int)BoneId.Root] = Matrix4x4.Rotate(Quaternion.Euler(-90f, 0, 0));
            }

            // Arm-swing: read fd.arm_swing or similar via reflection if it exists.
            float armSwing = SafeReadFloat(fd, "arm_swing");   // expected range roughly -1..1
            if (Mathf.Abs(armSwing) > 0.01f)
            {
                m[(int)BoneId.LArmUpper] = Matrix4x4.TRS(Bones[(int)BoneId.LArmUpper].BindPoseOffset,
                    Quaternion.Euler(0, armSwing * 45f, 0), Vector3.one);
                m[(int)BoneId.RArmUpper] = Matrix4x4.TRS(Bones[(int)BoneId.RArmUpper].BindPoseOffset,
                    Quaternion.Euler(0, -armSwing * 45f, 0), Vector3.one);
            }

            // Leg stride: similar mapping.
            float stride = SafeReadFloat(fd, "leg_stride");
            if (Mathf.Abs(stride) > 0.01f)
            {
                m[(int)BoneId.LLegUpper] = Matrix4x4.TRS(Bones[(int)BoneId.LLegUpper].BindPoseOffset,
                    Quaternion.Euler(0, stride * 35f, 0), Vector3.one);
                m[(int)BoneId.RLegUpper] = Matrix4x4.TRS(Bones[(int)BoneId.RLegUpper].BindPoseOffset,
                    Quaternion.Euler(0, -stride * 35f, 0), Vector3.one);
                // Lower legs follow at half amplitude.
                m[(int)BoneId.LLegLower] = Matrix4x4.TRS(Bones[(int)BoneId.LLegLower].BindPoseOffset,
                    Quaternion.Euler(0, stride * 17.5f, 0), Vector3.one);
                m[(int)BoneId.RLegLower] = Matrix4x4.TRS(Bones[(int)BoneId.RLegLower].BindPoseOffset,
                    Quaternion.Euler(0, -stride * 17.5f, 0), Vector3.one);
            }

            return m;
        }

        static float SafeReadFloat(object fd, string fieldName)
        {
            try
            {
                var f = fd.GetType().GetField(fieldName);
                if (f != null && (f.FieldType == typeof(float) || f.FieldType == typeof(double)))
                    return System.Convert.ToSingle(f.GetValue(fd));
            }
            catch { }
            return 0f;
        }

        static Vector2 SafeReadVector2(object fd, string fieldName)
        {
            try
            {
                var f = fd.GetType().GetField(fieldName);
                if (f != null && f.FieldType == typeof(Vector2)) return (Vector2)f.GetValue(fd)!;
            }
            catch { }
            return Vector2.zero;
        }
    }
}
