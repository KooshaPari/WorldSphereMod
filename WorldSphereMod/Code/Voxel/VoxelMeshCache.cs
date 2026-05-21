using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        public static int Capacity = 8192;

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

        static readonly object _lock = new object();
        static readonly Dictionary<int, Entry> _cache = new Dictionary<int, Entry>(1024);
        static readonly HashSet<int> _diagnosedSprites = new HashSet<int>();
        static readonly HashSet<string> _invalidVoxelStyles = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        static readonly Queue<Sprite> _warmQueue = new Queue<Sprite>();
        static readonly HashSet<int> _warmQueuedSprites = new HashSet<int>();
        // Evict() can't Destroy a mesh that may still be queued in the batcher for this frame;
        // queue it here and let VoxelFrameDriver drain after MeshInstanceBatcher.Flush().
        static readonly Queue<Mesh> _pendingDestroy = new Queue<Mesh>();
        static ulong _frame;
        static int _warmBudgetMsPerFrame = 5;
        static long _hits;
        static long _misses;

        /// <summary>Cumulative cache-hit count since process start (or last Clear).</summary>
        public static long HitCount => System.Threading.Interlocked.Read(ref _hits);
        /// <summary>Cumulative cache-miss count since process start (or last Clear).</summary>
        public static long MissCount => System.Threading.Interlocked.Read(ref _misses);

        /// <summary>Total number of meshes currently held.</summary>
        public static int Count
        {
            get { lock (_lock) return _cache.Count; }
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
        public static Mesh Get(Sprite sprite, int depth = -1)
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
                    System.Threading.Interlocked.Increment(ref _hits);
                    return e.Mesh;
                }
            }
            // Build outside the lock — Mesh construction touches Unity APIs that
            // shouldn't be held under a lock, and Get() always runs on the main thread.
            System.Threading.Interlocked.Increment(ref _misses);
            Mesh m = BuildVoxelMesh(sprite, depth, out _, out string inflationStyle);
            Debug.Log($"[WSM3D] VoxelMeshCache.Get style=\"{inflationStyle}\" sprite=\"{sprite.name}\" vertexCount={m?.vertexCount ?? 0}");
            if (m != null && Core.savedSettings.VoxelMeshSmoothing)
            {
                // ADR-0008: Laplacian smoothing converts blocky voxel stair-steps
                // into a rounded 'blob' silhouette. Smooth returns a copy via
                // Object.Instantiate; destroy the raw mesh so we don't leak it.
                Mesh smoothed = MeshSmoother.Smooth(m, Core.savedSettings.SmoothingIterations);
                if (smoothed != null && !ReferenceEquals(smoothed, m))
                {
                    UnityEngine.Object.Destroy(m);
                    m = smoothed;
                }
            }
            MeshSnapshot snapshot = m != null ? CreateSnapshot(sprite, m, m.vertices, m.colors32, m.triangles) : null;
            LogVoxelizedSprite(sprite, m, inflationStyle);
            if (m == null || m.vertexCount == 0)
            {
                return null;
            }
            lock (_lock)
            {
                _cache[key] = new Entry { Mesh = m, Snapshot = snapshot, LastFrame = _frame };
                if (_cache.Count > Capacity) Evict();
            }
            return m;
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

        /// <summary>
        /// Queue sprites for budgeted voxel-mesh warmup. Work is drained from
        /// <see cref="VoxelFrameDriver.LateUpdate"/> via <see cref="DrainWarmCacheTick"/>.
        /// </summary>
        public static void WarmCacheAsync(IEnumerable<Sprite> sprites, int msBudgetPerFrame = 5)
        {
            if (sprites == null) return;

            lock (_lock)
            {
                _warmBudgetMsPerFrame = msBudgetPerFrame > 0 ? msBudgetPerFrame : 1;

                foreach (var sprite in sprites)
                {
                    if (!ShouldWarmSprite(sprite)) continue;

                    int key = sprite.GetInstanceID();
                    if (_cache.ContainsKey(key) || _warmQueuedSprites.Contains(key)) continue;

                    _warmQueue.Enqueue(sprite);
                    _warmQueuedSprites.Add(key);
                }
            }
        }

        /// <summary>Advance the frame counter; call once per render frame.</summary>
        public static void Tick()
        {
            lock (_lock) _frame++;
        }

        /// <summary>
        /// Drain queued warm-cache work for a bounded amount of time. Call once per
        /// frame from <see cref="VoxelFrameDriver.LateUpdate"/>.
        /// </summary>
        public static void DrainWarmCacheTick()
        {
            int budgetMs;
            lock (_lock)
            {
                budgetMs = _warmBudgetMsPerFrame;
            }

            if (budgetMs <= 0) return;

            Stopwatch sw = Stopwatch.StartNew();
            var batch = new List<Sprite>();
            while (sw.ElapsedMilliseconds < budgetMs)
            {
                Sprite sprite = null;

                lock (_lock)
                {
                    while (_warmQueue.Count > 0)
                    {
                        sprite = _warmQueue.Dequeue();
                        if (sprite == null) continue;

                        int key = sprite.GetInstanceID();
                        _warmQueuedSprites.Remove(key);

                        if (sprite.texture == null) { sprite = null; continue; }
                        if (IsPerpSprite(sprite)) { sprite = null; continue; }
                        if (_cache.ContainsKey(key)) { sprite = null; continue; }
                        break;
                    }

                    if (sprite == null)
                    {
                        break;
                    }
                }

                batch.Add(sprite);
            }

            if (batch.Count == 0) return;

            if (batch.Count == 1)
            {
                Mesh mesh = BuildVoxelMesh(batch[0], -1, out _);
                MeshSnapshot snapshot = mesh != null ? CreateSnapshot(batch[0], mesh, mesh.vertices, mesh.colors32, mesh.triangles) : null;
                CacheWarmSprite(batch[0], mesh, snapshot);
                return;
            }

            var built = new ConcurrentQueue<(Sprite Sprite, Mesh Mesh, MeshSnapshot Snapshot)>();
            System.Threading.Tasks.Parallel.ForEach(batch, sprite =>
            {
                Mesh mesh = BuildVoxelMesh(sprite, -1, out _);
                MeshSnapshot snapshot = mesh != null ? CreateSnapshot(sprite, mesh, mesh.vertices, mesh.colors32, mesh.triangles) : null;
                built.Enqueue((sprite, mesh, snapshot));
            });

            while (built.TryDequeue(out var result))
            {
                CacheWarmSprite(result.Sprite, result.Mesh, result.Snapshot);
            }
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
                _warmQueue.Clear();
                _warmQueuedSprites.Clear();
            }
            System.Threading.Interlocked.Exchange(ref _hits, 0);
            System.Threading.Interlocked.Exchange(ref _misses, 0);
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
            // Caller holds _lock. O(N) two-pass eviction — find frame range, drop bottom decile.
            if (_cache.Count == 0) return;
            ulong minFrame = ulong.MaxValue, maxFrame = 0;
            foreach (var v in _cache.Values)
            {
                if (v.LastFrame < minFrame) minFrame = v.LastFrame;
                if (v.LastFrame > maxFrame) maxFrame = v.LastFrame;
            }
            if (maxFrame == minFrame) return;
            ulong threshold = minFrame + (maxFrame - minFrame) / 10;
            var toRemove = new List<int>();
            foreach (var kv in _cache)
            {
                if (kv.Value.LastFrame <= threshold) toRemove.Add(kv.Key);
            }
            foreach (var key in toRemove)
            {
                if (_cache[key].Mesh != null) _pendingDestroy.Enqueue(_cache[key].Mesh);
                _cache.Remove(key);
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
            inflationStyle = ResolveVoxelInflationStyle();
            if (string.Equals(inflationStyle, "balloon", System.StringComparison.OrdinalIgnoreCase))
            {
                return SpriteVoxelizer.BuildBalloon(sprite, depth, out vertexToTexel);
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

        static bool IsPerpSprite(Sprite sprite)
        {
            if (sprite == null) return false;

            string name = sprite.name;
            return (!string.IsNullOrEmpty(name) && (Constants.PerpActors.ContainsKey(name) || Constants.PerpBuildings.ContainsKey(name)));
        }

        static bool ShouldWarmSprite(Sprite sprite)
        {
            return sprite != null && sprite.texture != null && !IsPerpSprite(sprite);
        }

        static void CacheWarmSprite(Sprite sprite, Mesh mesh, MeshSnapshot snapshot)
        {
            LogVoxelizedSprite(sprite, mesh, "warmup");
            if (sprite == null || mesh == null || mesh.vertexCount == 0)
            {
                return;
            }

            int warmKey = sprite.GetInstanceID();
            lock (_lock)
            {
                if (_cache.ContainsKey(warmKey))
                {
                    _pendingDestroy.Enqueue(mesh);
                    return;
                }

                _cache[warmKey] = new Entry { Mesh = mesh, Snapshot = snapshot, LastFrame = _frame };
                if (_cache.Count > Capacity) Evict();
            }
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
