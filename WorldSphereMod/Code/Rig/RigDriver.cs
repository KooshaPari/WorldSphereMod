using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityDebug = UnityEngine.Debug;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.Rig
{
    /// <summary>
    /// Per-actor skeletal driver. Humanoid actors are rendered through a
    /// <see cref="SkinnedMeshRenderer"/> hierarchy so the bone pose can change every
    /// frame without rebuilding the voxel mesh. Other rig types still fall back to
    /// the static voxel path for now.
    /// </summary>
    [Phase(nameof(SavedSettings.SkeletalAnimation))]
    public static class RigDriver
    {
        sealed class ActorRigInstance
        {
            public long ActorKey;
            public Actor Actor;
            public long SourceKey;
            public RigType RigType;
            public GameObject RootObject;
            public Transform[] Bones = System.Array.Empty<Transform>();
            public Quaternion[] LocalRotations = System.Array.Empty<Quaternion>();
            public Mesh Mesh;
            public SkinnedMeshRenderer Renderer;
            public MaterialPropertyBlock PropertyBlock;
            public Vector3 BaseScale;
            public Quaternion BaseRotation;
            public Vector3 BasePosition;
            // Mesh-space (per-sprite) WORLD bind position of each bone joint,
            // derived from the centroid of the voxels that bone skins. The bone
            // Transform hierarchy and the mesh bindposes are both built from these
            // SAME positions, so bind-pose space is consistent by construction —
            // this is the fix for the unit-vs-mesh-space mismatch that shredded
            // actors when bones rotated.
            public Vector3[] BoneBindWorld = System.Array.Empty<Vector3>();
            public Color Tint;
            public float PhaseSeed;
            public int LastSeenFrame;
        }

        /// <summary>
        /// Master gate for the skeletal skinning path.
        ///
        /// HISTORY: the original 12-bone humanoid rig used HARDCODED unit-space
        /// bind offsets (HumanoidRig.Bones: 0.5f, 0.3f, ...) while the voxel mesh
        /// is built in per-sprite world units (vertex = (pixel - pivot) /
        /// sprite.pixelsPerUnit, see SpriteVoxelizer.BuildPerTexel). Those spaces
        /// did NOT coincide — the skeleton joints were not at the centroids of the
        /// voxels they skinned — so the instant a bone rotated, each rigidly
        /// single-weighted voxel swung about the wrong pivot and the actor
        /// shredded. That was the "rigging is a mess" the user reported.
        ///
        /// FIX (this pass): both the bone Transform hierarchy AND the mesh
        /// bindposes are now derived from the SAME per-sprite mesh data:
        /// each bone's world bind position is the CENTROID of the voxels assigned
        /// to it (BuildMeshAlignedSkin). Because the bindpose and the rest bone
        /// transform are built from identical centroids, the rest skin matrix is
        /// exactly identity and bind-pose space is consistent by construction — no
        /// unit-vs-mesh mismatch is possible. Vertices are BLEND-weighted to their
        /// own bone plus its parent (inverse-distance, normalized, &lt;=4
        /// influences) so bone rotation no longer tears the seam between regions.
        /// The hardcoded HumanoidRig.Bones offsets are now used ONLY as a
        /// fallback direction for bones whose sprite has zero assigned voxels.
        /// </summary>
        const bool kSkinnedRigProductionReady = true;

        static readonly Dictionary<long, ActorRigInstance> _actorRigs = new Dictionary<long, ActorRigInstance>();
        static readonly List<long> _scratchRemove = new List<long>();
        static Transform? _root;
        static Material? _skinnedMaterial;
        const int kPerfLogIntervalFrames = 60;
        static int _perfFrameCounter;
        static double _perfWindowMs;

        public static bool SubmitSkinnedActor(
            Actor a, Vector3 pos, Quaternion rot, Vector3 scl, Color tint,
            RigType rigType)
        {
            if (a == null || a.asset == null)
            {
                return false;
            }

            Sprite? sp = a.calculateMainSprite();
            if (sp == null)
            {
                return false;
            }

            SkinnedVoxelMesh svm = RigCache.GetOrBuild(sp, rigType);
            if (svm.BaseMesh == null || svm.BaseMesh.vertexCount == 0)
            {
                return false;
            }

            // STABLE-OFF GUARD: until the humanoid rig builds mesh-aligned bind
            // poses (see kSkinnedRigProductionReady), route EVERY actor through
            // the static voxel mesh — which renders correctly — instead of the
            // distorting skinned path. Returning true (not false) is deliberate:
            // the caller treats a false return as "invisible this frame", so we
            // submit the static mesh ourselves and report success so the actor
            // stays visible and correct.
            if (!kSkinnedRigProductionReady || rigType != RigType.Humanoid)
            {
                return VoxelRender.Submit(svm.BaseMesh, Matrix4x4.TRS(pos, rot, scl), tint);
            }

            if (!EnsureHumanoidRigMaterial())
            {
                return VoxelRender.Submit(svm.BaseMesh, Matrix4x4.TRS(pos, rot, scl), tint);
            }

            ActorRigInstance? rig = GetOrCreateRig(a, sp, rigType, svm);
            if (rig == null || rig.Mesh == null || rig.Renderer == null || rig.RootObject == null)
            {
                return VoxelRender.Submit(svm.BaseMesh, Matrix4x4.TRS(pos, rot, scl), tint);
            }

            rig.LastSeenFrame = Time.frameCount;
            UpdateRigTransform(rig, pos, rot, scl);
            UpdateRigPose(rig, a);
            ApplyTint(rig, tint);
            return true;
        }

        /// <summary>
        /// Called once per frame from the render driver. Visible actors are refreshed
        /// from their current animation frame data; actors that were not submitted this
        /// frame are reaped so the skinned rig list stays in sync with visibility.
        /// </summary>
        public static void Update()
        {
            double frameElapsedMs = 0.0;
            long startTimestamp = Stopwatch.GetTimestamp();

            if (_actorRigs.Count == 0)
            {
                frameElapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
                MaybeLogPerfStats(_actorRigs.Count, frameElapsedMs);
                return;
            }

            int currentFrame = Time.frameCount;
            _scratchRemove.Clear();

            foreach (var kv in _actorRigs)
            {
                ActorRigInstance rig = kv.Value;
                if (rig == null || rig.Actor == null || rig.RootObject == null || rig.Mesh == null || rig.Renderer == null)
                {
                    _scratchRemove.Add(kv.Key);
                    continue;
                }

                if (rig.LastSeenFrame != currentFrame)
                {
                    _scratchRemove.Add(kv.Key);
                    continue;
                }

                UpdateRigPose(rig, rig.Actor);
                ApplyTint(rig, rig.Tint);
            }

            for (int i = 0; i < _scratchRemove.Count; i++)
            {
                RemoveRig(_scratchRemove[i]);
            }

            if (RigGpuSkinning.IsEnabled(Core.savedSettings))
            {
                RigGpuSkinning.TickFrame();
            }

            frameElapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            MaybeLogPerfStats(_actorRigs.Count, frameElapsedMs);
        }

        static void MaybeLogPerfStats(int activeRigCount, double frameElapsedMs)
        {
            _perfFrameCounter++;
            _perfWindowMs += frameElapsedMs;
            if (_perfFrameCounter < kPerfLogIntervalFrames)
            {
                return;
            }

            double avgFrameMs = _perfWindowMs / _perfFrameCounter;
            _perfFrameCounter = 0;
            _perfWindowMs = 0.0;
            if (Core.savedSettings != null && Core.savedSettings.ProfilerDump)
                UnityDebug.Log($"[WSM3D][Perf] RigDriver.Update avg60FrameMs={avgFrameMs:F3}ms frameSkinnedActors={activeRigCount}");
        }

        public static void Clear()
        {
            foreach (var key in new List<long>(_actorRigs.Keys))
            {
                RemoveRig(key);
            }

            if (_skinnedMaterial != null)
            {
                Object.Destroy(_skinnedMaterial);
                _skinnedMaterial = null;
            }

            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
                _root = null;
            }

            RigGpuSkinning.Clear();

            _actorRigs.Clear();
            _scratchRemove.Clear();
        }

        static ActorRigInstance? GetOrCreateRig(Actor actor, Sprite sprite, RigType rigType, SkinnedVoxelMesh svm)
        {
            long actorKey = RuntimeHelpers.GetHashCode(actor);
            long sourceKey = ((long)(uint)sprite.GetInstanceID() << 8) | (byte)rigType;

            if (_actorRigs.TryGetValue(actorKey, out ActorRigInstance existing) &&
                existing != null &&
                existing.SourceKey == sourceKey &&
                existing.RootObject != null &&
                existing.Mesh != null &&
                existing.Renderer != null)
            {
                existing.Actor = actor;
                existing.LastSeenFrame = Time.frameCount;
                return existing;
            }

            if (existing != null)
            {
                RemoveRig(actorKey);
            }

            Transform root = EnsureRoot();
            if (root == null)
            {
                return null;
            }

            Vector3[] boneBindWorld;
            Mesh mesh = BuildHumanoidMeshClone(svm, out boneBindWorld);
            if (mesh == null)
            {
                return null;
            }

            GameObject go = new GameObject("rig:" + actorKey);
            go.transform.SetParent(root, worldPositionStays: false);
            SkinnedMeshRenderer renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = GetOrCreateSkinnedMaterial();
            renderer.updateWhenOffscreen = true;
            renderer.receiveShadows = false;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var bones = new Transform[HumanoidRig.Bones.Length];
            bones[0] = go.transform;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            // Build the bone hierarchy from the SAME mesh-space centroids used for
            // the mesh bindposes. localPosition = childCentroid - parentCentroid so
            // every joint's WORLD rest position equals boneBindWorld[i]; the rest
            // skin matrix is then exactly identity (no shred on first rotation).
            for (int i = 1; i < HumanoidRig.Bones.Length; i++)
            {
                BoneDefinition boneDef = HumanoidRig.Bones[i];
                GameObject boneGo = new GameObject(((BoneId)i).ToString());
                Transform bone = boneGo.transform;
                bone.SetParent(bones[boneDef.ParentIndex], worldPositionStays: false);
                int parent = boneDef.ParentIndex;
                Vector3 parentWorld = (parent >= 0 && parent < boneBindWorld.Length) ? boneBindWorld[parent] : Vector3.zero;
                bone.localPosition = boneBindWorld[i] - parentWorld;
                bone.localRotation = Quaternion.identity;
                bone.localScale = Vector3.one;
                bones[i] = bone;
            }

            renderer.bones = bones;
            renderer.rootBone = go.transform;
            Bounds localBounds = mesh.bounds;
            localBounds.Expand(1.5f);
            renderer.localBounds = localBounds;

            var instance = new ActorRigInstance
            {
                ActorKey = actorKey,
                Actor = actor,
                SourceKey = sourceKey,
                RigType = rigType,
                RootObject = go,
                Bones = bones,
                BoneBindWorld = boneBindWorld,
                LocalRotations = new Quaternion[HumanoidRig.Bones.Length],
                Mesh = mesh,
                Renderer = renderer,
                PropertyBlock = new MaterialPropertyBlock(),
                PhaseSeed = ((actorKey & 0xffff) / 65535f) * Mathf.PI * 2f,
                LastSeenFrame = Time.frameCount,
            };

            _actorRigs[actorKey] = instance;
            UpdateRigTransform(instance, Vector3.zero, Quaternion.identity, Vector3.one);
            UpdateRigPose(instance, actor);
            ApplyTint(instance, Color.white);
            return instance;
        }

        static Mesh BuildHumanoidMeshClone(SkinnedVoxelMesh svm, out Vector3[] boneBindWorld)
        {
            boneBindWorld = System.Array.Empty<Vector3>();
            if (svm.BaseMesh == null)
            {
                return null;
            }

            Mesh mesh = Object.Instantiate(svm.BaseMesh);
            mesh.name = $"{svm.BaseMesh.name}:skinned";

            int vc = mesh.vertexCount;
            Vector3[] verts = mesh.vertices;
            byte[] boneIndices = svm.BoneIndices ?? System.Array.Empty<byte>();
            int boneCount = HumanoidRig.Bones.Length;

            // --- 1. Per-bone voxel centroids in MESH space ---------------------
            // The bone joint for region B is placed at the mean position of the
            // vertices assigned to B. That is the pivot voxels actually rotate
            // about, so rotation no longer swings them off to a foreign origin.
            var sum = new Vector3[boneCount];
            var cnt = new int[boneCount];
            var clampedBone = new int[vc];
            for (int i = 0; i < vc; i++)
            {
                int b = i < boneIndices.Length ? boneIndices[i] : (int)BoneId.Spine;
                if (b < 0 || b >= boneCount) b = (int)BoneId.Spine;
                clampedBone[i] = b;
                sum[b] += verts[i];
                cnt[b]++;
            }

            boneBindWorld = ComputeBoneBindWorld(sum, cnt, boneCount);

            // --- 2. Mesh bindposes from the SAME centroids ---------------------
            // Rest bone world matrix is a pure translation to its centroid (rest
            // rotation is identity for this rig), so the bindpose is the inverse
            // translation. world[i]*bindpose[i] == identity at rest → zero shred.
            var bindPoses = new Matrix4x4[boneCount];
            for (int b = 0; b < boneCount; b++)
            {
                bindPoses[b] = Matrix4x4.Translate(-boneBindWorld[b]);
            }
            mesh.bindposes = bindPoses;

            // --- 3. Blended weights: own bone + parent, inverse-distance -------
            // Single-bone rigid weights leave a hard seam that tears when adjacent
            // bones rotate apart. Splitting each vertex between its own bone and
            // its parent (capped <=4 influences, normalized to 1) lets the seam
            // bend smoothly instead of cracking.
            var weights = new BoneWeight[vc];
            for (int i = 0; i < vc; i++)
            {
                int b = clampedBone[i];
                int parent = HumanoidRig.Bones[b].ParentIndex;
                if (parent < 0 || parent >= boneCount || parent == b)
                {
                    weights[i].boneIndex0 = b;
                    weights[i].weight0 = 1f;
                    continue;
                }

                // Inverse-distance blend toward the two joints. eps avoids div0 and
                // keeps a vertex sitting exactly on a joint fully weighted to it.
                const float eps = 1e-4f;
                float dOwn = (verts[i] - boneBindWorld[b]).magnitude + eps;
                float dPar = (verts[i] - boneBindWorld[parent]).magnitude + eps;
                float wOwn = 1f / dOwn;
                float wPar = 1f / dPar;
                // Bias toward the vertex's own segment so regions stay distinct
                // (a pure inverse-distance blend over-bleeds across long bones).
                wOwn *= 2f;
                float inv = 1f / (wOwn + wPar);
                weights[i].boneIndex0 = b;
                weights[i].weight0 = wOwn * inv;
                weights[i].boneIndex1 = parent;
                weights[i].weight1 = wPar * inv;
            }

            mesh.boneWeights = weights;
            mesh.MarkDynamic();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Resolve each bone's mesh-space rest joint position. Bones that skin at
        /// least one voxel use that voxel cluster's centroid. Bones with no voxels
        /// (sprite never assigned them) fall back to parentCentroid + the
        /// hardcoded HumanoidRig unit-space offset so the hierarchy stays
        /// connected — these never skin any vertex, so their exact position only
        /// matters for keeping child joints in a sane place.
        /// </summary>
        static Vector3[] ComputeBoneBindWorld(Vector3[] sum, int[] cnt, int boneCount)
        {
            var bind = new Vector3[boneCount];
            var resolved = new bool[boneCount];
            // Bones are declared parent-before-child (parent index < own index),
            // so a single forward pass resolves parents before their children.
            for (int b = 0; b < boneCount; b++)
            {
                if (cnt[b] > 0)
                {
                    bind[b] = sum[b] / cnt[b];
                    resolved[b] = true;
                    continue;
                }

                int parent = HumanoidRig.Bones[b].ParentIndex;
                Vector3 parentPos = (parent >= 0 && parent < boneCount) ? bind[parent] : Vector3.zero;
                bind[b] = parentPos + HumanoidRig.Bones[b].BindPoseOffset;
                resolved[b] = true;
            }

            return bind;
        }

        static void UpdateRigTransform(ActorRigInstance rig, Vector3 pos, Quaternion rot, Vector3 scl)
        {
            if (rig == null || rig.RootObject == null)
            {
                return;
            }

            bool flip = scl.x < 0f;
            Vector3 worldScale = new Vector3(Mathf.Abs(scl.x), Mathf.Abs(scl.y), Mathf.Abs(scl.z));
            Quaternion worldRot = rot * (flip ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity);

            rig.BasePosition = pos;
            rig.BaseRotation = worldRot;
            rig.BaseScale = worldScale;

            Transform root = rig.RootObject.transform;
            root.position = pos;
            root.localScale = worldScale;
        }

        static void UpdateRigPose(ActorRigInstance rig, Actor actor)
        {
            if (rig == null || rig.Bones == null || rig.Bones.Length < HumanoidRig.Bones.Length)
            {
                return;
            }
            // Pause-aware: when game is paused (Time.timeScale=0), keep current
            // pose frozen instead of advancing the walk-phase from wall-clock
            // Time.time. User flagged that limbs continued to sway during pause.
            if (Time.timeScale <= 0f)
            {
                return;
            }

            float walkPhase = Time.time + rig.PhaseSeed;
            float walkAmount = 0f;

            try
            {
                if (actor != null)
                {
                    Vector3 current = actor.current_position;
                    Vector3 next = actor.next_step_position;
                    // Cylindrical X-wrap (CurrentShape == 0): raw Vector3.Distance
                    // explodes near the seam when current.x and next.x sit on
                    // opposite sides of the world. Tools.MathStuff.Dist wraps X.
                    walkAmount = Mathf.Clamp01(Tools.MathStuff.Dist(current.x, next.x, current.y, next.y));
                }
            }
            catch
            {
                walkAmount = 0f;
            }

            if (walkAmount > 0f)
            {
                walkPhase *= Mathf.Lerp(1f, 2.5f, walkAmount);
            }

            if (rig.LocalRotations == null || rig.LocalRotations.Length != HumanoidRig.Bones.Length)
            {
                rig.LocalRotations = new Quaternion[HumanoidRig.Bones.Length];
            }

            HumanoidRig.FillLocalRotations(actor, walkPhase, rig.LocalRotations);

            Transform root = rig.RootObject.transform;
            root.position = rig.BasePosition;
            root.rotation = rig.BaseRotation * rig.LocalRotations[0];
            root.localScale = rig.BaseScale;

            for (int i = 1; i < rig.Bones.Length; i++)
            {
                if (rig.Bones[i] == null) continue;
                rig.Bones[i].localRotation = rig.LocalRotations[i];
            }
        }

        static void ApplyTint(ActorRigInstance rig, Color tint)
        {
            if (rig == null || rig.Renderer == null)
            {
                return;
            }

            rig.Tint = tint;
            MaterialPropertyBlock block = rig.PropertyBlock ?? new MaterialPropertyBlock();
            block.Clear();
            block.SetColor("_BaseColor", tint);
            block.SetColor("_Color", tint);
            rig.Renderer.SetPropertyBlock(block);
            rig.PropertyBlock = block;
        }

        static Transform EnsureRoot()
        {
            if (_root != null)
            {
                return _root;
            }

            if (Mod.Object == null)
            {
                return null;
            }

            GameObject go = new GameObject("WSM3D.Rigs");
            _root = go.transform;
            _root.SetParent(Mod.Object.transform, worldPositionStays: false);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;
            return _root;
        }

        static bool EnsureHumanoidRigMaterial()
        {
            return GetOrCreateSkinnedMaterial() != null;
        }

        /// <summary>
        /// SkinnedMeshRenderer cannot use the instanced voxel batcher material.
        /// GPU instancing on a SkinnedMeshRenderer causes shader variant lookup
        /// failure (magenta) because Unity has no instanced skinning variant for
        /// Standard or OpaqueVertexColor. We clone the resolved voxel material
        /// once and disable instancing so the shader compiles for the skinned
        /// rendering path.
        /// </summary>
        static Material? GetOrCreateSkinnedMaterial()
        {
            if (_skinnedMaterial != null)
            {
                return _skinnedMaterial;
            }

            // Resolve via LoadedShaders first (OpaqueVertexColor from bundle).
            Shader? shader = null;
            if (Core.Sphere.LoadedShaders.TryGetValue("OpaqueVertexColor", out var bundled) && bundled != null)
            {
                shader = bundled;
            }
            if (shader == null)
            {
                shader = Shader.Find("WSM3D/OpaqueVertexColor");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                return null;
            }

            _skinnedMaterial = new Material(shader)
            {
                name = "WSM3D.Rig.Skinned",
                enableInstancing = false,
            };
            _skinnedMaterial.SetInt("_Cull", 0);
            _skinnedMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 1;
            _skinnedMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
            _skinnedMaterial.SetColor("_Color", Color.white);
            _skinnedMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
            _skinnedMaterial.SetColor("_BaseColor", Color.white);
            _skinnedMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _skinnedMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            _skinnedMaterial.SetInt("_ZWrite", 1);
            _skinnedMaterial.DisableKeyword("_ALPHABLEND_ON");
            _skinnedMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            _skinnedMaterial.DisableKeyword("_ALPHATEST_ON");
            _skinnedMaterial.SetFloat("_Cutoff", 0.0f);

            if (shader.name == "Standard")
            {
                _skinnedMaterial.EnableKeyword("_EMISSION");
                float emissionMultiplier = Core.savedSettings != null ? Core.savedSettings.ImpostorEmissionMultiplier : 1.5f;
                _skinnedMaterial.SetColor("_EmissionColor", new Color(emissionMultiplier, emissionMultiplier, emissionMultiplier, 1f));
                _skinnedMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            UnityDebug.Log($"[WSM3D] RigDriver skinned material created: shader='{shader.name}', instancing=false");
            return _skinnedMaterial;
        }

        static void RemoveRig(long actorKey)
        {
            if (!_actorRigs.TryGetValue(actorKey, out ActorRigInstance rig))
            {
                return;
            }

            if (rig != null)
            {
                if (rig.Mesh != null)
                {
                    Object.Destroy(rig.Mesh);
                }

                if (rig.RootObject != null)
                {
                    Object.Destroy(rig.RootObject);
                }
            }

            _actorRigs.Remove(actorKey);
        }
    }
}
