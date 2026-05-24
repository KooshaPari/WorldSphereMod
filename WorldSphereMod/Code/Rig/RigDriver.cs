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
            public Color Tint;
            public float PhaseSeed;
            public int LastSeenFrame;
        }

        static readonly Dictionary<long, ActorRigInstance> _actorRigs = new Dictionary<long, ActorRigInstance>();
        static readonly List<long> _scratchRemove = new List<long>();
        static Transform? _root;
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

            if (rigType != RigType.Humanoid)
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
            UnityDebug.Log($"[WSM3D][Perf] RigDriver.Update avg60FrameMs={avgFrameMs:F3}ms frameSkinnedActors={activeRigCount}");
        }

        public static void Clear()
        {
            foreach (var key in new List<long>(_actorRigs.Keys))
            {
                RemoveRig(key);
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

            Mesh mesh = BuildHumanoidMeshClone(svm);
            if (mesh == null)
            {
                return null;
            }

            GameObject go = new GameObject("rig:" + actorKey);
            go.transform.SetParent(root, worldPositionStays: false);
            SkinnedMeshRenderer renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = VoxelRender.GetResolvedMaterial();
            renderer.updateWhenOffscreen = true;
            renderer.receiveShadows = false;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var bones = new Transform[HumanoidRig.Bones.Length];
            bones[0] = go.transform;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            for (int i = 1; i < HumanoidRig.Bones.Length; i++)
            {
                BoneDefinition boneDef = HumanoidRig.Bones[i];
                GameObject boneGo = new GameObject(((BoneId)i).ToString());
                Transform bone = boneGo.transform;
                bone.SetParent(bones[boneDef.ParentIndex], worldPositionStays: false);
                bone.localPosition = boneDef.BindPoseOffset;
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

        static Mesh BuildHumanoidMeshClone(SkinnedVoxelMesh svm)
        {
            if (svm.BaseMesh == null)
            {
                return null;
            }

            Mesh mesh = Object.Instantiate(svm.BaseMesh);
            mesh.name = $"{svm.BaseMesh.name}:skinned";

            Matrix4x4[] bindPoses = HumanoidRig.GetBindPoses();
            if (bindPoses != null && bindPoses.Length == HumanoidRig.Bones.Length)
            {
                mesh.bindposes = bindPoses;
            }

            byte[] boneIndices = svm.BoneIndices ?? System.Array.Empty<byte>();
            var weights = new BoneWeight[mesh.vertexCount];
            for (int i = 0; i < weights.Length; i++)
            {
                int boneIndex = i < boneIndices.Length ? boneIndices[i] : (int)BoneId.Spine;
                if (boneIndex < 0 || boneIndex >= HumanoidRig.Bones.Length)
                {
                    boneIndex = (int)BoneId.Spine;
                }

                weights[i].boneIndex0 = boneIndex;
                weights[i].weight0 = 1f;
            }

            mesh.boneWeights = weights;
            mesh.MarkDynamic();
            mesh.RecalculateBounds();
            return mesh;
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
                    walkAmount = Mathf.Clamp01(Vector3.Distance(current, next));
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
            return VoxelRender.GetResolvedMaterial() != null || VoxelRender.EnsureMaterial();
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
