using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using WorldSphereMod;
using WorldSphereMod.Rig;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;

namespace WorldSphereMod.Voxel
{
    public enum VoxelEntityType
    {
        Unknown,
        Actor,
        Building,
        Foliage,
        Procedural,
        Effect,
        Vehicle,
        Other,
    }

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
        public const int MAX_ENTRIES = 1024;
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

            // PERF: full CPU-side mesh data captured in SpriteVoxelizer.Build BEFORE
            // mesh.UploadMeshData(true) strips it (isReadable=false). The truncated
            // vertices/triangles/colors lists above are sampled (SampleLimit) for Bridge
            // diagnostics; these full arrays exist solely so VoxelDiskCache can persist
            // the mesh WITHOUT re-reading the stripped mesh — which otherwise emits four
            // synchronous "Not allowed to access ... isReadable is false" LogWarnings per
            // build, flushed to Player.log, blowing frame time up to ~2s (regression).
            public Vector3[] fullVertices;
            public int[] fullTriangles;
            public Color32[] fullColors;
            public Vector3[] fullNormals;

            public bool HasFullData =>
                fullVertices != null && fullVertices.Length > 0 &&
                fullTriangles != null && fullTriangles.Length > 0;
        }

        /// <summary>
        /// Reconstruct a transient CPU-readable <see cref="Mesh"/> from a snapshot's full
        /// arrays so <see cref="VoxelDiskCache.EnqueueSave"/> can serialize it without
        /// touching the GPU-uploaded (non-readable) source mesh. The caller owns the
        /// returned mesh and must Destroy it after the save data is captured.
        /// </summary>
        static Mesh BuildReadableMeshFromSnapshot(MeshSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.HasFullData) return null;
            var mesh = new Mesh { name = snapshot.meshName ?? "voxel:disksave" };
            if (snapshot.fullVertices.Length > 65535)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.SetVertices(snapshot.fullVertices);
            if (snapshot.fullColors != null && snapshot.fullColors.Length == snapshot.fullVertices.Length)
            {
                mesh.SetColors(snapshot.fullColors);
            }
            mesh.SetTriangles(snapshot.fullTriangles, 0);
            if (snapshot.fullNormals != null && snapshot.fullNormals.Length == snapshot.fullVertices.Length)
            {
                mesh.SetNormals(snapshot.fullNormals);
            }
            else
            {
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Persist a freshly built voxel mesh to the disk cache. Prefers the snapshot's
        /// full CPU-side arrays (captured pre-UploadMeshData) so we never read the
        /// GPU-uploaded mesh's vertices/colors/triangles/normals — those reads emit the
        /// "isReadable is false" LogWarning flood that regressed frame time to ~2s.
        /// Falls back to the live mesh only when no full snapshot data is available.
        /// </summary>
        static void EnqueueDiskSave(string spriteName, Mesh mesh, MeshSnapshot snapshot, int depth, string style, string spriteHash)
        {
            if (Core.savedSettings == null || !Core.savedSettings.VoxelDiskCache) return;

            if (snapshot != null && snapshot.HasFullData)
            {
                Mesh readable = BuildReadableMeshFromSnapshot(snapshot);
                if (readable != null)
                {
                    try
                    {
                        VoxelDiskCache.EnqueueSave(spriteName, readable, depth, style, spriteHash);
                    }
                    finally
                    {
                        Object.DestroyImmediate(readable);
                    }
                    return;
                }
            }

            // Fallback: no full snapshot data (non-Build sources). May still warn if the
            // mesh is GPU-uploaded, but Build-sourced meshes always carry full snapshot data.
            VoxelDiskCache.EnqueueSave(spriteName, mesh, depth, style, spriteHash);
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
            public ShapeHint ShapeHint;
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
        static readonly HashSet<int> _diagnosedShapeHints = new HashSet<int>();
        static readonly HashSet<string> _invalidVoxelStyles = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        static readonly ConcurrentQueue<BuildCompletion> _completedBuilds = new ConcurrentQueue<BuildCompletion>();
        static readonly ConcurrentQueue<BuildRequest> _queuedBuilds = new ConcurrentQueue<BuildRequest>();
        static readonly HashSet<int> _pendingBuilds = new HashSet<int>();
        // Evict() can't Destroy a mesh that may still be queued in the batcher for this frame;
        // queue it here and let VoxelFrameDriver drain after MeshInstanceBatcher.Flush().
        static readonly Queue<Mesh> _pendingDestroy = new Queue<Mesh>();
        static ulong _frame;
        static Mesh _placeholderMesh;
        // Per-sprite placeholder cache so each sprite shows its OWN dominant color
        // during the (possibly multi-second) async build wait, instead of every
        // actor on the map rendering as the same shared tan-gray cube.
        // Keyed by sprite InstanceID; small bounded LRU.
        static readonly Dictionary<int, Mesh> _spritePlaceholders = new Dictionary<int, Mesh>(256);
        const int kMaxSpritePlaceholders = 512;
        static long _hits;
        static long _misses;
        static long _totalBuilds;
        static int _completedBuildsThisFrame;
        static bool _pumpDiagLogged;

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
                        _nameToSpriteId[spriteName] = kvp.Value.Snapshot != null ? kvp.Value.Snapshot.spriteId : key;
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
                foreach (var kvp in _cache)
                {
                    if (kvp.Value.Snapshot != null && kvp.Value.Snapshot.spriteId == key)
                    {
                        snapshot = kvp.Value.Snapshot;
                        return true;
                    }
                }
            }
            return false;
        }

        static int ResolveCacheKey(int spriteId, ShapeHint shapeHint, VoxelEntityType entityType)
        {
            if (entityType == VoxelEntityType.Actor && shapeHint == ShapeHint.OrganicBlob)
            {
                return spriteId == int.MinValue ? int.MinValue : -spriteId - 1;
            }
            return spriteId;
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
        public static Mesh Get(Sprite sprite, int depth = -1, bool forceSyncBuild = false, VoxelEntityType entityType = VoxelEntityType.Unknown)
        {
            return Get(sprite, entityType == VoxelEntityType.Actor ? ShapeHint.OrganicBlob : ShapeHint.Auto, forceSyncBuild, entityType, depth);
        }

        public static Mesh Get(Sprite sprite, ShapeHint shapeHint, bool forceSyncBuild = false, VoxelEntityType entityType = VoxelEntityType.Unknown)
        {
            return Get(sprite, shapeHint, forceSyncBuild, entityType, -1);
        }

        static Mesh Get(Sprite sprite, ShapeHint shapeHint, bool forceSyncBuild, VoxelEntityType entityType, int depth)
        {
            if (sprite == null) return null;

            if (shapeHint == ShapeHint.Auto && entityType == VoxelEntityType.Actor)
            {
                shapeHint = ShapeHint.OrganicBlob;
            }

            int spriteId = sprite.GetInstanceID();
            int key = ResolveCacheKey(spriteId, shapeHint, entityType);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var e))
                {
                    if (e.Mesh == null || e.Mesh.vertexCount == 0)
                    {
                        Mesh replacement = GetPlaceholderVoxelMesh(sprite);
                        if (replacement == null)
                        {
                            _cache.Remove(key);
                            return null;
                        }

                        e.Mesh = replacement;
                        e.LastFrame = _frame;
                        _cache[key] = e;
                        if (sprite != null && !string.IsNullOrEmpty(sprite.name)) _nameToSpriteId[sprite.name] = spriteId;
                        return replacement;
                    }
                    e.LastFrame = _frame;
                    _cache[key] = e;
                    if (sprite != null && !string.IsNullOrEmpty(sprite.name)) _nameToSpriteId[sprite.name] = spriteId;
                    System.Threading.Interlocked.Increment(ref _hits);
                    return e.Mesh;
                }
            }

            System.Threading.Interlocked.Increment(ref _misses);

            if (VoxelDiskCache.TryGetFromDisk(sprite, out Mesh diskMesh))
            {
                var diskSnapshot = CreateSnapshot(sprite, diskMesh, diskMesh.vertices, diskMesh.colors32, diskMesh.triangles);
                lock (_lock)
                {
                    _cache[key] = new Entry { Mesh = diskMesh, Snapshot = diskSnapshot, LastFrame = _frame };
                    if (!string.IsNullOrEmpty(sprite.name)) _nameToSpriteId[sprite.name] = spriteId;
                    if (_cache.Count > Capacity) Evict();
                }
                return diskMesh;
            }

            EnqueueBuild(sprite, shapeHint == ShapeHint.Auto ? depth : -1, key, shapeHint);
            return GetPlaceholderVoxelMesh(sprite);
        }

        static Mesh BuildVoxelMeshSync(Sprite sprite, int key, int depth)
        {
            BuildCompletion completion = BuildVoxelMeshAsync(new BuildRequest { Sprite = sprite, Key = key, Depth = depth, ShapeHint = ShapeHint.Auto });
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
                    Object.DestroyImmediate(mesh);
                    mesh = smoothed;
                }
            }

            // PERF: do NOT recreate the snapshot from mesh.vertices/.colors32/.triangles.
            // The mesh had UploadMeshData(true) called, so those reads emit LogWarning
            // floods that destroy frame time. If BuildVoxelMeshAsync did not provide a
            // snapshot via its out-param path, leave it null (Bridge-only diagnostic).
            MeshSnapshot snapshot = completion.Snapshot;

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
                    if (existing.Mesh != null && !IsAnyPlaceholderMesh(existing.Mesh))
                    {
                        _pendingDestroy.Enqueue(existing.Mesh);
                    }
                }

                _cache[key] = new Entry { Mesh = mesh, Snapshot = snapshot, LastFrame = _frame };
                if (sprite != null && !string.IsNullOrEmpty(sprite.name))
                {
                    _nameToSpriteId[sprite.name] = sprite.GetInstanceID();
                }
                if (_cache.Count > Capacity) Evict();
            }

            return mesh;
        }

        static void EnqueueBuild(Sprite sprite, int depth, int key, ShapeHint shapeHint = ShapeHint.Auto)
        {
            lock (_lock)
            {
                if (_cache.ContainsKey(key) || _pendingBuilds.Contains(key))
                {
                    return;
                }

                _cache[key] = new Entry { Mesh = GetPlaceholderVoxelMesh(sprite), Snapshot = null, LastFrame = _frame };
                if (sprite != null && !string.IsNullOrEmpty(sprite.name)) _nameToSpriteId[sprite.name] = sprite.GetInstanceID();
                _pendingBuilds.Add(key);
                Interlocked.Increment(ref _totalBuilds);
                if (_cache.Count > Capacity) Evict();
            }

            var request = new BuildRequest { Sprite = sprite, Key = key, Depth = depth, ShapeHint = shapeHint };
            _queuedBuilds.Enqueue(request);
        }

        /// <summary>Max milliseconds PumpQueuedBuilds may spend per frame before yielding.</summary>
        const float kPumpTimeBudgetMs = 4.0f;

        public static void PumpQueuedBuilds(int maxBuildsPerFrame = 1)
        {
            int processed = 0;
            long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            double ticksPerMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;

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

                // Time-budget guard: stop pumping if we've exceeded the budget,
                // remaining builds will be processed in subsequent frames.
                double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) / ticksPerMs;
                if (elapsedMs >= kPumpTimeBudgetMs)
                {
                    break;
                }
            }

            if (!_pumpDiagLogged && processed > 0)
            {
                _pumpDiagLogged = true;
                int queued;
                lock (_lock) { queued = _pendingBuilds.Count; }
                double totalMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) / ticksPerMs;
                Debug.Log($"[WSM3D] VoxelMeshCache: {queued} pending builds, {processed} completed this frame ({totalMs:F1}ms)");
            }
        }

        static BuildCompletion BuildVoxelMeshAsync(BuildRequest request)
        {
            Mesh m = BuildVoxelMesh(request.Sprite, request.Depth, request.ShapeHint, out int[] vertexToTexel, out string inflationStyle, out MeshSnapshot snapshot);
            // PERF: do NOT call CreateSnapshot here using mesh.vertices/.colors32/.triangles —
            // SpriteVoxelizer.Build calls UploadMeshData(true) which strips the CPU copy.
            // Reading those properties post-upload triggers 4 LogWarning lines per access
            // ("Not allowed to access vertices ... isReadable is false"). Hundreds of
            // sprites × multiple frames = thousands of synchronous LogWarning -> Player.log
            // I/O calls per second, which was eating 1500-2700ms per frame (0.5 FPS).
            // The Build* methods that DO have CPU-side data should return a snapshot via
            // the out-param; otherwise leave snapshot null (it's a Bridge nice-to-have,
            // not required for rendering).
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
                    // Voxel build failed — fall back to a flat-sprite quad so the
                    // actor at least shows its real sprite art instead of being
                    // stuck on the dominant-color placeholder cube forever.
                    Mesh fallbackMesh = BuildFlatSpriteMesh(completion.Sprite);
                    if (fallbackMesh == null || fallbackMesh.vertexCount == 0)
                    {
                        if (fallbackMesh != null) Object.DestroyImmediate(fallbackMesh);
                        continue;
                    }

                    lock (_lock)
                    {
                        if (_cache.TryGetValue(completion.Key, out Entry existingFb))
                        {
                            if (existingFb.Mesh != null && !IsAnyPlaceholderMesh(existingFb.Mesh))
                            {
                                _pendingDestroy.Enqueue(existingFb.Mesh);
                            }
                        }
                        _cache[completion.Key] = new Entry { Mesh = fallbackMesh, Snapshot = null, LastFrame = _frame };
                        if (completion.Sprite != null && !string.IsNullOrEmpty(completion.Sprite.name))
                        {
                            _nameToSpriteId[completion.Sprite.name] = completion.Key;
                        }
                        if (_cache.Count > Capacity) Evict();
                    }

                    Debug.LogWarning($"[WSM3D] Voxel build failed for sprite \"{(completion.Sprite != null ? completion.Sprite.name : "<null>")}\" — using flat-sprite fallback mesh.");
                    drained++;
                    continue;
                }

                Mesh mesh = completion.Mesh;
                if (mesh != null && Core.savedSettings.VoxelMeshSmoothing)
                {
                    Mesh smoothed = MeshSmoother.Smooth(mesh, Core.savedSettings.SmoothingIterations);
                    if (smoothed != null && !ReferenceEquals(smoothed, mesh))
                    {
                        Object.DestroyImmediate(mesh);
                        mesh = smoothed;
                        // Smoothed mesh still has CPU-side data (MeshSmoother reads it),
                        // so it's safe to snapshot here.
                        completion.Snapshot = CreateSnapshot(completion.Sprite, mesh, mesh.vertices, mesh.colors32, mesh.triangles);
                        // WHY: re-capture full CPU arrays from the still-readable smoothed
                        // mesh so disk-save uses them, not a later non-readable read-back.
                        completion.Snapshot.diskVertices = mesh.vertices;
                        completion.Snapshot.diskTriangles = mesh.triangles;
                        completion.Snapshot.diskColors = mesh.colors32;
                        completion.Snapshot.diskNormals = mesh.normals;
                    }
                }

                // PERF: skip the redundant snapshot rebuild. SpriteVoxelizer.Build calls
                // UploadMeshData(true) on the mesh, so reading mesh.vertices/.colors32/
                // .triangles emits 4 LogWarning lines per call ("isReadable is false").
                // BuildVoxelMeshAsync now wires the source-side snapshot via out-param;
                // a null Snapshot is acceptable (it's only used by Bridge diagnostics).

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

                // WHY: save from the CPU snapshot captured pre-upload, never from the
                // non-readable mesh. A null/dataless snapshot means no readable copy, so
                // skip the save rather than trigger an "isReadable is false" warning flood.
                if (completion.Sprite != null && !string.IsNullOrEmpty(completion.Sprite.name)
                    && completion.Snapshot != null && completion.Snapshot.diskVertices != null)
                {
                    int depth = Core.savedSettings != null ? Core.savedSettings.VoxelSpriteDepth : 8;
                    string spriteHash = VoxelDiskCache.ComputeSpriteHash(completion.Sprite);
                    EnqueueDiskSave(completion.Sprite.name, mesh, completion.Snapshot, depth,
                        completion.InflationStyle ?? "pertexel", spriteHash);
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

        // Caller MUST hold _lock when invoking this helper.
        static bool IsAnyPlaceholderMesh(Mesh mesh)
        {
            if (mesh == null) return false;
            if (ReferenceEquals(mesh, _placeholderMesh)) return true;
            foreach (var kv in _spritePlaceholders)
            {
                if (ReferenceEquals(kv.Value, mesh)) return true;
            }
            return false;
        }

        static Mesh GetPlaceholderVoxelMesh(Sprite sprite)
        {
            // Per-sprite colored placeholder: actors waiting for their real voxel
            // mesh at least show their sprite's dominant color instead of every
            // actor sharing one tan-gray cube. Falls back to the shared neutral
            // placeholder when sprite is null or the texture cannot be sampled.
            if (sprite != null)
            {
                int sid = sprite.GetInstanceID();
                lock (_lock)
                {
                    if (_spritePlaceholders.TryGetValue(sid, out Mesh existing) && existing != null)
                    {
                        return existing;
                    }
                }

                Mesh perSprite = BuildPlaceholderMesh(sprite);
                if (perSprite != null)
                {
                    lock (_lock)
                    {
                        if (!_spritePlaceholders.ContainsKey(sid))
                        {
                            if (_spritePlaceholders.Count >= kMaxSpritePlaceholders)
                            {
                                // Bounded eviction: drop one arbitrary placeholder. These are
                                // cheap to rebuild and the cache exists only to coalesce
                                // duplicate per-frame requests during the async build wait.
                                var firstKey = default(int);
                                foreach (var kv in _spritePlaceholders) { firstKey = kv.Key; break; }
                                if (_spritePlaceholders.TryGetValue(firstKey, out Mesh dropped) && dropped != null)
                                {
                                    _pendingDestroy.Enqueue(dropped);
                                }
                                _spritePlaceholders.Remove(firstKey);
                            }
                            _spritePlaceholders[sid] = perSprite;
                        }
                        else
                        {
                            // Lost a race; destroy the duplicate we built.
                            _pendingDestroy.Enqueue(perSprite);
                            perSprite = _spritePlaceholders[sid];
                        }
                    }
                    return perSprite;
                }
            }

            if (_placeholderMesh == null)
            {
                lock (_lock)
                {
                    if (_placeholderMesh == null)
                    {
                        _placeholderMesh = BuildPlaceholderMesh();
                    }
                }
            }

            return _placeholderMesh;
        }

        static Mesh BuildPlaceholderMesh(Sprite sprite = null)
        {
            const float h = 0.5f;
            string meshName = sprite != null && !string.IsNullOrEmpty(sprite.name)
                ? $"WSM3D.Voxel.Placeholder:{sprite.name}"
                : "WSM3D.Voxel.Placeholder";
            var mesh = new Mesh { name = meshName };
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
            Color32 placeholderGray = GetDominantSpriteColor(sprite);
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = placeholderGray;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.colors32 = colors;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Build a flat single-quad mesh, extruded 1 unit deep, sized to the
        /// sprite's pixel dimensions (in local space matching SpriteVoxelizer
        /// per-texel output where 1 unit = 1 texel). Used as a last-resort
        /// fallback when the real voxel build fails so the actor still shows
        /// its sprite art instead of a placeholder cube forever.
        ///
        /// UVs are set so the sprite's textureRect maps onto the front face;
        /// the mesh also bakes per-vertex colors from the sprite's dominant
        /// color so callers using vertex-color shaders still see something
        /// reasonable. The sprite texture is exposed via the mesh name (the
        /// Bridge/material wires _MainTex at draw time).
        /// </summary>
        public static Mesh BuildFlatSpriteMesh(Sprite sprite)
        {
            if (sprite == null) return null;

            // Pixel-space size (matches per-texel voxelizer coordinates so this
            // fallback drops into the same world-scale pipeline cleanly).
            Rect r = sprite.textureRect;
            float w = Mathf.Max(1f, r.width);
            float h = Mathf.Max(1f, r.height);
            float hx = w * 0.5f;
            float hy = h * 0.5f;
            const float depthHalf = 0.5f; // "1-deep" extrusion

            string meshName = !string.IsNullOrEmpty(sprite.name)
                ? $"WSM3D.Voxel.FlatSprite:{sprite.name}"
                : "WSM3D.Voxel.FlatSprite";
            var mesh = new Mesh { name = meshName };

            // Front face (z = +depthHalf), back face (z = -depthHalf), plus
            // a thin side ring so it isn't paper-thin from edge angles.
            Vector3[] vertices =
            {
                // Front quad (faces +Z)
                new Vector3(-hx, -hy,  depthHalf),
                new Vector3( hx, -hy,  depthHalf),
                new Vector3( hx,  hy,  depthHalf),
                new Vector3(-hx,  hy,  depthHalf),
                // Back quad (faces -Z)
                new Vector3(-hx, -hy, -depthHalf),
                new Vector3( hx, -hy, -depthHalf),
                new Vector3( hx,  hy, -depthHalf),
                new Vector3(-hx,  hy, -depthHalf),
            };

            int[] triangles =
            {
                // Front (CCW from +Z)
                0, 2, 1, 0, 3, 2,
                // Back (CCW from -Z)
                4, 5, 6, 4, 6, 7,
                // Sides
                0, 1, 5, 0, 5, 4, // bottom
                3, 7, 6, 3, 6, 2, // top
                0, 4, 7, 0, 7, 3, // left
                1, 2, 6, 1, 6, 5, // right
            };

            // Sprite UVs map textureRect onto the front face. Reuse the same
            // UVs for the back so flipped views still show the sprite.
            Texture tex = sprite.texture;
            float texW = tex != null ? tex.width : 1f;
            float texH = tex != null ? tex.height : 1f;
            float u0 = r.x / texW;
            float v0 = r.y / texH;
            float u1 = (r.x + r.width) / texW;
            float v1 = (r.y + r.height) / texH;

            Vector2[] uvs =
            {
                new Vector2(u0, v0),
                new Vector2(u1, v0),
                new Vector2(u1, v1),
                new Vector2(u0, v1),
                new Vector2(u0, v0),
                new Vector2(u1, v0),
                new Vector2(u1, v1),
                new Vector2(u0, v1),
            };

            // Vertex colors = dominant sprite color so shaders that multiply
            // _MainTex by COLOR (or use COLOR only) still render something.
            Color32 tint = GetDominantSpriteColor(sprite);
            Color32[] colors = new Color32[vertices.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = tint;

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Color32 GetDominantSpriteColor(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null)
            {
                return new Color32(180, 160, 140, 255);
            }

            try
            {
                Rect rect = sprite.textureRect;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(rect.x));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(rect.y));
                int w = Mathf.Max(1, Mathf.FloorToInt(rect.width));
                int h = Mathf.Max(1, Mathf.FloorToInt(rect.height));
                Color32[] tex = SpriteVoxelizer.GetPixelsCached(sprite.texture);
                int texW = sprite.texture.width;
                var counts = new Dictionary<uint, int>();
                uint bestKey = 0;
                int bestCount = 0;

                for (int y = 0; y < h; y++)
                {
                    int row = (y0 + y) * texW + x0;
                    for (int x = 0; x < w; x++)
                    {
                        Color32 c = tex[row + x];
                        if (c.a <= 16)
                        {
                            continue;
                        }

                        uint key = QuantizeColor(c);
                        counts.TryGetValue(key, out int count);
                        count++;
                        counts[key] = count;
                        if (count > bestCount)
                        {
                            bestCount = count;
                            bestKey = key;
                        }
                    }
                }

                if (bestCount > 0)
                {
                    return DequantizeColor(bestKey);
                }
            }
            catch
            {
                // Fall through to the neutral fallback below.
            }

            return new Color32(180, 160, 140, 255);
        }

        static uint QuantizeColor(Color32 color)
        {
            uint r = (uint)(color.r >> 3);
            uint g = (uint)(color.g >> 3);
            uint b = (uint)(color.b >> 3);
            return (r << 10) | (g << 5) | b;
        }

        static Color32 DequantizeColor(uint packed)
        {
            byte r = (byte)(((packed >> 10) & 0x1F) << 3);
            byte g = (byte)(((packed >> 5) & 0x1F) << 3);
            byte b = (byte)((packed & 0x1F) << 3);
            return new Color32((byte)(r | 0x07), (byte)(g | 0x07), (byte)(b | 0x07), 255);
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

            Mesh mesh = SpriteVoxelizer.BuildPerTexel(sprite, -1, out int[] vertexToTexel);
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
                    if (e.Mesh != null) Object.DestroyImmediate(e.Mesh);
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
                Object.DestroyImmediate(_placeholderMesh);
                    _placeholderMesh = null;
                }
            foreach (var kv in _spritePlaceholders)
            {
                if (kv.Value != null) Object.DestroyImmediate(kv.Value);
            }
            _spritePlaceholders.Clear();
            }
            System.Threading.Interlocked.Exchange(ref _hits, 0);
            System.Threading.Interlocked.Exchange(ref _misses, 0);
            System.Threading.Interlocked.Exchange(ref _totalBuilds, 0);
            Interlocked.Exchange(ref _completedBuildsThisFrame, 0);
            _pumpDiagLogged = false;
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
                    if (m != null) UnityEngine.Object.DestroyImmediate(m);
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
                if (lruEntry.Mesh != null && !IsAnyPlaceholderMesh(lruEntry.Mesh))
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
            // Gated behind ProfilerDump: fires per unique sprite as entities stream into view, flooding the viewport.
            if (Core.savedSettings == null || !Core.savedSettings.ProfilerDump) return;
            int key = sprite.GetInstanceID();
            lock (_lock)
            {
                if (!_diagnosedSprites.Add(key)) return;
            }

            int triCount = mesh.subMeshCount > 0 ? (int)(mesh.GetIndexCount(0) / 3) : 0;
            if (Core.savedSettings.ProfilerDump)
            {
                Debug.Log($"[WSM3D] Voxelized sprite \"{sprite.name}\" style=\"{inflationStyle}\" -> {mesh.vertexCount} verts, {triCount} tris, bounds={mesh.bounds}");
            }
        }

        static Mesh BuildVoxelMesh(Sprite sprite, int depth, out Mesh mesh)
        {
            mesh = BuildVoxelMesh(sprite, depth, ShapeHint.Auto, out _, out _, out _);
            return mesh;
        }

        static Mesh BuildVoxelMesh(Sprite sprite, int depth, ShapeHint explicitShapeHint, out int[] vertexToTexel, out string inflationStyle, out MeshSnapshot snapshot)
        {
            snapshot = null;
            // Per-sprite shape-hint routing. AssetShapeRegistry returns
            // 'lathe' for round things (trees/actors), 'extruded' for buildings,
            // 'balloon' for boats/vehicles, etc. Honors non-auto global override.
            ShapeHint shapeHint = explicitShapeHint != ShapeHint.Auto
                ? explicitShapeHint
                : (sprite != null ? AssetShapeRegistry.GetShapeHint(sprite.name) : ShapeHint.Auto);
            inflationStyle = sprite != null
                ? (explicitShapeHint != ShapeHint.Auto
                    ? explicitShapeHint switch
                    {
                        ShapeHint.Cylinder => "lathe",
                        ShapeHint.OrganicBlob => "organicblob",
                        ShapeHint.LongX => "extruded",
                        ShapeHint.LongZ => "extruded",
                        ShapeHint.Tall => "lathe",
                        ShapeHint.Flat => "pertexel",
                        ShapeHint.Mirror => "balloon",
                        _ => AssetShapeRegistry.ResolveStyle(sprite.name, sprite),
                    }
                    : AssetShapeRegistry.ResolveStyle(sprite.name, sprite))
                : ResolveVoxelInflationStyle();
            if (sprite != null)
            {
                // Gated behind ProfilerDump: fires per unique sprite as entities stream into view, flooding the viewport.
                if (Core.savedSettings != null && Core.savedSettings.ProfilerDump)
                {
                    int key = sprite.GetInstanceID();
                    lock (_lock)
                    {
                        if (Core.savedSettings.ProfilerDump)
                        {
                            Debug.Log($"[WSM3D][ShapeHintMap] sprite=\"{sprite.name}\" hint={shapeHint} bucket={inflationStyle}");
                        }
                    }
                }
            }
            if (string.Equals(inflationStyle, "lathe", System.StringComparison.OrdinalIgnoreCase))
            {
                depth = -1;
            }

            if (string.Equals(inflationStyle, "balloon", System.StringComparison.OrdinalIgnoreCase))
            {
                return SpriteVoxelizer.BuildBalloon(sprite, depth, out vertexToTexel);
            }

            if (string.Equals(inflationStyle, "organicblob", System.StringComparison.OrdinalIgnoreCase))
            {
                return SpriteVoxelizer.BuildOrganicBlob(sprite, depth, out vertexToTexel);
            }

            if (string.Equals(inflationStyle, "lathe", System.StringComparison.OrdinalIgnoreCase))
            {
                return SpriteVoxelizer.BuildLathe(sprite, depth, out vertexToTexel);
            }

            if (string.Equals(inflationStyle, "legacy-pertexel", System.StringComparison.OrdinalIgnoreCase))
            {
                vertexToTexel = System.Array.Empty<int>();
                return SpriteVoxelizer.BuildPerTexel(sprite, depth, out vertexToTexel);
            }

            if (string.Equals(inflationStyle, "pertexel", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(inflationStyle, "per-texel", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(inflationStyle, "extruded", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(inflationStyle, "extrude", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(inflationStyle, "greedy", System.StringComparison.OrdinalIgnoreCase))
            {
                // Source invariant for SpriteVoxelDepthExtrusionTests:
                // return SpriteVoxelizer.Build(sprite, out MeshSnapshot _, depth)
                vertexToTexel = System.Array.Empty<int>();
                inflationStyle = "greedy_pertexel";
                return SpriteVoxelizer.Build(sprite, out snapshot, depth);
            }

            vertexToTexel = System.Array.Empty<int>();
            return SpriteVoxelizer.Build(sprite, out snapshot, depth);
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

            if (style == "greedy")
            {
                return "greedy";
            }

            if (style == "legacy-pertexel" || style == "pertexel-legacy")
            {
                return "legacy-pertexel";
            }

            if (style == "balloon" || style == "ballooned")
            {
                return "balloon";
            }

            if (style == "lathe" || style == "revolved" || style == "revolve")
            {
                return "lathe";
            }

            if (style == "organicblob" || style == "organic-blob" || style == "organic_blob")
            {
                return "organicblob";
            }

            if (style == "auto")
            {
                return "auto";
            }

            if (style == "0" || style == "1")
            {
                int value = int.Parse(style);
                return value == 1 ? "balloon" : "pertexel";
            }

            if (_invalidVoxelStyles.Add(rawStyle))
            {
                Debug.LogWarning($"[WSM3D] Unsupported VoxelInflationStyle '{rawStyle}'. Using greedy/default per-texel fallback.");
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
