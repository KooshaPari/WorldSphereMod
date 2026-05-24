using UnityEngine;

namespace WorldSphereMod.Rig
{
    /// <summary>
    /// 12-bone humanoid skeleton. The same deterministic pixel-space segmentation used
    /// for cache-time bone assignment is reused at animation time through the static
    /// skinning matrices returned by <see cref="Evaluate"/>.
    /// </summary>
    public static class HumanoidRig
    {
        public static readonly BoneDefinition[] Bones = new BoneDefinition[12]
        {
            new BoneDefinition(-1, new Vector3(0f,  0f,   0f), default(RectInt)),
            new BoneDefinition(0,  new Vector3(0f,  0.5f, 0f), default(RectInt)),
            new BoneDefinition(1,  new Vector3(0f,  0.3f, 0f), default(RectInt)),
            new BoneDefinition(2,  new Vector3(0f,  0.4f, 0f), default(RectInt)),
            new BoneDefinition(2,  new Vector3(-0.3f,  0.2f, 0f), default(RectInt)),
            new BoneDefinition(4,  new Vector3(-0.05f,-0.3f, 0f), default(RectInt)),
            new BoneDefinition(2,  new Vector3( 0.3f,  0.2f, 0f), default(RectInt)),
            new BoneDefinition(6,  new Vector3( 0.05f,-0.3f, 0f), default(RectInt)),
            new BoneDefinition(1,  new Vector3(-0.15f,-0.3f, 0f), default(RectInt)),
            new BoneDefinition(8,  new Vector3(0f, -0.4f, 0f), default(RectInt)),
            new BoneDefinition(1,  new Vector3( 0.15f,-0.3f, 0f), default(RectInt)),
            new BoneDefinition(10, new Vector3(0f, -0.4f, 0f), default(RectInt)),
        };

        static readonly Matrix4x4[] _restWorld;
        static readonly Matrix4x4[] _restWorldInverse;

        static HumanoidRig()
        {
            _restWorld = BuildHierarchy(identityPose: true, scale: 1f, prone: false, armSwing: 0f, legStride: 0f, headPitch: 0f, attackSwing: 0f);
            _restWorldInverse = new Matrix4x4[Bones.Length];
            for (int i = 0; i < Bones.Length; i++)
            {
                _restWorldInverse[i] = _restWorld[i].inverse;
            }
        }

        /// <summary>
        /// Deterministic per-pixel bone assignment over a sprite's <see cref="Color32"/>
        /// buffer in pixel space (Y=0 bottom).
        /// </summary>
        public static BoneId[] SegmentVoxels(int spriteW, int spriteH, Color32[] pixels)
        {
            var result = new BoneId[Mathf.Max(0, spriteW * spriteH)];
            if (spriteW <= 0 || spriteH <= 0 || pixels == null || pixels.Length < spriteW * spriteH)
            {
                return result;
            }

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
            float headRowMin = y1 - 0.20f * H;
            float upper40Min = y1 - 0.40f * H;
            float leftArmMax = x0 + 0.15f * W;
            float rightArmMin = x1 - 0.15f * W;
            float legRowMax = y0 + 0.30f * H;
            float colMid = x0 + 0.50f * W;

            for (int y = y0; y <= y1; y++)
            {
                int row = y * spriteW;
                for (int x = x0; x <= x1; x++)
                {
                    int idx = row + x;
                    Color32 c = pixels[idx];
                    if (c.a <= 16) continue;

                    if (y >= headRowMin)
                    {
                        result[idx] = BoneId.Head;
                        continue;
                    }
                    if (y >= upper40Min)
                    {
                        Color.RGBToHSV(new Color(c.r / 255f, c.g / 255f, c.b / 255f), out _, out float s, out _);
                        if (s < 0.25f)
                        {
                            result[idx] = BoneId.Head;
                            continue;
                        }
                    }

                    if (y <= legRowMax)
                    {
                        float legMid = y0 + 0.15f * H;
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

                    float nonHeadLow = legRowMax;
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

                    result[idx] = (y > armMid) ? BoneId.Spine : BoneId.Hips;
                }
            }

            return result;
        }

        /// <summary>
        /// Projects WorldBox's 2D animation state into bone skinning matrices. The
        /// returned matrices are already in sprite-local space and can be applied
        /// directly to voxel vertices.
        /// </summary>
        public static Matrix4x4[] Evaluate(AnimationFrameData? fd, float scale)
        {
            float armSwing = ReadFloat(fd, 0f, "arm_swing", "armSwing", "swing");
            float legStride = ReadFloat(fd, 0f, "leg_stride", "legStride", "stride");
            float headPitch = ReadFloat(fd, 0f, "head_pitch", "headPitch", "neck_pitch", "neckPitch");
            float attackSwing = ReadFloat(fd, 0f, "attack_swing", "attackSwing", "attack", "attack_progress");
            Vector2 size = ReadVector2(fd, "size_unit");
            bool prone = size.x > 0.001f && size.y / size.x < 0.6f;

            // FR-WSM-008 dragonfly fix: skeleton hierarchy is ALWAYS at scale=1.
            // _restWorldInverse was baked at scale=1 in the static ctor — passing a
            // different scale here makes the skin matrix world[i] * restInverse[i]
            // accumulate an N-times stretch since rest is at unit length but
            // current pose has scale-multiplied bind offsets. External mesh transform
            // applies render scale; bones stay unit-length.
            return BuildHierarchy(false, 1f, prone, armSwing, legStride, headPitch, attackSwing);
        }

        public static Matrix4x4[] GetBindPoses()
        {
            return (Matrix4x4[])_restWorldInverse.Clone();
        }

        /// <summary>
        /// Fill the local bone rotations for a live skinned mesh hierarchy. When the
        /// engine frame data does not expose a usable walk cycle, a time-based fallback
        /// keeps the actor visibly animated while walking.
        /// </summary>
        public static void FillLocalRotations(Actor actor, float walkPhase, Quaternion[] localRotations)
        {
            if (localRotations == null || localRotations.Length < Bones.Length)
            {
                return;
            }

            AnimationFrameData? fd = null;
            try
            {
                fd = actor != null ? actor.getAnimationFrameData() : null;
            }
            catch
            {
                fd = null;
            }

            float armSwing = ReadFloat(fd, 0f, "arm_swing", "armSwing", "swing");
            float legStride = ReadFloat(fd, 0f, "leg_stride", "legStride", "stride");
            float headPitch = ReadFloat(fd, 0f, "head_pitch", "headPitch", "neck_pitch", "neckPitch");
            float attackSwing = ReadFloat(fd, 0f, "attack_swing", "attackSwing", "attack", "attack_progress");
            Vector2 size = ReadVector2(fd, "size_unit");
            bool prone = size.x > 0.001f && size.y / size.x < 0.6f;

            float walkAmount = 0f;
            try
            {
                if (actor != null)
                {
                    Vector3 current = actor.current_position;
                    Vector3 next = actor.next_step_position;
                    walkAmount = Mathf.Clamp01(Vector3.Distance(current, next));
                }
            }
            catch
            {
                walkAmount = 0f;
            }

            if (walkAmount > 0f)
            {
                float cycle = Mathf.Sin(walkPhase * Mathf.PI * 2f) * walkAmount;
                if (Mathf.Abs(armSwing) < 0.001f) armSwing = -cycle;
                if (Mathf.Abs(legStride) < 0.001f) legStride = cycle;
                if (Mathf.Abs(headPitch) < 0.001f) headPitch = Mathf.Sin(walkPhase * Mathf.PI) * 0.12f * walkAmount;
            }

            for (int i = 0; i < Bones.Length; i++)
            {
                localRotations[i] = GetBoneRotation((BoneId)i, prone, armSwing, legStride, headPitch, attackSwing);
            }
        }

        static Matrix4x4[] BuildHierarchy(bool identityPose, float scale, bool prone, float armSwing, float legStride, float headPitch, float attackSwing)
        {
            var local = new Matrix4x4[Bones.Length];
            var world = new Matrix4x4[Bones.Length];

            for (int i = 0; i < Bones.Length; i++)
            {
                Quaternion rot = GetBoneRotation((BoneId)i, prone, armSwing, legStride, headPitch, attackSwing);

                Vector3 bind = Bones[i].BindPoseOffset * scale;
                local[i] = Matrix4x4.TRS(bind, rot, Vector3.one);
                int parent = Bones[i].ParentIndex;
                world[i] = parent < 0 ? local[i] : world[parent] * local[i];
            }

            if (identityPose)
            {
                return world;
            }

            var skin = new Matrix4x4[Bones.Length];
            for (int i = 0; i < Bones.Length; i++)
            {
                skin[i] = world[i] * _restWorldInverse[i];
            }
            return skin;
        }

        static Quaternion GetBoneRotation(BoneId bone, bool prone, float armSwing, float legStride, float headPitch, float attackSwing)
        {
            Quaternion rot = Quaternion.identity;
            switch (bone)
            {
                case BoneId.Root:
                    if (prone)
                    {
                        rot = Quaternion.Euler(-90f, 0f, 0f);
                    }
                    break;
                case BoneId.Spine:
                    rot = Quaternion.Euler(0f, 0f, Mathf.Clamp(attackSwing * 3f, -10f, 10f));
                    break;
                case BoneId.Head:
                    rot = Quaternion.Euler(Mathf.Clamp(headPitch * 20f + attackSwing * 8f, -25f, 25f), 0f, 0f);
                    break;
                case BoneId.LArmUpper:
                    rot = Quaternion.Euler(0f, Mathf.Clamp((armSwing * 55f) + (attackSwing * 25f), -90f, 90f), 0f);
                    break;
                case BoneId.RArmUpper:
                    rot = Quaternion.Euler(0f, Mathf.Clamp((-armSwing * 55f) + (attackSwing * 25f), -90f, 90f), 0f);
                    break;
                case BoneId.LArmLower:
                    rot = Quaternion.Euler(0f, Mathf.Clamp((armSwing * 18f) + (attackSwing * 10f), -65f, 65f), 0f);
                    break;
                case BoneId.RArmLower:
                    rot = Quaternion.Euler(0f, Mathf.Clamp((-armSwing * 18f) + (attackSwing * 10f), -65f, 65f), 0f);
                    break;
                case BoneId.LLegUpper:
                    rot = Quaternion.Euler(0f, Mathf.Clamp(legStride * 35f, -55f, 55f), 0f);
                    break;
                case BoneId.RLegUpper:
                    rot = Quaternion.Euler(0f, Mathf.Clamp(-legStride * 35f, -55f, 55f), 0f);
                    break;
                case BoneId.LLegLower:
                    rot = Quaternion.Euler(0f, Mathf.Clamp(legStride * 17.5f, -35f, 35f), 0f);
                    break;
                case BoneId.RLegLower:
                    rot = Quaternion.Euler(0f, Mathf.Clamp(-legStride * 17.5f, -35f, 35f), 0f);
                    break;
            }

            return rot;
        }

        static float ReadFloat(object? fd, float fallback, params string[] names)
        {
            if (fd == null)
            {
                return fallback;
            }

            foreach (string name in names)
            {
                try
                {
                    var type = fd.GetType();
                    var field = type.GetField(name);
                    if (field != null && (field.FieldType == typeof(float) || field.FieldType == typeof(double)))
                    {
                        return System.Convert.ToSingle(field.GetValue(fd));
                    }

                    var prop = type.GetProperty(name);
                    if (prop != null && (prop.PropertyType == typeof(float) || prop.PropertyType == typeof(double)))
                    {
                        return System.Convert.ToSingle(prop.GetValue(fd));
                    }
                }
                catch
                {
                }
            }

            return fallback;
        }

        static Vector2 ReadVector2(object? fd, string fieldName)
        {
            if (fd == null)
            {
                return Vector2.zero;
            }

            try
            {
                var type = fd.GetType();
                var field = type.GetField(fieldName);
                if (field != null && field.FieldType == typeof(Vector2))
                {
                    return (Vector2)field.GetValue(fd)!;
                }

                var prop = type.GetProperty(fieldName);
                if (prop != null && prop.PropertyType == typeof(Vector2))
                {
                    return (Vector2)prop.GetValue(fd)!;
                }
            }
            catch
            {
            }

            return Vector2.zero;
        }
    }
}
