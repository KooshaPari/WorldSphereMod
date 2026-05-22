using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using WorldSphereMod;
using WorldSphereMod.Rig;
using Debug = UnityEngine.Debug;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// LRU cache of voxelized meshes keyed by <see cref="Sprite.GetInstanceID"/>. Survives
    /// world rebuilds (entries live in the static dictionary), but evicts on capacity.
    ///
    /// The cache is the only allocation site for voxel meshes — every render pass that
    /// previously assigned a <see cref="Sprite"/> to a quad should call <see cref="Get"/>
    /// instead and feed the result to <see cref="MeshInstanceBatcher"/>.
    /// </summary>
    public static class VoxelMeshCache
    {
        public const int SampleLimit = 100;
        public const int MAX_ENTRIES = 512;
        public static int Capacity => MAX_ENTRIES;

        public sealed class MeshBoundsSnapshot
        {
            public Vector3 min;
            public Vector3 max;
        }

        public sealed class MeshInvariantsSnapshot
        {
            public int distinctTriVerts;
            public bool maxTriIndexLessThanVerts;
            public int maxTriIndex;
        }

        public sealed class MeshSnapshot
        {
            public int spriteId;
            public string spriteName;
            public string meshName;
            public int vertexCount;
            public int triangleCount;
            public MeshBoundsSnapshot bounds;
            public List<Vector3> vertices = new List<Vector3>();
            public List<int> triangles = new List<int>();
            public List<Color32> colors = new List<Color32>();
            public MeshInvariantsSnapshot invariants;
        }

        struct Entry
        {
            public Mesh Mesh;
            public MeshSnapshot Snapshot;
            public ulong LastFrame;
        }

        struct BuildRequest
        {
            public Sprite Sprite;
            public int Key;
            public int Depth;
        }

        struct BuildCompletion
        {
            public int Key;
            public Sprite Sprite;
            public Mesh Mesh;
            public MeshSnapshot Snapshot;
            public string InflationStyle;
            public bool BuildFailed;
        }

        static readonly object _lock = new object();
        static readonly Dictionary<int, Entry> _cache = new Dictionary<int, Entry>(1024);
        static readonly Dictionary<string, int> _nameToSpriteId = new Dictionary<string, int>();
        static readonly HashSet<int> _diagnosedSprites = new HashSet<int>();
        static readonly HashSet<string> _invalidVoxelStyles = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        static readonly ConcurrentQueue<BuildCompletion> _completedBuilds = new ConcurrentQueue<BuildCompletion>();
        static readonly ConcurrentQueue<BuildRequest> _queuedBuilds = new ConcurrentQueue<BuildRequest>();
        static readonly HashSet<int> _pendingBuilds = new HashSet<int>();
        // Evict() can't Destroy a mesh that may still be queued in the batcher for this frame;
        // queue it here and let VoxelFrameDriver drain after MeshInstanceBatcher.Flush().
        static readonly Queue<Mesh> _pendingDestroy = new Queue<Mesh>();
        static ulong _frame;
        static Mesh _placeholderMesh;
        static long _hits;
        static long _misses;
        static long _totalBuilds;
        static int _completedBuildsThisFrame;

        /// <summary>Cumulative cache-hit count since process start (or last Clear).</summary>
        public static long HitCount => System.Threading.Interlocked.Read(ref _hits);
        /// <summary>Cumulative cache-miss count since process start (or last Clear).</summary>
        public static long MissCount => System.Threading.Interlocked.Read(ref _misses);
        /// <summary>Number of builds currently queued for background processing.</summary>
        public static int PendingBuilds
        {
            get { lock (_lock) return _pendingBuilds.Count; }
        }

        /// <summary>Number of completions that were applied in the last frame.</summary>
        public static int CompletedBuildsThisFrame => Volatile.Read(ref _completedBuildsThisFrame);
        /// <summary>Total background build requests enqueued since process start (or last Clear).</summary>
        public static long TotalBuilds => Interlocked.Read(ref _totalBuilds);

        /// <summary>Total number of meshes currently held.</summary>
        public static int Count
        {
            get { lock (_lock) return _cache.Count; }
        }

        public static bool TryDescribe(string spriteName, out MeshSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrEmpty(spriteName)) return false;
            int key;
            lock (_lock)
            {
                if (_nameToSpriteId.TryGetValue(spriteName, out key))
                {
                    if (_cache.TryGetValue(key, out Entry e) && e.Snapshot != null)
                    {
                        snapshot = e.Snapshot;
                        return true;
                    }
                }
                // Fallback: scan all entries for matching snapshot.spriteName
                // Necessary when the cache entry was inserted with a different
                // Sprite instance ID than the one Bridge resolves now.
                foreach (var kvp in _cache)
                {
                    if (kvp.Value.Snapshot != null &&
                        string.Equals(kvp.Value.Snapshot.spriteName, spriteName, System.StringComparison.Ordinal))
                    {
                        // Back-fill name index for next time
                        _nameToSpriteId[spriteName] = kvp.Key;
                        snapshot = kvp.Value.Snapshot;
                        return true;
                    }
                }
            }
            return false;
        }

        // Back-fill name index when caller resolves a Sprite outside the cache path.
        public static void RegisterSpriteName(Sprite sprite)
        {
            if (sprite == null || string.IsNullOrEmpty(sprite.name)) return;
            int key = sprite.GetInstanceID();
            lock (_lock)
            {
                if (_cache.ContainsKey(key)) _nameToSpriteId[sprite.name] = key;
            }
        }

        public static bool TryDescribe(Sprite sprite, out MeshSnapshot snapshot)
        {
            snapshot = null;
            if (sprite == null) return false;

            int key = sprite.GetInstanceID();
            lock (_lock)
            {
                if (!_cache.TryGetValue(key, out Entry entry) || entry.Snapshot == null)
                {
                    return false;
                }

                snapshot = entry.Snapshot;
                return true;
            }
        }

        public static List<MeshSnapshot> DescribeAll()
        {
            var snapshots = new List<MeshSnapshot>();
            lock (_lock)
            {
                foreach (Entry entry in _cache.Values)
                {
                    if (entry.Snapshot != null)
                    {
                        snapshots.Add(entry.Snapshot);
                    }
                }
            }

            snapshots.Sort((a, b) =>
            {
                int nameCompare = string.CompareOrdinal(a != null ? a.spriteName : null, b != null ? b.spriteName : null);
                if (nameCompare != 0) return nameCompare;
                int idA = a != null ? a.spriteId : 0;
                int idB = b != null ? b.spriteId : 0;
                return idA.CompareTo(idB);
            });
            return snapshots;
        }

        internal static MeshSnapshot CreateSnapshot(Sprite sprite, Mesh mesh, IList<Vector3> vertices, IList<Color32> colors, IList<int> triangles)
        {
            var snapshot = new MeshSnapshot
            {
                spriteId = sprite != null ? sprite.GetInstanceID() : 0,
                spriteName = sprite != null ? sprite.name : null,
                meshName = mesh != null ? mesh.name : null,
                vertexCount = vertices != null ? vertices.Count : 0,
                triangleCount = triangles != null ? triangles.Count / 3 : 0,
                bounds = new MeshBoundsSnapshot
                {
                    min = mesh != null ? mesh.bounds.min : Vector3.zero,
                    max = mesh != null ? mesh.bounds.max : Vector3.zero,
                },
                invariants = new MeshInvariantsSnapshot
                {
                    distinctTriVerts = 0,
                    maxTriIndexLessThanVerts = true,
                    maxTriIndex = -1,
                },
            };

            int vertexSampleCount = vertices != null ? Math.Min(SampleLimit, vertices.Count) : 0;
            for (int i = 0; i < vertexSampleCount; i++)
            {
                snapshot.vertices.Add(vertices[i]);
            }

            int triangleSampleCount = triangles != null ? Math.Min(SampleLimit, triangles.Count) : 0;
            var distinctTriangleVerts = new HashSet<int>();
            int maxTriIndex = -1;
            for (int i = 0; i < triangleSampleCount; i++)
            {
                int index = triangles[i];
                snapshot.triangles.Add(index);
                distinctTriangleVerts.Add(index);
                if (index > maxTriIndex) maxTriIndex = index;
            }

            int colorSampleCount = colors != null ? Math.Min(SampleLimit, colors.Count) : 0;
            for (int i = 0; i < colorSampleCount; i++)
            {
                snapshot.colors.Add(colors[i]);
            }

            snapshot.invariants.distinctTriVerts = distinctTriangleVerts.Count;
            snapshot.invariants.maxTriIndex = maxTriIndex;
            snapshot.invariants.maxTriIndexLessThanVerts = maxTriIndex < snapshot.vertexCount;
            return snapshot;
        }

        /// <summary>Return the cached voxel mesh for <paramref name="sprite"/>, building one if missing.</summary>
        public static Mesh Get(Sprite sprite, int depth = -1, bool forceSyncBuild = false)
        {
            if (sprite == null) return null;
            int key = sprite.GetInstanceID();
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var e))
                {
                    if (e.Mesh == null || e.Mesh.vertexCount == 0)
                    {
                        _cache.Remove(key);
                        return null;
                    }
                    e.LastFrame = _frame;
                    _cache[key] = e;
                    if (sprite != null && !string.IsNullOrEmpty(sprite.name)) _nameToSpriteId[sprite.name] = key;
                    System.Threading.Interlocked.Increment(ref _hits);
                    return e.Mesh;
                }
            }

            System.Threading.Interlocked.Increment(ref _misses);
            EnqueueBuild(sprite, depth, key);
            return GetPlaceholderVoxelMesh();
        }

        static Mesh BuildVoxelMeshSync(Sprite sprite, int key, int depth)
        {
            BuildCompletion completion = BuildVoxelMeshAsync(new BuildRequest { Sprite = sprite, Key = key, Depth = depth });
            if (completion.BuildFailed || completion.Mesh == null || completion.Mesh.vertexCount == 0)
            {
                return null;
            }

            Mesh mesh = completion.Mesh;
            if (mesh != null && Core.savedSettings != null && Core.savedSettings.VoxelMeshSmoothing)
            {
                Mesh smoothed = MeshSmoother.Smooth(mesh, Core.savedSettings.SmoothingIterations);
                if (smoothed != null && !ReferenceEquals(smoothed, mesh))
                {
                    UnityEngine.Object.Destroy(mesh);
                    mesh = smoothed;
                }
            }

            MeshSnapshot snapshot = completion.Snapshot;
            if (snapshot == null && mesh != null)
            {
                snapshot = CreateSnapshot(completion.Sprite, mesh, mesh.vertices, mesh.colors32, mesh.triangles);
            }

            completion.Mesh = mesh;
            completion.Snapshot = snapshot;

            LogVoxelizedSprite(completion.Sprite, mesh, completion.InflationStyle);
            lock (_lock)
            {
                if (_pendingBuilds.Remove(key))
                {
                    // no-op, keep existing cache placeholder lifetime behavior
                }

                if (_cache.TryGetValue(key, out var existing))
                {
                    if (existing.Mesh != null && !ReferenceEquals(existing.Mesh, _placeholderMesh))
                    {
                        _pendingDestroy.Enqueue(existing.Mesh);
                    }
                }

                _cache[key] = new Entry { Mesh = mesh, Snapshot = snapshot, LastFrame = _frame };
                if (sprite != null && !string.IsNullOrEmpty(sprite.name))
                {
                    _nameToSpriteId[sprite.name] = key;
                }
                if (_cache.Count > Capacity) Evict();
            }

            return mesh;
        }

        static void EnqueueBuild(Sprite sprite, int depth, int key)
        {
            lock (_lock)
            {
                if (_cache.ContainsKey(key) || _pendingBuilds.Contains(key))
                {
                    return;
                }

                _cache[key] = new Entry { Mesh = GetPlaceholderVoxelMesh(), Snapshot = null, LastFrame = _frame };
                if (sprite != null && !string.IsNullOrEmpty(sprite.name)) _nameToSpriteId[sprite.name] = key;
                _pendingBuilds.Add(key);
                Interlocked.Increment(ref _totalBuilds);
                if (_cache.Count > Capacity) Evict();
            }

            var request = new BuildRequest { Sprite = sprite, Key = key, Depth = depth };
            _queuedBuilds.Enqueue(request);
        }

        public static void PumpQueuedBuilds(int maxBuildsPerFrame = 1)
        {
            int processed = 0;
            while (processed < maxBuildsPerFrame && _queuedBuilds.TryDequeue(out BuildRequest request))
            {
                bool shouldBuild = true;
                lock (_lock)
                {
                    shouldBuild = _pendingBuilds.Contains(request.Key);
                }

                if (!shouldBuild)
                {
                    continue;
                }

                try
                {
                    var completion = BuildVoxelMeshAsync(request);
                    _completedBuilds.Enqueue(completion);
                }
                catch
                {
                    _completedBuilds.Enqueue(new BuildCompletion
                    {
                        Key = request.Key,
                        Sprite = request.Sprite,
                        Mesh = null,
                        Snapshot = null,
                        InflationStyle = null,
                        BuildFailed = true
                    });
                }

                processed++;
            }
        }

        static BuildCompletion BuildVoxelMeshAsync(BuildRequest request)
        {
            Mesh m = BuildVoxelMesh(request.Sprite, request.Depth, out int[] vertexToTexel, out string inflationStyle);
            MeshSnapshot snapshot = m != null ? CreateSnapshot(request.Sprite, m, m.vertices, m.colors32, m.triangles) : null;
            return new BuildCompletion
            {
                Key = request.Key,
                Sprite = request.Sprite,
                Mesh = m,
                Snapshot = snapshot,
                InflationStyle = inflationStyle,
                BuildFailed = m == null || m.vertexCount == 0 || vertexToTexel == null,
            };
        }

        /// <summary>
        /// Apply up to <paramref name="maxCompletionsPerFrame"/> completed async builds.
        /// </summary>
        public static void DrainCompletedBuilds(int maxCompletionsPerFrame = 8)
        {
            int drained = 0;
            while (drained < maxCompletionsPerFrame && _completedBuilds.TryDequeue(out BuildCompletion completion))
            {
                lock (_lock)
                {
                    _pendingBuilds.Remove(completion.Key);
                }

                if (completion.BuildFailed || completion.Mesh == null || completion.Mesh.vertexCount == 0)
                {
                    continue;
                }

                Mesh mesh = completion.Mesh;
                if (mesh != null && Core.savedSettings.VoxelMeshSmoothing)
                {
                    Mesh smoothed = MeshSmoother.Smooth(mesh, Core.savedSettings.SmoothingIterations);
                    if (smoothed != null && !ReferenceEquals(smoothed, mesh))
                    {
                        UnityEngine.Object.Destroy(mesh);
                        mesh = smoothed;
                        completion.Snapshot = CreateSnapshot(completion.Sprite, mesh, mesh.vertices, mesh.colors32, mesh.triangles);
                    }
                }

                if (completion.Snapshot == null)
                {
                    completion.Snapshot = CreateSnapshot(completion.Sprite, mesh, mesh.vertices, mesh.colors32, mesh.triangles);
                }

                LogVoxelizedSprite(completion.Sprite, mesh, completion.InflationStyle);
                lock (_lock)
                {
                    if (_cache.TryGetValue(completion.Key, out Entry existing))
                    {
                        if (existing.Mesh != null && !ReferenceEquals(existing.Mesh, _placeholderMesh))
                        {
                            _pendingDestroy.Enqueue(existing.Mesh);
                        }
                    }

                    _cache[completion.Key] = new Entry { Mesh = mesh, Snapshot = completion.Snapshot, LastFrame = _frame };
                    if (_cache.Count > Capacity) Evict();
                }

                drained++;
            }
            if (drained > 0)
            {
                Interlocked.Add(ref _completedBuildsThisFrame, drained);
            }
        }

        public static void BeginFrame()
        {
            Interlocked.Exchange(ref _completedBuildsThisFrame, 0);
        }

        static Mesh GetPlaceholderVoxelMesh()
        {
            if (_placeholderMesh != null) return _placeholderMesh;

            lock (_lock)
            {
                if (_placeholderMesh != null) return _placeholderMesh;
                _placeholderMesh = BuildPlaceholderMesh();
                return _placeholderMesh;
            }
        }

        static Mesh BuildPlaceholderMesh()
        {
            const float h = 0.5f;
            var mesh = new Mesh { name = "WSM3D.Voxel.Placeholder" };
            Vector3[] vertices =
            {
                new Vector3(-h, -h, -h),
                new Vector3(h, -h, -h),
                new Vector3(h, h, -h),
                new Vector3(-h, h, -h),
                new Vector3(-h, -h, h),
                new Vector3(h, -h, h),
                new Vector3(h, h, h),
                new Vector3(-h, h, h),
                new Vector3(-h, -h, -h),
                new Vector3(-h, h, -h),
                new Vector3(-h, h, h),
                new Vector3(-h, -h, h),
                new Vector3(h, -h, -h),
                new Vector3(h, h, -h),
                new Vector3(h, h, h),
                new Vector3(h, -h, h),
                new Vector3(-h, h, -h),
                new Vector3(h, h, -h),
                new Vector3(h, h, h),
                new Vector3(-h, h, h),
                new Vector3(-h, -h, -h),
                new Vector3(h, -h, -h),
                new Vector3(h, -h, h),
                new Vector3(-h, -h, h),
            };
            int[] triangles =
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                3, 7, 6, 3, 6, 2,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5,
            };
            Color32[] colors = new Color32[vertices.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = Color.magenta;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.colors32 = colors;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Build a voxel mesh plus per-vertex rigid bone assignment for skeletal-tier
        /// actors. Humanoid rigs use the sprite's local Y bands to split head, torso,
        /// arm, and leg voxels into one-bone skin regions.
        /// </summary>
        public static SkinnedVoxelMesh BuildWithBoneWeights(Sprite sprite, WorldSphereMod.Rig.RigType rigType)
        {
            WorldSphereMod.Rig.RigType resolvedRig = rigType == WorldSphereMod.Rig.RigType.None
                ? WorldSphereMod.Rig.RigType.Static
                : rigType;

            if (sprite == null)
            {
                return new SkinnedVoxelMesh
                {
                    BaseMesh = null,
                    BoneIndices = System.Array.Empty<byte>(),
                    RigType = resolvedRig,
                };
            }

            Mesh mesh = SpriteVoxelizer.BuildPerTexel(sprite, SpriteVoxelizer.DefaultDepth, out int[] vertexToTexel);
            if (mesh == null || mesh.vertexCount == 0)
            {
                return new SkinnedVoxelMesh
                {
                    BaseMesh = mesh,
                    BoneIndices = System.Array.Empty<byte>(),
                    RigType = resolvedRig,
                };
            }

            byte[] boneIndices = new byte[mesh.vertexCount];
            bool enableSkeletalSkinning = Core.savedSettings == null || Core.savedSettings.SkeletalAnimation;
            if (!enableSkeletalSkinning)
            {
                mesh.boneWeights = System.Array.Empty<BoneWeight>();
                mesh.bindposes = System.Array.Empty<Matrix4x4>();
            }

            if (resolvedRig == WorldSphereMod.Rig.RigType.Humanoid)
            {
                BoneId[] segment = BuildHumanoidSegments(sprite);
                int segLen = segment != null ? segment.Length : 0;
                int vmapLen = vertexToTexel != null ? vertexToTexel.Length : 0;

                for (int i = 0; i < boneIndices.Length; i++)
                {
                    BoneId bone = BoneId.Spine;
                    if (segment != null && i < vmapLen)
                    {
                        int t = vertexToTexel[i];
                        if (t >= 0 && t < segLen)
                        {
                            BoneId mapped = segment[t];
                            bone = mapped == BoneId.Root ? BoneId.Spine : mapped;
                        }
                    }
                    boneIndices[i] = (byte)bone;
                }
            }
            else
            {
                byte defaultBone = (byte)BoneId.Spine;
                for (int i = 0; i < boneIndices.Length; i++)
                {
                    boneIndices[i] = defaultBone;
                }
            }

            return new SkinnedVoxelMesh
            {
                BaseMesh = mesh,
                BoneIndices = enableSkeletalSkinning ? boneIndices : System.Array.Empty<byte>(),
                RigType = resolvedRig,
            };
        }

        /// <summary>Wipe everything. Call when the world reloads.</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                foreach (var e in _cache.Values)
                {
                    if (e.Mesh != null) UnityEngine.Object.Destroy(e.Mesh);
                }
                _cache.Clear();
                _diagnosedSprites.Clear();
                _pendingDestroy.Clear();
                _pendingBuilds.Clear();
            while (_completedBuilds.TryDequeue(out _))
            {
            }
            while (_queuedBuilds.TryDequeue(out _))
            {
            }
            if (_placeholderMesh != null)
            {
                UnityEngine.Object.Destroy(_placeholderMesh);
                    _placeholderMesh = null;
                }
            }
            System.Threading.Interlocked.Exchange(ref _hits, 0);
            System.Threading.Interlocked.Exchange(ref _misses, 0);
            System.Threading.Interlocked.Exchange(ref _totalBuilds, 0);
            Interlocked.Exchange(ref _completedBuildsThisFrame, 0);
        }

        /// <summary>Advance the frame counter; call once per render frame.</summary>
        public static void Tick()
        {
            lock (_lock) _frame++;
        }

        /// <summary>Destroy meshes queued by <see cref="Evict"/>. Call once per frame after the batcher flushes.</summary>
        public static void DrainPendingDestroy()
        {
            lock (_lock)
            {
                while (_pendingDestroy.Count > 0)
                {
                    var m = _pendingDestroy.Dequeue();
                    if (m != null) UnityEngine.Object.Destroy(m);
                }
            }
        }

        static void Evict()
        {
            // Caller holds _lock. Remove least-recently-used entries until capped.
            if (_cache.Count <= MAX_ENTRIES)
            {
                return;
            }

            int toRemoveCount = _cache.Count - MAX_ENTRIES;
            while (_cache.Count > MAX_ENTRIES && toRemoveCount > 0)
            {
                int lruKey = -1;
                ulong lruFrame = ulong.MaxValue;
                foreach (var kv in _cache)
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

                Entry lruEntry = _cache[lruKey];
                if (lruEntry.Mesh != null && !ReferenceEquals(lruEntry.Mesh, _placeholderMesh))
                {
                    _pendingDestroy.Enqueue(lruEntry.Mesh);
                }

                _cache.Remove(lruKey);
                toRemoveCount--;
            }
        }

        static void LogVoxelizedSprite(Sprite sprite, Mesh mesh, string inflationStyle)
        {
            if (sprite == null || mesh == null) return;
            int key = sprite.GetInstanceID();
            lock (_lock)
            {
                if (!_diagnosedSprites.Add(key)) return;
            }

            int triCount = mesh.subMeshCount > 0 ? (int)(mesh.GetIndexCount(0) / 3) : 0;
            Debug.Log($"[WSM3D] Voxelized sprite \"{sprite.name}\" style=\"{inflationStyle}\" -> {mesh.vertexCount} verts, {triCount} tris, bounds={mesh.bounds}");
        }

        static Mesh BuildVoxelMesh(Sprite sprite, int depth, out Mesh mesh)
        {
            mesh = BuildVoxelMesh(sprite, depth, out _, out _);
            return mesh;
        }

        static Mesh BuildVoxelMesh(Sprite sprite, int depth, out int[] vertexToTexel, out string inflationStyle)
        {
            // Per-sprite shape-hint routing. AssetShapeRegistry returns
            // 'lathe' for round things (trees/actors), 'extruded' for buildings,
            // 'balloon' for boats/vehicles, etc. Honors non-auto global override.
            inflationStyle = sprite != null
                ? AssetShapeRegistry.ResolveStyle(sprite.name, sprite)
                : ResolveVoxelInflationStyle();
            if (string.Equals(inflationStyle, "lathe", System.StringComparison.OrdinalIgnoreCase))
            {
                depth = -1;
            }

            if (string.Equals(inflationStyle, "balloon", System.StringComparison.OrdinalIgnoreCase))
            {
                return SpriteVoxelizer.BuildBalloon(sprite, depth, out vertexToTexel);
            }

            if (string.Equals(inflationStyle, "lathe", System.StringComparison.OrdinalIgnoreCase))
            {
                return SpriteVoxelizer.BuildLathe(sprite, depth, out vertexToTexel);
            }

            vertexToTexel = System.Array.Empty<int>();
            return SpriteVoxelizer.BuildPerTexel(sprite, depth, out vertexToTexel);
        }

        static string ResolveVoxelInflationStyle()
        {
            string rawStyle = Core.savedSettings != null ? Core.savedSettings.VoxelInflationStyle : null;
            if (string.IsNullOrWhiteSpace(rawStyle))
            {
                return "pertexel";
            }

            string style = rawStyle.Trim().ToLowerInvariant();
            if (style == "pertexel" || style == "per-texel" || style == "extruded" || style == "extrude")
            {
                return "pertexel";
            }

            if (style == "balloon" || style == "ballooned")
            {
                return "balloon";
            }

            if (style == "lathe" || style == "revolved" || style == "revolve")
            {
                return "lathe";
            }

            if (style == "0" || style == "1")
            {
                int value = int.Parse(style);
                return value == 1 ? "balloon" : "pertexel";
            }

            if (_invalidVoxelStyles.Add(rawStyle))
            {
                Debug.LogWarning($"[WSM3D] Unsupported VoxelInflationStyle '{rawStyle}'. Using per-texel fallback.");
            }

            return "pertexel";
        }

        static BoneId[] BuildHumanoidSegments(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null || !sprite.texture.isReadable)
            {
                return null;
            }

            Rect r = sprite.textureRect;
            int w = Mathf.Max(1, (int)r.width);
            int h = Mathf.Max(1, (int)r.height);
            int sx = (int)r.x;
            int sy = (int)r.y;
            Color32[] tex = SpriteVoxelizer.GetPixelsCached(sprite.texture);
            int texW = sprite.texture.width;

            var sub = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int dstRow = y * w;
                int srcRow = (sy + y) * texW + sx;
                for (int x = 0; x < w; x++)
                {
                    sub[dstRow + x] = tex[srcRow + x];
                }
            }

            return WorldSphereMod.Rig.HumanoidRig.SegmentVoxels(w, h, sub);
        }
    }
}
