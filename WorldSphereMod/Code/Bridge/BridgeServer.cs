using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;

namespace WorldSphereMod.Bridge
{
    public sealed class BridgeServer : MonoBehaviour
    {
        const int Port = 8766;
        static readonly int[] CandidatePorts = { Port, 8767, 8768, 8769 };
        static readonly BindingFlags SettingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
        const string VoxelDumpRoot = @"C:\Users\koosh\.claude\jobs\b012a2c2";

        static readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        HttpListener? _listener;
        Thread? _listenerThread;
        volatile bool _running;
        static int _mainThreadId;
        int _boundPort = Port;

        public static bool EnableFailed;

        static UnityEngine.GameObject _rootHost;
        static int _instanceGeneration;
        int _myGeneration;

        /// <summary>Last-known values sampled on the Unity main thread for lock-free /health.</summary>
        static string _cachedHealthVersion = "unknown";
        static bool _cachedIsWorld3D;

        /// <summary>Last-known perf counters for lock-free /telemetry (PlayCUA assert_telemetry).</summary>
        static float _cachedFrameMs;
        static float _cachedVoxelCacheHit;
        static float _cachedImpostorCacheHit;
        static long _cachedDrawCalls;
        static long _cachedLastNonZeroDrawCalls;
        static long _cachedInstances;
        static int _telemetryCacheFrame = -1;
        static int _telemetryProbeSuccessFrame = -1;

        /// <summary>
        /// When true, <see cref="Voxel.VoxelFrameDriver"/> submits the debug sanity probe each
        /// LateUpdate so /telemetry lastNonZeroDrawCalls is populated for PlayCUA without
        /// blocking POST /settings on a stalled main-thread queue.
        /// </summary>
        public static volatile bool LiveTelemetryProbeEnabled;

        public static void EnsureCreated()
        {
            try
            {
                // Unity's DontDestroyOnLoad can fail if WorldBox uses
                // LoadSceneMode.Single — root GameObject still dies. Handle the
                // null-after-transition case by recreating cleanly.
                bool needsCreate = _rootHost == null;
                if (!needsCreate)
                {
                    try { needsCreate = _rootHost.GetComponent<BridgeServer>() == null; }
                    catch { needsCreate = true; } // accessing destroyed object throws
                }
                if (!needsCreate)
                {
                    EnsureVoxelFrameDriverOnBridgeHost();
                    return;
                }
                _rootHost = new UnityEngine.GameObject("WSM3D.BridgeServer");
                UnityEngine.Object.DontDestroyOnLoad(_rootHost);
                _rootHost.AddComponent<BridgeServer>();
                EnsureVoxelFrameDriverOnBridgeHost();
                Debug.Log("[WSM3D][Bridge] (re)created root host + BridgeServer component");
            }
            catch (Exception ex)
            {
                EnableFailed = true;
                Debug.LogWarning("[WSM3D][Bridge] failed to create bridge server: " + ex.Message);
            }
        }

        static void EnsureVoxelFrameDriverOnBridgeHost()
        {
            if (_rootHost == null) return;
            try
            {
                if (_rootHost.GetComponent<WorldSphereMod.Voxel.VoxelFrameDriver>() != null) return;
                _rootHost.AddComponent<WorldSphereMod.Voxel.VoxelFrameDriver>();
                Debug.Log("[WSM3D][Bridge] attached VoxelFrameDriver to bridge DDOL host for end-of-frame flush");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WSM3D][Bridge] failed to attach VoxelFrameDriver on bridge host: " + ex.Message);
            }
        }

        void Awake()
        {
            CaptureMainThread();
            // Survive scene transitions (save load destroys Mod.Object's scene → bridge dies → main-thread queue stops draining → all HTTP requests time out at 5s default(T)).
            try { UnityEngine.Object.DontDestroyOnLoad(gameObject); } catch { /* root-only requirement */ }
            StartListener();
        }

        /// <summary>Records Unity's main thread id once; safe to call from any per-frame vanilla hook.</summary>
        public static void CaptureMainThread()
        {
            if (_mainThreadId == 0)
            {
                _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            }
        }

        public static void DrainStaticQueue()
        {
            // Authoritative refresh: only called from Unity main-thread Harmony hooks.
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            RefreshHealthCache();
            while (_mainThreadQueue.TryDequeue(out Action? work))
            {
                try { work?.Invoke(); }
                catch (Exception ex) { Debug.LogWarning("[WSM3D][Bridge] main-thread work failed: " + ex.Message); }
            }

            int frame = Time.frameCount;
            if (frame != _telemetryCacheFrame)
            {
                _telemetryCacheFrame = frame;
                RefreshTelemetryCache();
            }

            TryRunLiveTelemetryProbeEndOfFrame();
        }

        /// <summary>
        /// Submit the optional sanity probe, flush batched draws, and refresh /telemetry cache.
        /// Call from MapBox.renderStuff Postfix and from <see cref="LateUpdate"/> — not from
        /// <see cref="Update"/> (emit postfixes have not run yet).
        /// </summary>
        public static void TryRunLiveTelemetryProbeEndOfFrame()
        {
            int frame = Time.frameCount;
            if (frame == _telemetryProbeSuccessFrame) return;

            try
            {
                if (Core.savedSettings == null) return;
                if (!LiveTelemetryProbeEnabled && !Core.savedSettings.DebugSanityCube) return;
                if (!Core.IsWorld3D && !LiveTelemetryProbeEnabled) return;

                WorldSphereMod.Voxel.SanityTestCube.Draw();
                if (WorldSphereMod.Voxel.MeshInstanceBatcher.HasPendingSubmissions)
                {
                    WorldSphereMod.Voxel.VoxelRender.Flush();
                    WorldSphereMod.Voxel.VoxelMeshCache.DrainPendingDestroy();
                }

                RefreshTelemetryCache();
                if (_cachedLastNonZeroDrawCalls > 0L)
                {
                    _telemetryProbeSuccessFrame = frame;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WSM3D][Bridge] telemetry probe failed: " + ex.Message);
            }
        }

        static void RefreshHealthCache()
        {
            try
            {
                _cachedHealthVersion = Core.savedSettings != null ? Core.savedSettings.Version : "unknown";
                _cachedIsWorld3D = Core.IsWorld3D;
            }
            catch
            {
                // Unity objects may be mid-teardown during scene transitions.
            }
        }

        /// <summary>Sample perf counters after MeshInstanceBatcher.Flush for lock-free /telemetry.</summary>
        public static void RefreshTelemetryCache()
        {
            try
            {
                _cachedFrameMs = Time.unscaledDeltaTime * 1000f;
                _cachedVoxelCacheHit = SafeHitRate(
                    () => WorldSphereMod.Voxel.VoxelMeshCache.HitCount,
                    () => WorldSphereMod.Voxel.VoxelMeshCache.MissCount);
                _cachedImpostorCacheHit = SafeHitRate(
                    () => WorldSphereMod.LOD.ImpostorBillboard.HitCount,
                    () => WorldSphereMod.LOD.ImpostorBillboard.MissCount);
                _cachedDrawCalls = SafeLong(() => WorldSphereMod.Voxel.MeshInstanceBatcher.FrameDrawCalls);
                if (_cachedDrawCalls > 0L)
                {
                    _cachedLastNonZeroDrawCalls = _cachedDrawCalls;
                }
                _cachedInstances = SafeLong(() => WorldSphereMod.Voxel.MeshInstanceBatcher.FrameInstances);
            }
            catch
            {
                // Unity objects may be mid-teardown during scene transitions.
            }
        }

        void Update() => DrainStaticQueue();
        void LateUpdate() => DrainStaticQueue();
        void FixedUpdate() => DrainStaticQueue();

        void OnDestroy()
        {
            // EnsureCreated can spawn a new BridgeServer before the old host's OnDestroy
            // runs; stopping the listener here would kill the replacement's accept loop.
            if (_myGeneration < _instanceGeneration) return;
            StopListener();
        }

        void StartListener()
        {
            foreach (int port in CandidatePorts)
            {
                if (TryStartListener(port)) return;
            }

            EnableFailed = true;
        }

        bool TryStartListener(int port)
        {
            HttpListener? listener = null;
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();

                _listener = listener;
                _running = true;
                _boundPort = port;
                _listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "WSM3D BridgeServer" };
                _listenerThread.Start();
                WorldSphereMod.Voxel.SanityTestCube.Reset();
                LiveTelemetryProbeEnabled = true;
                Debug.Log($"[WSM3D][Bridge] HTTP RPC listening on 127.0.0.1:{port}");
                return true;
            }
            catch (HttpListenerException ex)
            {
                return HandleBindFailure(listener, port, ex);
            }
            catch (SocketException ex)
            {
                return HandleBindFailure(listener, port, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return HandleBindFailure(listener, port, ex);
            }
            catch (Exception ex)
            {
                return HandleBindFailure(listener, port, ex);
            }
        }

        bool HandleBindFailure(HttpListener? listener, int port, Exception ex)
        {
            EnableFailed = true;
            Debug.LogWarning($"[WSM3D][Bridge] failed to bind HTTP listener on 127.0.0.1:{port}: {ex.Message}");
            if (_listener == listener) _listener = null;
            _listenerThread = null;
            _running = false;
            try { listener?.Close(); } catch { }
            return false;
        }

        void StopListener()
        {
            _running = false;
            LiveTelemetryProbeEnabled = false;
            try { WorldSphereMod.Voxel.SanityTestCube.Reset(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            if (_listenerThread != null && _listenerThread.IsAlive && Thread.CurrentThread.ManagedThreadId != _listenerThread.ManagedThreadId)
                _listenerThread.Join(500);
            _listenerThread = null;
            _listener = null;
        }

        void ListenLoop()
        {
            while (_running)
            {
                HttpListenerContext? context = null;
                try { context = _listener?.GetContext(); }
                catch (HttpListenerException) { if (!_running) return; }
                catch (ObjectDisposedException) { return; }
                catch (Exception ex)
                {
                    if (_running) Debug.LogWarning("[WSM3D][Bridge] listener loop error on 127.0.0.1:" + _boundPort + ": " + ex.Message);
                    continue;
                }
                if (context != null) ProcessRequest(context);
            }
        }

        void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                string method = context.Request.HttpMethod ?? string.Empty;
                string path = context.Request.Url?.AbsolutePath ?? "/";
                if (path.Length > 1 && path.EndsWith("/")) path = path.TrimEnd('/');

                if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteJson(context.Response, BuildHealthPayload());
                        return;
                    }
                    if (string.Equals(path, "/telemetry", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteJson(context.Response, BuildTelemetryPayload());
                        return;
                    }
                    if (string.Equals(path, "/settings", StringComparison.OrdinalIgnoreCase)) { WriteRawJson(context.Response, InvokeOnMainThread(BuildSettingsJson)); return; }
                    if (string.Equals(path, "/voxel/sprite", StringComparison.OrdinalIgnoreCase))
                    {
                        string spriteName = context.Request.QueryString["name"] ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(spriteName))
                        {
                            WriteJson(context.Response, InvokeOnMainThread(BuildVoxelSpriteListPayload));
                            return;
                        }

                        HttpStatusCode statusCode = HttpStatusCode.OK;
                        object payload = InvokeOnMainThread(() => BuildVoxelSpritePayload(spriteName, out statusCode));
                        WriteJson(context.Response, payload, statusCode);
                        return;
                    }
                    if (string.Equals(path, "/voxel/stats", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteJson(context.Response, InvokeOnMainThread(BuildVoxelStatsPayload));
                        return;
                    }
                    if (string.Equals(path, "/voxel/queue", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteJson(context.Response, InvokeOnMainThread(BuildVoxelQueuePayload));
                        return;
                    }
                    if (string.Equals(path, "/memory", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteJson(context.Response, InvokeOnMainThread(BuildMemoryPayload));
                        return;
                    }
                    if (string.Equals(path, "/voxel/actor", StringComparison.OrdinalIgnoreCase))
                    {
                        string indexText = context.Request.QueryString["index"] ?? "0";
                        WriteJson(context.Response, InvokeOnMainThread(() => BuildVoxelActorPayload(indexText)));
                        return;
                    }
                    if (string.Equals(path, "/voxel/diff", StringComparison.OrdinalIgnoreCase))
                    {
                        string baselinePath = context.Request.QueryString["baseline"] ?? string.Empty;
                        WriteJson(context.Response, InvokeOnMainThread(() => BuildVoxelDiffPayload(baselinePath)));
                        return;
                    }
                    if (path.StartsWith("/phase/", StringComparison.OrdinalIgnoreCase))
                    {
                        string phaseName = Uri.UnescapeDataString(path.Substring("/phase/".Length));
                        WriteJson(context.Response, InvokeOnMainThread(() => BuildPhasePayload(phaseName)));
                        return;
                    }
                }
                else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && path.StartsWith("/settings/", StringComparison.OrdinalIgnoreCase))
                {
                    string key = path.Substring("/settings/".Length);
                    string rawValue = context.Request.QueryString["value"] ?? string.Empty;
                    WriteJson(context.Response, UpdateSettingQueued(key, rawValue));
                    return;
                }
                else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && string.Equals(path, "/actions/load_save", StringComparison.OrdinalIgnoreCase))
                {
                    string slotText = context.Request.QueryString["slot"] ?? string.Empty;
                    WriteJson(context.Response, LoadSaveQueued(slotText));
                    return;
                }
                else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && (string.Equals(path, "/actions/screenshot", StringComparison.OrdinalIgnoreCase) || string.Equals(path, "/screenshot/now", StringComparison.OrdinalIgnoreCase)))
                {
                    string outputPath = context.Request.QueryString["path"] ?? string.Empty;
                    WriteJson(context.Response, CaptureScreenshot(outputPath));
                    return;
                }
                else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && string.Equals(path, "/voxel/dump_all", StringComparison.OrdinalIgnoreCase))
                {
                    WriteJson(context.Response, InvokeOnMainThread(DumpVoxelMeshes));
                    return;
                }
                else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && string.Equals(path, "/diag/dump_now", StringComparison.OrdinalIgnoreCase))
                {
                    WriteJson(context.Response, InvokeOnMainThread(ForceDiagDumpNow));
                    return;
                }
                else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && string.Equals(path, "/texturepack/import", StringComparison.OrdinalIgnoreCase))
                {
                    string packPath = context.Request.QueryString["path"] ?? string.Empty;
                    WriteJson(context.Response, InvokeOnMainThread(() => BuildTexturePackImportPayload(packPath)));
                    return;
                }

                else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && string.Equals(path, "/actions/spawn_units", StringComparison.OrdinalIgnoreCase))
                {
                    string countText = context.Request.QueryString["count"] ?? "10";
                    string race = context.Request.QueryString["race"] ?? "human";
                    WriteJson(context.Response, SpawnUnitsQueued(countText, race));
                    return;
                }
                else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && string.Equals(path, "/actions/generate_world", StringComparison.OrdinalIgnoreCase))
                {
                    WriteJson(context.Response, GenerateWorldQueued());
                    return;
                }

                WriteJson(context.Response, new { ok = false, error = "not_found", path, method }, HttpStatusCode.NotFound);
            }
            catch (Exception ex)
            {
                WriteJson(context.Response, new { ok = false, error = ex.Message }, HttpStatusCode.InternalServerError);
            }
        }

        object BuildHealthPayload() => new
        {
            ok = true,
            bridgeAlive = true,
            listenerPort = _boundPort,
            listenerThreadAlive = _listenerThread != null && _listenerThread.IsAlive,
            version = _cachedHealthVersion,
            isWorld3D = _cachedIsWorld3D,
        };

        object BuildVoxelSpritePayload(string spriteName, out HttpStatusCode statusCode)
        {
            statusCode = HttpStatusCode.OK;
            if (string.IsNullOrWhiteSpace(spriteName))
            {
                statusCode = HttpStatusCode.BadRequest;
                return new { ok = false, error = "missing_sprite_name" };
            }

            Sprite sprite = FindSpriteByName(spriteName);
            if (sprite == null)
            {
                statusCode = HttpStatusCode.NotFound;
                return new { ok = false, error = "unknown_sprite", name = spriteName };
            }

            WorldSphereMod.Voxel.VoxelMeshCache.Get(sprite);
            // Back-fill name index for sprites that were cached before the name-index
            // landed (pre-f39fb9b sessions).
            WorldSphereMod.Voxel.VoxelMeshCache.RegisterSpriteName(sprite);
            // Try name index first (new), fall back to sprite-ref lookup for back-compat.
            bool found = WorldSphereMod.Voxel.VoxelMeshCache.TryDescribe(spriteName, out WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot snapshot);
            if (!found || snapshot == null)
            {
                found = WorldSphereMod.Voxel.VoxelMeshCache.TryDescribe(sprite, out snapshot);
            }
            if (!found || snapshot == null)
            {
                statusCode = HttpStatusCode.NotFound;
                return new { ok = false, error = "mesh_not_cached", name = spriteName, spriteId = sprite.GetInstanceID() };
            }

            return BuildVoxelSnapshotPayload(snapshot);
        }

        object BuildVoxelSpriteListPayload()
        {
            List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot> snapshots = WorldSphereMod.Voxel.VoxelMeshCache.DescribeAll();
            var names = new List<string>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                string name = snapshots[i] != null ? snapshots[i].spriteName : null;
                if (!string.IsNullOrEmpty(name) && !names.Contains(name))
                {
                    names.Add(name);
                }
            }
            names.Sort(StringComparer.Ordinal);

            return new
            {
                ok = true,
                count = names.Count,
                spriteNames = names
            };
        }

        object BuildVoxelStatsPayload() => new
        {
            ok = true,
            cache = new
            {
                size = WorldSphereMod.Voxel.VoxelMeshCache.Count,
                hits = WorldSphereMod.Voxel.VoxelMeshCache.HitCount,
                misses = WorldSphereMod.Voxel.VoxelMeshCache.MissCount
            }
        };


        object BuildMemoryPayload()
        {
            long gc = System.GC.GetTotalMemory(forceFullCollection: false);
            long alloc = 0;
            try { alloc = System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64; } catch { }
            return new
            {
                ok = true,
                gcBytes = gc,
                gcMB = gc / (1024.0 * 1024.0),
                processBytes = alloc,
                processMB = alloc / (1024.0 * 1024.0),
                generation0CollectCount = System.GC.CollectionCount(0),
                generation1CollectCount = System.GC.CollectionCount(1),
                generation2CollectCount = System.GC.CollectionCount(2)
            };
        }

        object BuildVoxelQueuePayload() => new
        {
            ok = true,
            pendingBuilds = WorldSphereMod.Voxel.VoxelMeshCache.PendingBuilds,
            completedThisFrame = WorldSphereMod.Voxel.VoxelMeshCache.CompletedBuildsThisFrame,
            totalBuilds = WorldSphereMod.Voxel.VoxelMeshCache.TotalBuilds
        };

        object BuildVoxelActorPayload(string indexText)
        {
            if (!BridgeSettingParser.TryParseNonNegativeInt(indexText, out int index))
            {
                return new { ok = false, error = "invalid_index", index = indexText };
            }

            ActorManager manager = World.world != null ? World.world.units : null;
            if (manager == null || manager.visible_units.array == null)
            {
                return new { ok = false, error = "world_not_ready", index };
            }

            int visibleCount = manager.visible_units.count;
            if (index >= visibleCount)
            {
                return new { ok = false, error = "index_out_of_range", index, count = visibleCount };
            }

            var samples = new List<object>();
            int end = Math.Min(visibleCount, index + 5);
            for (int i = index; i < end; i++)
            {
                Actor actor = manager.visible_units.array[i];
                Vector3 position = manager.render_data.positions[i];
                Vector3 rotation = manager.render_data.rotations[i];
                Vector3 scale = manager.render_data.scales[i];
                Sprite sprite = manager.render_data.main_sprites[i];

                WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot meshSnapshot = null;
                if (sprite != null)
                {
                    WorldSphereMod.Voxel.VoxelMeshCache.Get(sprite);
                    WorldSphereMod.Voxel.VoxelMeshCache.TryDescribe(sprite, out meshSnapshot);
                }

                samples.Add(new
                {
                    index = i,
                    actorId = actor != null && actor.asset != null ? actor.asset.id : null,
                    spriteName = sprite != null ? sprite.name : null,
                    trs = new
                    {
                        position = new { x = position.x, y = position.y, z = position.z },
                        rotation = new { x = rotation.x, y = rotation.y, z = rotation.z },
                        scale    = new { x = scale.x,    y = scale.y,    z = scale.z    }
                    },
                    cachedMesh = meshSnapshot != null ? BuildVoxelSnapshotPayload(meshSnapshot) : null
                });
            }

            return new
            {
                ok = true,
                startIndex = index,
                count = samples.Count,
                actors = samples
            };
        }

        object BuildVoxelDiffPayload(string baselinePath)
        {
            if (string.IsNullOrWhiteSpace(baselinePath))
            {
                return new { ok = false, error = "missing_baseline" };
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(baselinePath);
            }
            catch (Exception ex)
            {
                return new { ok = false, error = "invalid_baseline_path", path = baselinePath, message = ex.Message };
            }

            if (!File.Exists(fullPath))
            {
                return new { ok = false, error = "missing_baseline_file", path = fullPath };
            }

            List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot> baseline = LoadVoxelSnapshots(fullPath);
            if (baseline == null)
            {
                return new { ok = false, error = "invalid_baseline_json", path = fullPath };
            }

            List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot> current = WorldSphereMod.Voxel.VoxelMeshCache.DescribeAll();
            Dictionary<string, List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot>> baselineByName = GroupSnapshotsByName(baseline);
            Dictionary<string, List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot>> currentByName = GroupSnapshotsByName(current);

            var added = new List<object>();
            var removed = new List<object>();
            var changed = new List<object>();

            foreach (KeyValuePair<string, List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot>> kv in currentByName)
            {
                if (!baselineByName.TryGetValue(kv.Key, out List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot> baselineMatches))
                {
                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        added.Add(BuildSnapshotSummary(kv.Value[i]));
                    }
                    continue;
                }

                int pairCount = Math.Min(kv.Value.Count, baselineMatches.Count);
                for (int i = 0; i < pairCount; i++)
                {
                    var currentSnapshot = kv.Value[i];
                    var baselineSnapshot = baselineMatches[i];
                    List<string> changedFields = GetChangedFields(currentSnapshot, baselineSnapshot);
                    if (changedFields.Count > 0)
                    {
                        changed.Add(new
                        {
                            key = kv.Key,
                            index = i,
                            changedFields,
                            current = BuildSnapshotSummary(currentSnapshot),
                            baseline = BuildSnapshotSummary(baselineSnapshot)
                        });
                    }
                }

                if (kv.Value.Count > baselineMatches.Count)
                {
                    for (int i = baselineMatches.Count; i < kv.Value.Count; i++)
                    {
                        added.Add(BuildSnapshotSummary(kv.Value[i]));
                    }
                }
            }

            foreach (KeyValuePair<string, List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot>> kv in baselineByName)
            {
                if (!currentByName.TryGetValue(kv.Key, out List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot> currentMatches))
                {
                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        removed.Add(BuildSnapshotSummary(kv.Value[i]));
                    }
                    continue;
                }

                if (kv.Value.Count > currentMatches.Count)
                {
                    for (int i = currentMatches.Count; i < kv.Value.Count; i++)
                    {
                        removed.Add(BuildSnapshotSummary(kv.Value[i]));
                    }
                }
            }

            return new
            {
                ok = true,
                baseline = fullPath,
                currentCount = current.Count,
                baselineCount = baseline.Count,
                added,
                removed,
                changed,
                unchangedCount = Math.Max(0, current.Count - added.Count - changed.Count)
            };
        }

        object DumpVoxelMeshes()
        {
            List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot> snapshots = WorldSphereMod.Voxel.VoxelMeshCache.DescribeAll();
            var dump = new VoxelDumpDocument
            {
                generatedAtUtc = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff'Z'", CultureInfo.InvariantCulture),
                meshCount = snapshots.Count,
                meshes = snapshots
            };

            string json = JsonConvert.SerializeObject(dump, Formatting.Indented);
            string directory = VoxelDumpRoot;
            string path = Path.Combine(directory, "voxel-dump-" + dump.generatedAtUtc + ".json");
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return new { ok = false, error = "write_failed", path, message = ex.Message, generatedAtUtc = dump.generatedAtUtc, meshCount = snapshots.Count };
            }

            return new { ok = true, path, generatedAtUtc = dump.generatedAtUtc, meshCount = snapshots.Count };
        }

        static object PodVector3(UnityEngine.Vector3 v) => new { x = v.x, y = v.y, z = v.z };
        static object PodBounds(WorldSphereMod.Voxel.VoxelMeshCache.MeshBoundsSnapshot b)
            => b == null ? null : (object)new { min = new { x = b.min.x, y = b.min.y, z = b.min.z }, max = new { x = b.max.x, y = b.max.y, z = b.max.z } };
        static List<object> PodVertices(List<UnityEngine.Vector3> verts, int max = 64)
        {
            var o = new List<object>(System.Math.Min(verts == null ? 0 : verts.Count, max));
            if (verts == null) return o;
            int n = System.Math.Min(verts.Count, max);
            for (int i = 0; i < n; i++) o.Add(new { x = verts[i].x, y = verts[i].y, z = verts[i].z });
            return o;
        }

        static object BuildVoxelSnapshotPayload(WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot snapshot)
        {
            return new
            {
                ok = true,
                spriteId = snapshot.spriteId,
                spriteName = snapshot.spriteName,
                meshName = snapshot.meshName,
                vertexCount = snapshot.vertexCount,
                triangleCount = snapshot.triangleCount,
                bounds = PodBounds(snapshot.bounds),
                vertices = PodVertices(snapshot.vertices),
                triangles = snapshot.triangles,
                colors = snapshot.colors,
                invariants = snapshot.invariants
            };
        }

        static object BuildSnapshotSummary(WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot snapshot)
        {
            return new
            {
                spriteId = snapshot.spriteId,
                spriteName = snapshot.spriteName,
                meshName = snapshot.meshName,
                vertexCount = snapshot.vertexCount,
                triangleCount = snapshot.triangleCount,
                bounds = PodBounds(snapshot.bounds),
                invariants = snapshot.invariants
            };
        }

        static List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot> LoadVoxelSnapshots(string path)
        {
            string json = File.ReadAllText(path);
            string trimmed = json.TrimStart();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return JsonConvert.DeserializeObject<List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot>>(json);
            }

            VoxelDumpDocument document = JsonConvert.DeserializeObject<VoxelDumpDocument>(json);
            return document != null ? document.meshes : null;
        }

        static Dictionary<string, List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot>> GroupSnapshotsByName(List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot> snapshots)
        {
            var grouped = new Dictionary<string, List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot>>(StringComparer.Ordinal);
            if (snapshots == null) return grouped;

            for (int i = 0; i < snapshots.Count; i++)
            {
                WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot snapshot = snapshots[i];
                string key = snapshot != null && !string.IsNullOrEmpty(snapshot.spriteName) ? snapshot.spriteName : string.Empty;
                if (!grouped.TryGetValue(key, out List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot> list))
                {
                    list = new List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot>();
                    grouped[key] = list;
                }

                list.Add(snapshot);
            }

            foreach (KeyValuePair<string, List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot>> kv in grouped)
            {
                kv.Value.Sort((a, b) =>
                {
                    int idCompare = a.spriteId.CompareTo(b.spriteId);
                    if (idCompare != 0) return idCompare;
                    return string.CompareOrdinal(a.meshName, b.meshName);
                });
            }

            return grouped;
        }

        static List<string> GetChangedFields(WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot current, WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot baseline)
        {
            var changed = new List<string>();
            if (current == null || baseline == null)
            {
                changed.Add("snapshot");
                return changed;
            }

            if (current.vertexCount != baseline.vertexCount) changed.Add("vertexCount");
            if (current.triangleCount != baseline.triangleCount) changed.Add("triangleCount");
            if (!BoundsEqual(current.bounds, baseline.bounds)) changed.Add("bounds");
            if (!VectorListEqual(current.vertices, baseline.vertices)) changed.Add("vertices");
            if (!IntListEqual(current.triangles, baseline.triangles)) changed.Add("triangles");
            if (!ColorListEqual(current.colors, baseline.colors)) changed.Add("colors");
            if (current.invariants == null || baseline.invariants == null)
            {
                changed.Add("invariants");
                return changed;
            }

            if (current.invariants.distinctTriVerts != baseline.invariants.distinctTriVerts) changed.Add("distinctTriVerts");
            if (current.invariants.maxTriIndexLessThanVerts != baseline.invariants.maxTriIndexLessThanVerts) changed.Add("maxTriIndexLessThanVerts");
            if (current.invariants.maxTriIndex != baseline.invariants.maxTriIndex) changed.Add("maxTriIndex");
            return changed;
        }

        static bool BoundsEqual(WorldSphereMod.Voxel.VoxelMeshCache.MeshBoundsSnapshot a, WorldSphereMod.Voxel.VoxelMeshCache.MeshBoundsSnapshot b)
        {
            if (a == null || b == null) return a == b;
            return VectorEqual(a.min, b.min) && VectorEqual(a.max, b.max);
        }

        static bool VectorListEqual(List<Vector3> a, List<Vector3> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!VectorEqual(a[i], b[i])) return false;
            }
            return true;
        }

        static bool IntListEqual(List<int> a, List<int> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        static bool ColorListEqual(List<Color32> a, List<Color32> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!ColorEqual(a[i], b[i])) return false;
            }
            return true;
        }

        static bool VectorEqual(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) < 0.0001f &&
                   Mathf.Abs(a.y - b.y) < 0.0001f &&
                   Mathf.Abs(a.z - b.z) < 0.0001f;
        }

        static bool ColorEqual(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        static Sprite FindSpriteByName(string spriteName)
        {
            Sprite[] sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite sprite = sprites[i];
                if (sprite != null && string.Equals(sprite.name, spriteName, StringComparison.Ordinal))
                {
                    return sprite;
                }
            }

            return null;
        }

        public sealed class VoxelDumpDocument
        {
            public string generatedAtUtc;
            public int meshCount;
            public List<WorldSphereMod.Voxel.VoxelMeshCache.MeshSnapshot> meshes;
        }

        object BuildTelemetryPayload() => new
        {
            frameMs = _cachedFrameMs,
            voxelCacheHit = _cachedVoxelCacheHit,
            impostorCacheHit = _cachedImpostorCacheHit,
            drawCalls = _cachedDrawCalls,
            lastNonZeroDrawCalls = _cachedLastNonZeroDrawCalls,
            instances = _cachedInstances,
        };

        string BuildSettingsJson() => JsonConvert.SerializeObject(Core.savedSettings ?? new SavedSettings(), Formatting.Indented);

        /// <summary>
        /// Apply a settings mutation by parsing on the listener thread and deferring Unity
        /// mutation + persistence onto the main thread queue.
        /// </summary>
        object UpdateSettingQueued(string key, string rawValue)
        {
            if (string.IsNullOrWhiteSpace(key)) return new { ok = false, error = "missing_setting_key" };
            FieldInfo? field = typeof(SavedSettings).GetField(key, SettingFlags);
            if (field == null) return new { ok = false, error = "unknown_setting", key };
            if (!BridgeSettingParser.TryParseSettingValue(field.FieldType, rawValue, out object? parsed, out string parseError))
                return new { ok = false, error = parseError, key, value = rawValue };

            string fieldName = field.Name;
            bool invalidateVoxel = fieldName == "VoxelInflationStyle" || fieldName == "VoxelMeshSmoothing" || fieldName == "SmoothingIterations" || fieldName == "VoxelScaleMultiplier" || fieldName == "VoxelSpriteDepth" || fieldName == "VoxelLuminanceDepth" || fieldName == "VoxelNeutralLuminance" || fieldName == "VoxelShadowRecession" || fieldName == "VoxelColorTonemap" || fieldName == "ForceFallbackDrawPath";
            bool applyPhase = field.FieldType == typeof(bool);
            bool phaseValue = applyPhase && parsed is bool b && b;

            _mainThreadQueue.Enqueue(() =>
            {
                try
                {
                    if (Core.savedSettings == null)
                    {
                        Debug.LogWarning("[WSM3D][Bridge] deferred setting apply skipped because savedSettings is null for " + fieldName);
                        return;
                    }

                    field.SetValue(Core.savedSettings, parsed);
                    try { Core.SaveSettings(); } catch (Exception ex) { Debug.LogWarning("[WSM3D][Bridge] SaveSettings failed: " + ex.Message); }
                    if (applyPhase) Core.ApplyPhaseToggle(fieldName, phaseValue);
                    if (invalidateVoxel)
                    {
                        try { WorldSphereMod.Voxel.VoxelMeshCache.Clear(); } catch { }
                        try { WorldSphereMod.Voxel.VoxelRender.Reset(); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WSM3D][Bridge] deferred setting apply failed for " + fieldName + ": " + ex.Message);
                }
            });

            return new { ok = true, key = fieldName, value = parsed, queued = true };
        }


        /// <summary>
        /// Queue save load on the main thread and return immediately. Synchronous InvokeOnMainThread
        /// often timed out during loadWorld (5s), serializing null to PlayCUA as non-dict response.
        /// </summary>
        object LoadSaveQueued(string slotText)
        {
            if (!BridgeSettingParser.TryParseNonNegativeInt(slotText, out int slot))
            {
                return new { ok = false, error = "invalid_slot", slot = slotText };
            }

            string path = FindSavePath(slot);
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return new { ok = false, error = "missing_save", slot, path };
            }

            int queuedSlot = slot;
            string queuedPath = path;
            _mainThreadQueue.Enqueue(() =>
            {
                try
                {
                    if (World.world == null || World.world.save_manager == null)
                    {
                        Debug.LogWarning("[WSM3D][Bridge] load_save skipped: world_not_ready slot=" + queuedSlot);
                        return;
                    }

                    SaveManager.setCurrentPathAndId(queuedPath, queuedSlot);
                    World.world.save_manager.prepareLoading();
                    World.world.save_manager.loadWorld(queuedPath, false);
                    // loadWorld Postfix also runs survival; belt-and-suspenders if patch order differs.
                    CaptureMainThread();
                    EnsureCreated();
                    DrainStaticQueue();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WSM3D][Bridge] load_save failed slot=" + queuedSlot + ": " + ex.Message);
                }
            });

            return new { ok = true, slot, path, queued = true };
        }

        object SpawnUnitsQueued(string countText, string race)
        {
            if (!BridgeSettingParser.TryParseNonNegativeInt(countText, out int count))
                return new { ok = false, error = "invalid_count", count = countText };
            count = Math.Min(count, 200);

            _mainThreadQueue.Enqueue(() =>
            {
                try
                {
                    if (World.world == null || MapBox.instance == null)
                    {
                        Debug.LogWarning("[WSM3D][Bridge] spawn_units skipped: world_not_ready");
                        return;
                    }

                    var mapBox = MapBox.instance;
                    int mapW = MapBox.width;
                    int mapH = MapBox.height;
                    int spawned = 0;
                    var rng = new System.Random();

                    for (int i = 0; i < count; i++)
                    {
                        int x = rng.Next(mapW / 4, 3 * mapW / 4);
                        int y = rng.Next(mapH / 4, 3 * mapH / 4);
                        WorldTile tile = mapBox.GetTile(x, y);
                        if (tile == null || tile.Type.ocean) continue;
                        try
                        {
                            if (mapBox.units == null) { Debug.LogWarning("[WSM3D][Bridge] spawn_units: mapBox.units is null"); break; }
                            mapBox.units.createNewUnit(race, tile);
                            spawned++;
                        }
                        catch (Exception spawnEx) { Debug.LogWarning($"[WSM3D][Bridge] spawn_units[{i}]: {spawnEx.GetType().Name}: {spawnEx.Message}\n{spawnEx.StackTrace}"); break; }
                    }
                    Debug.Log($"[WSM3D][Bridge] spawn_units: spawned {spawned}/{count} {race} units");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WSM3D][Bridge] spawn_units failed: " + ex.Message);
                }
            });

            return new { ok = true, count, race, queued = true };
        }

        object GenerateWorldQueued()
        {
            _mainThreadQueue.Enqueue(() =>
            {
                try
                {
                    if (MapBox.instance == null)
                    {
                        Debug.LogWarning("[WSM3D][Bridge] generate_world skipped: MapBox not ready");
                        return;
                    }
                    MapBox.instance.generateNewMap();
                    Debug.Log("[WSM3D][Bridge] generate_world: new map generated");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WSM3D][Bridge] generate_world failed: " + ex.Message);
                }
            });
            return new { ok = true, queued = true };
        }

        object CaptureScreenshot(string outputPath)
        {
            string requestPath = string.IsNullOrWhiteSpace(outputPath) ? WorldSphereMod.ScreenshotCapture.BuildDefaultPath() : outputPath;
            ManualResetEventSlim completed = new ManualResetEventSlim(false);
            object result = new { ok = false, error = "pending", path = Path.GetFullPath(requestPath) };
            Exception? error = null;

            _mainThreadQueue.Enqueue(() =>
            {
                try
                {
                    StartCoroutine(WorldSphereMod.ScreenshotCapture.CaptureCoroutine(requestPath, (savedPath, success, message) =>
                    {
                        result = success
                            ? new { ok = true, path = savedPath }
                            : new { ok = false, error = message, path = savedPath };
                        completed.Set();
                    }));
                }
                catch (Exception ex)
                {
                    error = ex;
                    completed.Set();
                }
            });

            if (!completed.Wait(10000))
            {
                return new { ok = false, error = "timeout", path = Path.GetFullPath(requestPath) };
            }

            if (error != null)
            {
                return new { ok = false, error = error.Message, path = Path.GetFullPath(requestPath) };
            }

            return result;
        }

        object BuildPhasePayload(string phaseName)
        {
            FieldInfo? field = ResolvePhaseField(phaseName);
            if (field == null || field.FieldType != typeof(bool) || !IsPhaseFlag(field.Name))
            {
                return new { ok = false, error = "unknown_phase", phase = phaseName };
            }

            bool enabled = Core.savedSettings != null && (bool)field.GetValue(Core.savedSettings);
            int phaseTypes = CountPhaseTypes(field.Name);
            return new
            {
                ok = true,
                phase = field.Name,
                status = enabled ? "enabled" : "disabled",
                enabled,
                patchedTypes = phaseTypes
            };
        }

        object BuildTexturePackImportPayload(string packPath)
        {
            string trimmed = packPath?.Trim() ?? string.Empty;
            string? manifestStubPath = null;
            object payload;
            if (string.IsNullOrEmpty(trimmed))
            {
                var importResult = WorldSphereMod.Import.TexturePackImporter.TryImportAtLoad();
                manifestStubPath = importResult.ManifestStubPath;
                payload = WorldSphereMod.Import.TexturePackImporter.BuildBridgeImportPayload(importResult);
            }
            else
            {
                payload = WorldSphereMod.Import.TexturePackImporter.BuildBridgeImportPayload(trimmed);
            }

            try { WorldSphereMod.Textures.McPackLoader.Initialize(manifestStubPath); }
            catch { /* bridge import must not take down the listener */ }
            return payload;
        }

        object ForceDiagDumpNow()
        {
            WorldSphereMod.Voxel.MeshInstanceBatcher.ArmFallbackDiagOnce();
            return new { ok = true, status = "armed" };
        }

        static string FindSavePath(int slot)
        {
            string root = SaveManager.persistentDataPath;
            if (string.IsNullOrEmpty(root))
            {
                root = Application.persistentDataPath;
            }

            return Path.Combine(root, "saves", "save" + slot.ToString(CultureInfo.InvariantCulture));
        }

        static FieldInfo? ResolvePhaseField(string phaseName)
        {
            if (string.IsNullOrWhiteSpace(phaseName)) return null;

            FieldInfo? direct = typeof(SavedSettings).GetField(phaseName, SettingFlags);
            if (direct != null) return direct;

            string normalized = NormalizePhaseName(phaseName);
            foreach (FieldInfo field in typeof(SavedSettings).GetFields(SettingFlags))
            {
                if (string.Equals(NormalizePhaseName(field.Name), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return field;
                }
            }

            return null;
        }

        static bool IsPhaseFlag(string flagName)
        {
            foreach (FieldInfo field in typeof(SavedSettings).GetFields(SettingFlags))
            {
                if (!string.Equals(field.Name, flagName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return HasPhaseTypes(flagName);
            }

            return false;
        }

        static bool HasPhaseTypes(string flagName) => CountPhaseTypes(flagName) > 0;

        static int CountPhaseTypes(string flagName)
        {
            int count = 0;
            Type[] types = typeof(PhaseAttribute).Assembly.GetTypes();
            for (int i = 0; i < types.Length; i++)
            {
                PhaseAttribute? attr = types[i].GetCustomAttribute<PhaseAttribute>();
                if (attr == null) continue;
                if (!string.Equals(attr.SettingsFlagName, flagName, StringComparison.Ordinal)) continue;
                count++;
            }

            return count;
        }

        static string NormalizePhaseName(string value)
        {
            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        T InvokeOnMainThread<T>(Func<T> func)
        {
            if (_mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId) return func();
            using (var done = new ManualResetEventSlim(false))
            {
                T result = default(T);
                Exception? error = null;
                _mainThreadQueue.Enqueue(() =>
                {
                    try { result = func(); }
                    catch (Exception ex) { error = ex; }
                    finally { done.Set(); }
                });
                // 5s timeout — if Update() isn't draining (paused / disabled / mid-load),
                // returns default(T) + logs warning instead of hanging the listener thread
                // indefinitely, which would back up the entire HTTP accept loop.
                if (!done.Wait(5000))
                {
                    UnityEngine.Debug.LogWarning("[WSM3D][Bridge] main-thread dispatch timed out (5s) — returning default(T).");
                    return default(T);
                }
                if (error != null) throw error;
                return result;
            }
        }

        static long SafeLong(Func<long> read)
        {
            try { return read(); } catch { return 0L; }
        }

        static float SafeHitRate(Func<long> hits, Func<long> misses)
        {
            try
            {
                long h = hits();
                long m = misses();
                long total = h + m;
                return total > 0 ? (float)h / total : 0f;
            }
            catch { return 0f; }
        }

        void WriteJson(HttpListenerResponse response, object payload, HttpStatusCode statusCode = HttpStatusCode.OK) => WriteRawJson(response, JsonConvert.SerializeObject(payload, Formatting.None), statusCode);

        void WriteRawJson(HttpListenerResponse response, string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            response.StatusCode = (int)statusCode;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = bytes.Length;
            using (Stream output = response.OutputStream) output.Write(bytes, 0, bytes.Length);
        }
    }
}
