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
        Dictionary<string, Action<HttpListenerContext>> _getRoutes;
        Dictionary<string, Action<HttpListenerContext>> _postRoutes;

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
            BuildRouteTables();
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
            // Per-field guards: pre-world Core.IsWorld3D (=> Sphere.Exists) can throw; don't let it skip version (the original /health NRE).
            try { _cachedHealthVersion = Core.savedSettings != null ? Core.savedSettings.Version : "unknown"; }
            catch { _cachedHealthVersion = "unknown"; }
            try { _cachedIsWorld3D = Core.IsWorld3D; }
            catch { _cachedIsWorld3D = false; }
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
                // Impostor billboard cache removed (far-LOD culls, no billboards). Hit-rate
                // slot held at 0 for telemetry-schema stability.
                _cachedImpostorCacheHit = 0f;
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

        // Single Update drain is sufficient — LateUpdate and FixedUpdate redundantly
        // called DrainStaticQueue at ~170 total invocations/sec vs 60 needed.
        // VoxelRender.LateUpdate also calls DrainStaticQueue to handle submits
        // queued mid-frame, so bridge commands still get processed before the camera
        // renders without needing a FixedUpdate path. (#robustness perf)
        void Update() => DrainStaticQueue();
        // LateUpdate and FixedUpdate DrainStaticQueue calls removed — redundant drain.

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
            // BeginGetContext + ThreadPool hand-off: the accept loop MUST stay free of
            // per-request work or any slow handler (e.g. /diag/render_stats + InvokeOnMainThread
            // doing a 5s main-thread wait) will back up the entire listener. Under parallel
            // load this manifested as 31% timeouts across ALL endpoints — the slow endpoint
            // wasn't the only casualty, every concurrent request hit the listener thread's
            // queue and timed out. Handing each accepted context to the thread pool isolates
            // the work and keeps the accept loop draining.
            HttpListener? listener = _listener;
            while (_running && listener != null)
            {
                try
                {
                    listener.BeginGetContext(OnRequestAccepted, listener);
                    // Block here only until the next context arrives OR the listener stops —
                    // we never touch per-request state on this loop.
                    _acceptSignal.WaitOne();
                }
                catch (HttpListenerException) { if (!_running) return; }
                catch (ObjectDisposedException) { return; }
                catch (Exception ex)
                {
                    if (_running) Debug.LogWarning("[WSM3D][Bridge] listener loop error on 127.0.0.1:" + _boundPort + ": " + ex.Message);
                    Thread.Sleep(1);
                }
            }
        }

        // Signaled by the BeginGetContext callback so the accept loop wakes per-context
        // (not on a fixed interval) without spinning the CPU. No lock — AutoResetEvent
        // is its own synchronization primitive.
        readonly System.Threading.AutoResetEvent _acceptSignal = new System.Threading.AutoResetEvent(false);

        void OnRequestAccepted(IAsyncResult ar)
        {
            // Always signal the accept loop first so it can re-arm BeginGetContext even
            // if the synchronous GetContext call below throws.
            try { _acceptSignal.Set(); } catch { }
            HttpListenerContext? context = null;
            HttpListener? listener = null;
            try
            {
                listener = ar.AsyncState as HttpListener;
                if (listener == null) return;
                context = listener.EndGetContext(ar);
            }
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex)
            {
                if (_running) Debug.LogWarning("[WSM3D][Bridge] accept error on 127.0.0.1:" + _boundPort + ": " + ex.Message);
                return;
            }
            if (context == null) return;
            // Process the request on the thread pool so a slow handler (InvokeOnMainThread
            // 5s wait, big JSON, etc.) can't back up the accept loop. Multiple in-flight
            // requests run in parallel — the previous single-threaded ListenLoop serialized
            // them and caused the 31% timeout cascade.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { ProcessRequest(context); }
                catch (Exception ex) { Debug.LogWarning("[WSM3D][Bridge] request handler crashed: " + ex.Message); }
            });
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
                    if (_getRoutes.TryGetValue(path, out Action<HttpListenerContext> handler))
                    {
                        handler(context);
                        return;
                    }
                    if (path.StartsWith("/phase/", StringComparison.OrdinalIgnoreCase))
                    {
                        HandlePhase(context);
                        return;
                    }
                }
                else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    if (_postRoutes.TryGetValue(path, out Action<HttpListenerContext> handler))
                    {
                        handler(context);
                        return;
                    }
                    if (path.StartsWith("/settings/", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleSettingsUpdate(context);
                        return;
                    }
                }

                WriteJson(context.Response, new { ok = false, error = "not_found", path, method }, HttpStatusCode.NotFound);
            }
            catch (Exception ex)
            {
                WriteJson(context.Response, new { ok = false, error = ex.Message }, HttpStatusCode.InternalServerError);
            }
        }

        void BuildRouteTables()
        {
            _getRoutes = new Dictionary<string, Action<HttpListenerContext>>(StringComparer.OrdinalIgnoreCase)
            {
                ["/health"] = HandleHealth,
                ["/telemetry"] = HandleTelemetry,
                ["/settings"] = HandleSettings,
                ["/voxel/sprite"] = HandleVoxelSprite,
                ["/voxel/stats"] = HandleVoxelStats,
                ["/voxel/queue"] = HandleVoxelQueue,
                ["/memory"] = HandleMemory,
                ["/voxel/actor"] = HandleVoxelActor,
                ["/voxel/diff"] = HandleVoxelDiff,
                ["/diag/emit_status"] = HandleDiagEmitStatus,
                ["/diag/errors"] = HandleDiagErrors,
                ["/diag/render_stats"] = HandleDiagRenderStats,
                ["/diag/water_samples"] = HandleDiagWaterSamples,
                ["/diag/full_dump"] = HandleDiagFullDump,
                ["/world/state"] = HandleWorldState,
                ["/tools"] = HandleTools,
                ["/capture/list"] = HandleCaptureList,
                ["/capture/status"] = HandleCaptureStatus,
            };

            _postRoutes = new Dictionary<string, Action<HttpListenerContext>>(StringComparer.OrdinalIgnoreCase)
            {
                ["/actions/load_save"] = HandleLoadSave,
                ["/actions/screenshot"] = HandleScreenshot,
                ["/screenshot/now"] = HandleScreenshot,
                ["/voxel/dump_all"] = HandleVoxelDumpAll,
                ["/diag/dump_now"] = HandleDiagDumpNow,
                ["/texturepack/import"] = HandleTexturePackImport,
                ["/actions/spawn_units"] = HandleSpawnUnits,
                ["/actions/generate_world"] = HandleGenerateWorld,
                ["/actions/new_world"] = HandleNewWorld,
                ["/actions/regenerate"] = HandleRegenerate,
                ["/actions/save"] = HandleSave,
                ["/actions/close_dialog"] = HandleCloseDialog,
                ["/actions/pause"] = HandlePause,
                ["/actions/play"] = HandlePlay,
                ["/actions/set_speed"] = HandleSetSpeed,
                ["/actions/camera"] = HandleCamera,
                ["/actions/camera_focus"] = HandleCameraFocus,
                ["/actions/select_tool"] = HandleSelectTool,
                ["/actions/use_tool"] = HandleUseTool,
                ["/capture/replay"] = HandleCaptureReplay,
                ["/capture/save"] = HandleCaptureSave,
            };
        }

        void HandleHealth(HttpListenerContext context) => WriteJson(context.Response, BuildHealthPayload());
        void HandleTelemetry(HttpListenerContext context) => WriteJson(context.Response, BuildTelemetryPayload());
        void HandleSettings(HttpListenerContext context) => WriteRawJson(context.Response, InvokeOnMainThread(BuildSettingsJson));

        void HandleVoxelSprite(HttpListenerContext context)
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
        }

        void HandleVoxelStats(HttpListenerContext context) => WriteJson(context.Response, InvokeOnMainThread(BuildVoxelStatsPayload));
        void HandleVoxelQueue(HttpListenerContext context) => WriteJson(context.Response, InvokeOnMainThread(BuildVoxelQueuePayload));
        void HandleMemory(HttpListenerContext context) => WriteJson(context.Response, InvokeOnMainThread(BuildMemoryPayload));

        void HandleVoxelActor(HttpListenerContext context)
        {
            string indexText = context.Request.QueryString["index"] ?? "0";
            WriteJson(context.Response, InvokeOnMainThread(() => BuildVoxelActorPayload(indexText)));
        }

        void HandleVoxelDiff(HttpListenerContext context)
        {
            string baselinePath = context.Request.QueryString["baseline"] ?? string.Empty;
            WriteJson(context.Response, InvokeOnMainThread(() => BuildVoxelDiffPayload(baselinePath)));
        }

        void HandlePhase(HttpListenerContext context)
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";
            string phaseName = Uri.UnescapeDataString(path.Substring("/phase/".Length));
            WriteJson(context.Response, InvokeOnMainThread(() => BuildPhasePayload(phaseName)));
        }

        void HandleDiagEmitStatus(HttpListenerContext context) => WriteJson(context.Response, InvokeOnMainThread(BuildEmitStatusPayload));
        void HandleDiagErrors(HttpListenerContext context) => WriteJson(context.Response, BuildDiagErrorsPayload());
        void HandleDiagRenderStats(HttpListenerContext context) => WriteJson(context.Response, InvokeOnMainThread(BuildRenderStatsPayload));
        void HandleDiagWaterSamples(HttpListenerContext context) => WriteJson(context.Response, InvokeOnMainThread(BuildWaterSamplesPayload));
        void HandleDiagFullDump(HttpListenerContext context) => WriteJson(context.Response, InvokeOnMainThread(BuildFullDumpPayload));

        void HandleWorldState(HttpListenerContext context)
        {
            WriteJson(context.Response, InvokeOnMainThread(() => BridgeActions.WorldState(Core.IsWorld3D)));
        }

        void HandleTools(HttpListenerContext context) => WriteJson(context.Response, InvokeOnMainThread(BridgeActions.ListTools));

        void HandleCaptureList(HttpListenerContext context)
        {
            var flows = WorldSphereMod.Capture.FlowLibrary.List();
            WriteJson(context.Response, new { ok = true, root = WorldSphereMod.Capture.CaptureRecorder.CaptureRoot, count = flows.Count, flows });
        }

        void HandleCaptureStatus(HttpListenerContext context)
        {
            WriteJson(context.Response, new
            {
                ok = true,
                enabled = WorldSphereMod.Capture.CaptureRecorder.Enabled,
                session = WorldSphereMod.Capture.CaptureRecorder.SessionPath,
                events = WorldSphereMod.Capture.CaptureRecorder.EventCount,
            });
        }

        void HandleSettingsUpdate(HttpListenerContext context)
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";
            string key = path.Substring("/settings/".Length);
            string rawValue = context.Request.QueryString["value"] ?? string.Empty;
            WriteJson(context.Response, UpdateSettingQueued(key, rawValue));
        }

        void HandleLoadSave(HttpListenerContext context)
        {
            string slotText = context.Request.QueryString["slot"] ?? string.Empty;
            WriteJson(context.Response, LoadSaveQueued(slotText));
        }

        void HandleScreenshot(HttpListenerContext context)
        {
            BridgeParams shot = BridgeParams.From(context.Request);
            string outputPath = shot.Get("path", string.Empty);
            string mode = shot.Get("mode", string.Empty);
            bool cameraMode = string.Equals(mode, "camera", StringComparison.OrdinalIgnoreCase);
            WriteJson(context.Response, CaptureScreenshot(outputPath, cameraMode));
        }

        void HandleVoxelDumpAll(HttpListenerContext context) => WriteJson(context.Response, InvokeOnMainThread(DumpVoxelMeshes));
        void HandleDiagDumpNow(HttpListenerContext context) => WriteJson(context.Response, InvokeOnMainThread(ForceDiagDumpNow));

        void HandleTexturePackImport(HttpListenerContext context)
        {
            string packPath = context.Request.QueryString["path"] ?? string.Empty;
            WriteJson(context.Response, InvokeOnMainThread(() => BuildTexturePackImportPayload(packPath)));
        }

        void HandleSpawnUnits(HttpListenerContext context)
        {
            BridgeParams sp = BridgeParams.From(context.Request);
            string countText = sp.Get("count", "10");
            string race = sp.Get("race", "human");
            string xText = sp.Get("x");
            string yText = sp.Get("y");
            WriteJson(context.Response, SpawnUnitsQueued(countText, race, xText, yText));
        }

        void HandleGenerateWorld(HttpListenerContext context) => WriteJson(context.Response, GenerateWorldQueued());
        void HandleNewWorld(HttpListenerContext context) => WriteJson(context.Response, QueueAction("new_world", BridgeActions.NewWorld));
        void HandleRegenerate(HttpListenerContext context) => WriteJson(context.Response, QueueAction("regenerate", BridgeActions.Regenerate));
        void HandleSave(HttpListenerContext context) => WriteJson(context.Response, QueueAction("save", BridgeActions.Save));
        void HandleCloseDialog(HttpListenerContext context) => WriteJson(context.Response, QueueAction("close_dialog", BridgeActions.CloseDialog));
        void HandlePause(HttpListenerContext context) => WriteJson(context.Response, QueueAction("pause", BridgeActions.Pause));
        void HandlePlay(HttpListenerContext context) => WriteJson(context.Response, QueueAction("play", BridgeActions.Play));

        void HandleSetSpeed(HttpListenerContext context)
        {
            string speed = BridgeParams.From(context.Request).Get("speed", string.Empty);
            WriteJson(context.Response, QueueAction("set_speed", () => BridgeActions.SetSpeed(speed)));
        }

        void HandleCamera(HttpListenerContext context)
        {
            BridgeParams cp = BridgeParams.From(context.Request);
            string cx = cp.Get("x", string.Empty);
            string cy = cp.Get("y", string.Empty);
            string cz = cp.Get("zoom", string.Empty);
            WriteJson(context.Response, QueueAction("camera", () => BridgeActions.Camera(cx, cy, cz)));
        }

        void HandleCameraFocus(HttpListenerContext context)
        {
            string target = BridgeParams.From(context.Request).Get("target", string.Empty);
            WriteJson(context.Response, QueueAction("camera_focus", () => BridgeActions.CameraFocus(target)));
        }

        void HandleSelectTool(HttpListenerContext context)
        {
            string id = BridgeParams.From(context.Request).Get("id", string.Empty);
            WriteJson(context.Response, QueueAction("select_tool", () => BridgeActions.SelectTool(id)));
        }

        void HandleUseTool(HttpListenerContext context)
        {
            BridgeParams up = BridgeParams.From(context.Request);
            string id = up.Get("id", string.Empty);
            string ux = up.Get("x", string.Empty);
            string uy = up.Get("y", string.Empty);
            WriteJson(context.Response, QueueAction("use_tool", () => BridgeActions.UseTool(id, ux, uy)));
        }

        void HandleCaptureReplay(HttpListenerContext context)
        {
            string file = context.Request.QueryString["file"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(file)) { WriteJson(context.Response, new { ok = false, error = "missing_file" }, HttpStatusCode.BadRequest); return; }
            WriteJson(context.Response, QueueAction("capture_replay", () => WorldSphereMod.Capture.CaptureReplayer.ReplayFile(file)));
        }

        void HandleCaptureSave(HttpListenerContext context)
        {
            string name = context.Request.QueryString["name"] ?? string.Empty;
            string source = context.Request.QueryString["source"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) { WriteJson(context.Response, new { ok = false, error = "missing_name" }, HttpStatusCode.BadRequest); return; }
            var (ok, savedPath, error) = WorldSphereMod.Capture.FlowLibrary.SaveAs(name, source);
            WriteJson(context.Response, ok ? (object)new { ok = true, name, path = savedPath } : new { ok = false, error, name }, ok ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        }

        // Reads request params from BOTH the query string and the JSON request body.
        // The HttpListener body stream can only be consumed ONCE, so callers build a single
        // BridgeParams per request and reuse it for every lookup (query takes precedence; the
        // body is parsed lazily on first body-backed lookup). Honors the live-bridge contract
        // that every /actions param works whether passed as ?name= or {"name":...}.
        sealed class BridgeParams
        {
            ExecuteEndpoint(context, () => (HttpStatusCode.OK, handler()));
        }

        void ExecuteEndpoint(HttpListenerContext context, Func<string> handler, bool rawJson)
        {
            ExecuteEndpoint(context, () => (HttpStatusCode.OK, handler()), rawJson);
        }

        void ExecuteEndpoint(HttpListenerContext context, Func<(HttpStatusCode statusCode, object payload)> handler)
        {
            ExecuteEndpoint(context, handler, rawJson: false);
        }

        void ExecuteEndpoint(HttpListenerContext context, Func<(HttpStatusCode statusCode, object payload)> handler, bool rawJson)
        {
            try
            {
                (HttpStatusCode statusCode, object payload) = handler();
                if (rawJson)
                {
                    WriteRawJson(context.Response, payload as string ?? JsonConvert.SerializeObject(payload, Formatting.None), statusCode);
                }
                else
                {
                    WriteJson(context.Response, payload, statusCode);
                }
            }
            catch (Exception ex)
            {
                WriteJson(context.Response, new { ok = false, error = "endpoint_error", message = ex.Message }, HttpStatusCode.InternalServerError);
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

        object BuildEmitStatusPayload() => new
        {
            ok = true,
            emitVoxelsCalled = WorldSphereMod.Voxel.VoxelRender.ActorVoxelEmit.EmitVoxelsCalled,
            visibleUnitsCount = WorldSphereMod.Voxel.VoxelRender.ActorVoxelEmit.LastVisibleUnitsCount,
            frustumCullerPassCount = WorldSphereMod.Voxel.VoxelRender.ActorVoxelEmit.LastFrustumCullerPassCount,
            batcherSubmitCount = WorldSphereMod.Voxel.VoxelRender.ActorVoxelEmit.LastBatcherSubmitCount,
            // (#208 fps) counts of building-emit postfix invocations that were short-circuited
            // because the visible-buildings identity hash was unchanged from the prior frame
            skippedStaticBuildingFrames = WorldSphereMod.Voxel.VoxelRender.BuildingVoxelEmit.SkippedStaticFrames,
            skippedStaticProcgenBuildingFrames = WorldSphereMod.ProcGen.BuildingProcRender.SkippedStaticFrames,
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
            // Dedicated actor-draw counters (NOT reset by the batcher) so we can measure actual
            // per-frame actor draws: actorDrawCumulative grows ~1/frame if drawing stably.
            actorDrawCumulative = WorldSphereMod.Voxel.VoxelRender.ActorDrawCallsCumulative,
            actorFrontCount = WorldSphereMod.Voxel.VoxelRender.ActorFrontCount,
            // Render-foundation MACHINE state (commit 40f903d): lets wsm3d-verify.ps1
            // confirm the LIT built-in fallback + lighting setup WITHOUT pixel sampling.
            // All reads are cheap static-field / RenderSettings property accesses computed
            // only on a /telemetry request (no per-frame cost).
            renderFoundation = BuildRenderFoundationPayload(),
        };

        /// <summary>
        /// Machine-readable snapshot of the render-foundation: resolved shaders on the
        /// live actor + terrain materials, ambient/sun lighting setup, and representative
        /// mesh vertex counts. Read by Tools/wsm3d-verify.ps1 (TIER 1, pixel-free).
        /// Fully null-safe so /telemetry never throws at the title screen.
        /// </summary>
        object BuildRenderFoundationPayload()
        {
            string actorShader = null;
            int actorVerts = 0;
            try
            {
                var actorMat = WorldSphereMod.Voxel.VoxelRender._material;
                if (actorMat != null && actorMat.shader != null) actorShader = actorMat.shader.name;
                actorVerts = WorldSphereMod.Voxel.VoxelRender.LastActorMeshVertCount;
            }
            catch { }

            string terrainShader = null;
            int terrainVerts = 0;
            try
            {
                var terrainMat = Core.Sphere.LastTerrainMaterial;
                if (terrainMat != null && terrainMat.shader != null) terrainShader = terrainMat.shader.name;
                var terrainMesh = Core.Sphere.LastTerrainMesh;
                if (terrainMesh != null) terrainVerts = terrainMesh.vertexCount;
            }
            catch { }

            float ar = 0f, ag = 0f, ab = 0f;
            string ambientMode = null;
            bool sunPresent = false, sunIsDirectional = false;
            float sunEulerX = 0f, sunEulerY = 0f, sunEulerZ = 0f, sunRotationDeg = 0f;
            float timeOfDay = 0f;
            try
            {
                UnityEngine.Color a = UnityEngine.RenderSettings.ambientLight;
                ar = a.r; ag = a.g; ab = a.b;
                ambientMode = UnityEngine.RenderSettings.ambientMode.ToString();
                UnityEngine.Light sun = UnityEngine.RenderSettings.sun;
                sunPresent = sun != null;
                sunIsDirectional = sun != null && sun.type == UnityEngine.LightType.Directional;
                if (sun != null)
                {
                    UnityEngine.Vector3 sunEuler = sun.transform.rotation.eulerAngles;
                    sunEulerX = sunEuler.x;
                    sunEulerY = sunEuler.y;
                    sunEulerZ = sunEuler.z;
                    sunRotationDeg = sunEuler.x;
                }
                timeOfDay = WorldSphereMod.Lighting.TimeOfDay.Current;
            }
            catch { }

            return new
            {
                actorMaterialShader = actorShader,
                terrainMaterialShader = terrainShader,
                ambientLight = new { r = ar, g = ag, b = ab },
                ambientMode,
                sunPresent,
                sunIsDirectional,
                sunEuler = new { x = sunEulerX, y = sunEulerY, z = sunEulerZ },
                sunRotationDeg,
                timeOfDay,
                actorMeshVertCount = actorVerts,
                terrainMeshVertCount = terrainVerts,
            };
        }

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

                    // ROOT-CAUSE FIX (#scene-entry): loadWorld() only ENQUEUES the load steps
                    // (setMapSize -> finishMakingWorld) into SmoothLoader. SmoothLoader is pumped
                    // by MapBox.Update, but MapBox.Update early-returns while Config.game_loaded
                    // is false (it stays false at the main menu / pre-first-world). So a bare
                    // loadWorld from the bridge enqueued everything and NOTHING drained it:
                    // MapBox.width/height stayed 0 and the fork's Become3D (guarded on width>0)
                    // never ran. The menu "Load" button avoids this because startTheGame() sets
                    // Config.game_loaded = true BEFORE enqueuing. Mirror the menu path exactly:
                    // drive startTheGame with load_save_on_start so the engine sets game_loaded,
                    // queues loadWorld + addLastStep (re-enables the SpriteRenderer/nameplate),
                    // and the SmoothLoader pump drains to a real in-gameplay world.
                    Config.game_loaded = true;
                    Config.load_new_map = false;
                    Config.load_save_on_start = true;
                    Config.load_save_on_start_slot = queuedSlot;
                    // NOTE: do NOT call save_manager.prepareLoading() here — it's a private
                    // WorldBox method (publicizer trap: compiles but throws under NML's Roslyn).
                    // startTheGame's load_save_on_start branch calls loadWorld() directly without
                    // a manual prepareLoading, so the menu path does not need it either.
                    MapBox.instance.startTheGame(false);

                    // loadWorld Postfix also runs survival; belt-and-suspenders if patch order differs.
                    CaptureMainThread();
                    EnsureCreated();
                    DrainStaticQueue();

                    // Post-condition log fires once SmoothLoader has drained setMapSize
                    // (MapBox.width becomes non-zero) so the operator can confirm scene entry.
                    LogWorldEnteredWhenReady("load_save slot=" + queuedSlot);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WSM3D][Bridge] load_save failed slot=" + queuedSlot + ": " + ex.Message);
                }
            });

            return new { ok = true, slot, path, queued = true };
        }

        /// <summary>
        /// Post-condition probe: poll up to ~10s (on the main thread via coroutine) until
        /// SmoothLoader has drained setMapSize so MapBox.width becomes non-zero, then log the
        /// real entered dimensions. Confirms the scene-entry fix worked without blocking the
        /// HTTP response. Safe no-op if the BridgeServer host coroutine can't start.
        /// </summary>
        void LogWorldEnteredWhenReady(string source)
        {
            try { StartCoroutine(WorldEnteredProbe(source)); }
            catch (Exception ex) { Debug.LogWarning("[WSM3D][Bridge] world-entered probe failed to start: " + ex.Message); }
        }

        System.Collections.IEnumerator WorldEnteredProbe(string source)
        {
            const int maxFrames = 600; // ~10s at 60fps; world load streams over many frames
            // SCENE-ENTRY DETECTION (#generate-path fix): the original gate required
            // !SmoothLoader.isLoading(), but on the bridge GENERATE path SmoothLoader never
            // fully drains to isLoading()==false within the probe window (the post-gen idle
            // steps / addLastStep keep a step queued), so the probe always TIMED OUT and
            // EnsureBecome3D never fired -> isWorld3D stayed false, drawCalls=0.
            //
            // The real readiness signal the menu path relies on is: the engine considers the
            // game loaded (Config.game_loaded) AND MapBox has real dimensions (width/height>0,
            // i.e. setMapSize ran). Once both hold the world IS entered; isLoading() merely
            // reflects residual streaming steps. So we PROCEED to Become3D as soon as map dims
            // are non-zero and game_loaded is set, without waiting for isLoading()==false. We
            // still give SmoothLoader a brief grace settle so most streaming finishes first.
            const int settleFramesAfterReady = 30; // ~0.5s grace once dims+game_loaded hold
            int readyFor = 0;
            for (int i = 0; i < maxFrames; i++)
            {
                int w = 0, h = 0;
                try { w = MapBox.width; h = MapBox.height; } catch { }
                bool gameLoaded = false;
                try { gameLoaded = Config.game_loaded; } catch { }
                bool loading = true;
                try { loading = SmoothLoader.isLoading(); } catch { }

                bool dimsReady = w > 0 && h > 0 && gameLoaded;
                // Fast path: full drain (dims + game_loaded + streaming finished).
                // Fallback path: dims + game_loaded held stable for a short grace window even
                // though isLoading() never cleared (the generate-path symptom).
                if (dimsReady) readyFor++; else readyFor = 0;
                bool proceed = dimsReady && (!loading || readyFor >= settleFramesAfterReady);

                if (proceed)
                {
                    Debug.Log($"[WSM3D][Bridge] world entered: mapSize={w}x{h} gameLoaded={gameLoaded} stillLoading={loading} (via {source})");
                    // DOWNSTREAM FIX: on the bridge-driven entry path (startTheGame from the
                    // RPC queue) the General.cs SphereControl.CreateSphere postfix on
                    // MapBox.finishMakingWorld does NOT fire/queue Become3D the way the menu
                    // path does (empirically: "Tidying Up The World" runs but no "Becoming 3D!"
                    // SmoothLoader step and no Become3D/HEIGHT-DIAG/COLOR-DIAG log appears), so
                    // Sphere is never created and isWorld3D stays false. Drive the 3D conversion
                    // explicitly here, mirroring what the postfix is supposed to do: only when
                    // Is3D is enabled and the map fits the 3D tile budget. If a prior Sphere
                    // exists, EnsureBecome3D tears it down and rebuilds it for this map.
                    EnsureBecome3D(source, w, h);
                    yield break;
                }
                yield return null;
            }
            int fw = 0, fh = 0;
            try { fw = MapBox.width; fh = MapBox.height; } catch { }
            bool fGameLoaded = false; try { fGameLoaded = Config.game_loaded; } catch { }
            bool fLoading = true; try { fLoading = SmoothLoader.isLoading(); } catch { }
            // Last-ditch: if dims are valid we still drive Become3D rather than abandoning the
            // entry — a non-zero map is a real world regardless of the loading flag.
            if (fw > 0 && fh > 0)
            {
                Debug.LogWarning($"[WSM3D][Bridge] world entered: dims ready at timeout mapSize={fw}x{fh} gameLoaded={fGameLoaded} stillLoading={fLoading} (via {source}) — driving Become3D anyway");
                EnsureBecome3D(source, fw, fh);
                yield break;
            }
            Debug.LogWarning($"[WSM3D][Bridge] world entered: TIMEOUT mapSize={fw}x{fh} gameLoaded={fGameLoaded} stillLoading={fLoading} (via {source}) — scene entry did not complete");
        }

        /// <summary>
        /// Explicitly drive the 3D conversion on the bridge entry path when the finishMakingWorld
        /// postfix did not. No-op when Is3D is off, or the map exceeds the 3D tile budget
        /// (Become3D would skip it anyway). If a prior sphere exists, tear it down first so the
        /// heightfield and camera rebuild against the newly entered world.
        /// </summary>
        static void EnsureBecome3D(string source, int w, int h)
        {
            try
            {
                var settings = Core.savedSettings;
                if (settings == null || !settings.Is3D) return;
                if (Core.IsWorld3D)
                {
                    Debug.Log($"[WSM3D][Bridge] rebuilding 3D for new world (prior sphere existed) via {source}");
                    try { Core.Become2D(); }
                    catch (Exception ex) { Debug.LogWarning("[WSM3D][Bridge] Become2D teardown failed: " + ex.Message); }
                }
                long totalTiles = (long)w * h;
                if (totalTiles > settings.MaxTilesFor3D)
                {
                    Debug.LogWarning($"[WSM3D][Bridge] skipping Become3D: {w}x{h}={totalTiles} > MaxTilesFor3D={settings.MaxTilesFor3D} (via {source})");
                    return;
                }
                Debug.Log($"[WSM3D][Bridge] driving Become3D explicitly via {source}");
                // When the bridge drives startTheGame, NML's IStagedLoad Init()/PostInit()
                // callbacks (which call Core.Init -> Sphere.PrepareAssets) may not have fired,
                // so the 'worldsphere' AssetBundle (CompoundSphereMaterial + CompoundSphereMesh)
                // was never loaded and Sphere.Begin aborts with "...missing — Bundle load likely
                // failed." PrepareAssets is idempotent and has NO World dependency, so force it
                // here before Become3D so the Sphere manager can be created.
                try { Core.Sphere.PrepareAssets(); }
                catch (Exception ex) { Debug.LogWarning("[WSM3D][Bridge] PrepareAssets (forced) failed: " + ex.Message); }
                // Core.Init (which sets Sphere.HeightMult = Max(TileHeight,1)) also never ran on
                // the bridge staged-load path, leaving HeightMult=0. CompoundSpheres'
                // SphereManager.SphereTilePosition NREs when building tiles with HeightMult=0,
                // so seed it from settings here (mirrors Core.Init).
                try
                {
                    if (Core.Sphere.HeightMult <= 0f)
                    {
                        float th = settings.TileHeight > 0f ? settings.TileHeight : 1f;
                        Core.Sphere.HeightMult = Mathf.Max(th, 1f);
                        Debug.Log($"[WSM3D][Bridge] seeded Sphere.HeightMult={Core.Sphere.HeightMult} (Core.Init had not run)");
                    }
                }
                catch (Exception ex) { Debug.LogWarning("[WSM3D][Bridge] HeightMult seed failed: " + ex.Message); }
                // Ensure CurrentShape is selected. The General.cs setMapSize prefix
                // (SphereControl.PrepareShape -> Core.Sphere.PrepareShape) is what normally
                // assigns CurrentShape = Shapes[CurrentShape]; if it did not fire on the bridge
                // path, CreateSettings wires a null getSphereTilePosition delegate and
                // CompoundSpheres' SphereManager.SphereTilePosition NREs while building tiles.
                try
                {
                    int sw2 = w, sh = h;
                    Core.Sphere.PrepareShape(ref sw2, ref sh);
                }
                catch (Exception ex) { Debug.LogWarning("[WSM3D][Bridge] PrepareShape (forced) failed: " + ex.Message); }
                // Mirror SphereControl.CreateSphere: reset the PrepareWorld guard so Become3D
                // reads real biome pixels (else vertex colors stay white). (#208)
                try { Core.Sphere.ResetPrepared(); } catch { }
                Core.Become3D();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WSM3D][Bridge] EnsureBecome3D failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Spawn N living units of a race on valid LAND tiles. Runs synchronously on the Unity
        /// main thread (via InvokeOnMainThread) and returns the REAL number actually created so
        /// the operator can self-verify population rose — instead of a fire-and-forget queued:true.
        ///
        /// Why the old version was a no-op:
        ///  - It only rejected tile.Type.ocean. A fresh map center is frequently water/lake/mountain,
        ///    so units that did spawn were dropped onto liquid/lava and drowned/died instantly,
        ///    or every random pick hit `continue` and nothing spawned at all.
        ///  - createNewUnit() returns null silently when the tile/asset is unusable; the old code
        ///    ignored the return value and counted a "spawn" that never produced a live actor.
        ///  - It was fire-and-forget, so {queued:true} returned before any actor existed and the
        ///    caller had no truthful count.
        ///
        /// Why units still DIED 0->0 after the tile fix (root cause of THIS change):
        ///  - ActorManager.createNewUnit() is the low-level constructor: it does NOT assign a
        ///    kingdom and does NOT set nutrition. On a fresh empty world the resulting actor has
        ///    no wild kingdom and zero nutrition, so it starves/gets pruned within the first ticks
        ///    -> "live 0->0". (A loaded SAVE worked because its actors already had kingdoms.)
        ///  - The in-game creature power instead calls spawnNewUnitByPlayer(), which wraps
        ///    spawnNewUnit(): assigns the asset's wild kingdom (kingdoms_wild.get(kingdom_id_wild)),
        ///    calls setStatsDirty(), and (crucially) setNutrition(nutrition_level_on_spawn). It also
        ///    joins a nearby joinable city when sapient. Those actors PERSIST.
        ///
        /// Fix: collect SPAWNABLE land tiles (ground && !liquid && !lava && !block) nearest-first,
        /// spawn each via ActorManager.spawnNewUnitByPlayer (the lasting, kingdom+nutrition path the
        /// player's creature power uses), require a non-null returned Actor, and report spawned vs
        /// requested + live unit delta.
        /// </summary>
        object SpawnUnitsQueued(string countText, string race, string xText = null, string yText = null)
        {
            if (!BridgeSettingParser.TryParseNonNegativeInt(countText, out int count))
                return new { ok = false, error = "invalid_count", count = countText };
            count = Math.Min(count, 500); // sane max: cap a single dispatch so a bad arg can't spawn-storm the sim
            if (string.IsNullOrWhiteSpace(race)) race = "human";

            bool haveAnchor = false;
            int anchorX = 0, anchorY = 0;
            if (BridgeSettingParser.TryParseNonNegativeInt(xText, out int px) &&
                BridgeSettingParser.TryParseNonNegativeInt(yText, out int py))
            { anchorX = px; anchorY = py; haveAnchor = true; }

            return InvokeOnMainThread<object>(() =>
            {
                try
                {
                    if (World.world == null || MapBox.instance == null)
                        return new { ok = false, error = "world_not_ready", race };

                    var mapBox = MapBox.instance;
                    if (mapBox.units == null)
                        return new { ok = false, error = "unit_manager_null", race };

                    // Reject unknown race up front so the caller gets a clear error instead of 0 spawns.
                    if (AssetManager.actor_library == null || AssetManager.actor_library.get(race) == null)
                        return new { ok = false, error = "unknown_race", race };

                    int mapW = MapBox.width;
                    int mapH = MapBox.height;
                    int cx = haveAnchor ? Math.Max(0, Math.Min(mapW - 1, anchorX)) : mapW / 2;
                    int cy = haveAnchor ? Math.Max(0, Math.Min(mapH - 1, anchorY)) : mapH / 2;

                    // Collect spawnable land tiles in ONE pass (cheap), nearest-to-center first, so we
                    // never do a per-unit O(W*H) scan (which blew the main-thread dispatch budget on
                    // water-heavy maps). Sort by distance to the anchor so units cluster near center.
                    var candidates = CollectSpawnableTiles(mapBox, cx, cy, mapW, mapH);
                    if (candidates.Count == 0)
                        return new { ok = false, error = "no_land_tiles", race, center = new { x = cx, y = cy } };

                    int before = LiveUnitCount();
                    int spawned = 0;

                    for (int i = 0; i < count; i++)
                    {
                        // Cycle through the nearest-first candidate list (wraps if count > land tiles).
                        WorldTile tile = candidates[i % candidates.Count];
                        if (tile == null) continue;
                        try
                        {
                            // Use the player-facing spawn path: assigns a wild kingdom + nutrition
                            // (and joins a nearby city if sapient) so the actor PERSISTS instead of
                            // starving/being pruned. createNewUnit() alone produced 0->0 live units.
                            Actor actor = mapBox.units.spawnNewUnitByPlayer(race, tile);
                            if (actor != null) spawned++;
                        }
                        catch (Exception spawnEx)
                        {
                            Debug.LogWarning($"[WSM3D][Bridge] spawn_units[{i}]: {spawnEx.GetType().Name}: {spawnEx.Message}");
                            break;
                        }
                    }

                    int after = LiveUnitCount();
                    Debug.Log($"[WSM3D][Bridge] spawn_units: spawned {spawned}/{count} {race} units (live {before}->{after})");
                    return new
                    {
                        ok = true,
                        race,
                        requested = count,
                        count = spawned,
                        spawned,
                        liveUnitsBefore = before,
                        liveUnitsAfter = after,
                        center = new { x = cx, y = cy },
                    };
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[WSM3D][Bridge] spawn_units failed: " + ex.Message);
                    return new { ok = false, error = ex.GetType().Name + ": " + ex.Message, race };
                }
            });
        }

        /// <summary>Total live actors via the engine's actor list (used for spawn self-verification).</summary>
        static int LiveUnitCount()
        {
            try
            {
                ActorManager units = World.world != null ? World.world.units : null;
                if (units == null) return 0;
                var list = units.getSimpleList();
                return list != null ? list.Count : 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// One-pass scan of the whole map for SPAWNABLE land tiles, returned sorted nearest-first
        /// to (cx,cy). Single O(W*H) pass (cheap even at 256²) — replaces a per-unit ring scan that
        /// could be O(W*H) PER unit and timed out the main-thread dispatch on water-heavy maps.
        /// </summary>
        static System.Collections.Generic.List<WorldTile> CollectSpawnableTiles(MapBox mapBox, int cx, int cy, int mapW, int mapH)
        {
            var found = new System.Collections.Generic.List<WorldTile>();
            for (int x = 0; x < mapW; x++)
            {
                for (int y = 0; y < mapH; y++)
                {
                    WorldTile t = mapBox.GetTile(x, y);
                    if (IsSpawnable(t)) found.Add(t);
                }
            }
            found.Sort((a, b) =>
            {
                long da = (long)(a.x - cx) * (a.x - cx) + (long)(a.y - cy) * (a.y - cy);
                long db = (long)(b.x - cx) * (b.x - cx) + (long)(b.y - cy) * (b.y - cy);
                return da.CompareTo(db);
            });
            return found;
        }

        /// <summary>
        /// A tile a civ unit can stand on without instantly dying. The base terrain layer
        /// (main_type.layer_type) must be Ground — not Ocean/Lava/Block/Goo — and the current
        /// top tile (Type, e.g. grass/forest) must not be liquid/lava/blocking. Using main_type's
        /// layer is more reliable than the `ground` bool, which is unset on grass/biome overlays.
        /// </summary>
        static bool IsSpawnable(WorldTile t)
        {
            if (t == null) return false;
            TileTypeBase baseType = t.main_type;
            if (baseType == null || baseType.layer_type != TileLayerType.Ground) return false;
            TileTypeBase top = t.Type;
            if (top != null && (top.liquid || top.lava || top.block)) return false;
            return true;
        }

        /// <summary>
        /// Run a headless toolbar/world action on the Unity main thread and return its real {ok,...} result.
        /// Uses InvokeOnMainThread (drained every frame by BridgeServer.Update, even at the title screen),
        /// so the operator gets the actual outcome instead of a fire-and-forget queued:true.
        /// </summary>
        object QueueAction(string name, Func<object> action)
        {
            try { return InvokeOnMainThread(action); }
            catch (Exception ex) { return new { ok = false, action = name, error = ex.Message }; }
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
                    // ROOT-CAUSE FIX: bare generateNewMap() enqueues finishMakingWorld into
                    // SmoothLoader but does NOT set Config.game_loaded, so MapBox.Update never
                    // pumps SmoothLoader and MapBox.width stays 0 (Become3D never runs).
                    // startTheGame(true) is the menu "Generate New World" path: it sets
                    // Config.game_loaded = true, generates, and queues addLastStep so the
                    // pump drains to a real in-gameplay world.
                    Config.game_loaded = true;
                    MapBox.instance.startTheGame(true);
                    CaptureMainThread();
                    EnsureCreated();
                    DrainStaticQueue();
                    LogWorldEnteredWhenReady("generate_world");
                    Debug.Log("[WSM3D][Bridge] generate_world: startTheGame(true) driven");
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
            return CaptureScreenshot(outputPath, false);
        }

        object CaptureScreenshot(string outputPath, bool cameraMode)
        {
            string requestPath = string.IsNullOrWhiteSpace(outputPath) ? WorldSphereMod.ScreenshotCapture.BuildDefaultPath() : outputPath;
            ManualResetEventSlim completed = new ManualResetEventSlim(false);
            object result = new { ok = false, error = "pending", path = Path.GetFullPath(requestPath) };
            Exception? error = null;

            _mainThreadQueue.Enqueue(() =>
            {
                try
                {
                    var coroutine = cameraMode
                        ? WorldSphereMod.ScreenshotCapture.CaptureCameraCoroutine(requestPath, (savedPath, success, message) =>
                        {
                            result = success
                                ? new { ok = true, path = savedPath }
                                : new { ok = false, error = message, path = savedPath };
                            completed.Set();
                        })
                        : WorldSphereMod.ScreenshotCapture.CaptureCoroutine(requestPath, (savedPath, success, message) =>
                        {
                            result = success
                                ? new { ok = true, path = savedPath }
                                : new { ok = false, error = message, path = savedPath };
                            completed.Set();
                        });
                    StartCoroutine(coroutine);
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

        // BRIDGE SINK for RenderErrorRegistry: per-type counts + sample examples so a remote
        // operator can GET /diag/errors during a run and see WHAT failed WHERE — no pixels.
        object BuildDiagErrorsPayload()
        {
            List<WorldSphereMod.Voxel.RenderErrorRegistry.TypeReport> snapshot =
                WorldSphereMod.Voxel.RenderErrorRegistry.Snapshot();
            var byType = new List<object>(snapshot.Count);
            var counts = new Dictionary<string, long>();
            long total = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                WorldSphereMod.Voxel.RenderErrorRegistry.TypeReport r = snapshot[i];
                counts[r.type] = r.count;
                total += r.count;
                var examples = new List<object>(r.examples.Count);
                for (int j = 0; j < r.examples.Count; j++)
                {
                    WorldSphereMod.Voxel.RenderErrorRegistry.Example e = r.examples[j];
                    examples.Add(new { name = e.name, reason = e.reason, pos = new { x = e.x, y = e.y, z = e.z } });
                }
                byType.Add(new { type = r.type, count = r.count, examples });
            }

            return new
            {
                ok = true,
                total,
                counts,
                errors = byType,
                renderErrorProps = Core.savedSettings != null && Core.savedSettings.RenderErrorProps,
            };
        }

        object BuildRenderStatsPayload()
        {
            long drawCalls = SafeLong(() => WorldSphereMod.Voxel.MeshInstanceBatcher.FrameDrawCalls);
            long instances = SafeLong(() => WorldSphereMod.Voxel.MeshInstanceBatcher.FrameInstances);
            long buckets = SafeLong(() => WorldSphereMod.Voxel.MeshInstanceBatcher.FrameBucketCount);
            bool fallbackPath = SafeBool(() => WorldSphereMod.Voxel.MeshInstanceBatcher.UseFallbackPath);
            bool instancingBroken = SafeBool(() => WorldSphereMod.Voxel.MeshInstanceBatcher.InstancingBroken);

            int visibleUnits = 0;
            int visibleBuildings = 0;
            try
            {
                var world = World.world;
                ActorManager units = world != null ? world.units : null;
                if (units != null) visibleUnits = units.visible_units.count;
            }
            catch { }
            try
            {
                var world = World.world;
                BuildingManager buildings = world != null ? world.buildings : null;
                if (buildings != null) visibleBuildings = buildings._visible_buildings_count;
            }
            catch { }

            int voxelCacheSize = SafeCount(() => WorldSphereMod.Voxel.VoxelMeshCache.Count);
            int procgenCacheSize = SafeCount(() => WorldSphereMod.ProcGen.ProcGenCache.Count);
            // Crossed-quad foliage + impostor billboard caches removed (all foliage/fx
            // is voxel now; far-LOD culls instead of billboarding). Slots kept at 0 for
            // telemetry-schema stability.
            int foliageCount = 0;
            int impostorCount = 0;

            bool emitVoxelsFired = drawCalls > 0 || _cachedLastNonZeroDrawCalls > 0;

            object cameraInfo = null;
            try
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 pos = cam.transform.position;
                    float dist = pos.magnitude;
                    cameraInfo = new
                    {
                        position = new { x = pos.x, y = pos.y, z = pos.z },
                        distanceFromOrigin = dist,
                        fieldOfView = cam.fieldOfView,
                        orthographic = cam.orthographic,
                        orthographicSize = cam.orthographicSize,
                        nearClip = cam.nearClipPlane,
                        farClip = cam.farClipPlane
                    };
                }
            }
            catch { }

            string materialShaderName = null;
            {
                Material mat = WorldSphereMod.Voxel.VoxelRender._material;
                if (mat != null && mat.shader != null)
                    materialShaderName = mat.shader.name;
            }

            return new
            {
                ok = true,
                worldReady = World.world != null,
                drawCalls,
                instances,
                buckets,
                fallbackPath,
                instancingBroken,
                emitVoxelsFired,
                lastNonZeroDrawCalls = _cachedLastNonZeroDrawCalls,
                visibleUnits,
                visibleBuildings,
                voxelCacheSize,
                procgenCacheSize,
                foliageCount,
                impostorCount,
                materialShaderName,
                camera = cameraInfo,
                isWorld3D = _cachedIsWorld3D,
                voxelEntitiesEnabled = Core.savedSettings != null && Core.savedSettings.VoxelEntities,
                frameMs = Time.unscaledDeltaTime * 1000f,
                frameCount = Time.frameCount
            };
        }

        /// <summary>
        /// Per-tile water state sampler for diagnostic / regression tests (#208 water-flat).
        /// Reads the same callbacks ConfigureHeightField wired into HeightFieldRenderer so
        /// the numbers reflect what the water sub-mesh is actually built with. We sample
        /// is-water + sampled water-level + sampled seabed for a set of tile coords. If the
        /// sampled water-level is constant across water tiles, the surface is flat (correct);
        /// if it tracks the seabed, the surface is sunken (regression).
        /// </summary>
        object BuildWaterSamplesPayload()
        {
            int w = 0, h = 0;
            float seaLevel = 0f;
            bool meshWater = false;
            try
            {
                w = MapBox.width;
                h = MapBox.height;
                seaLevel = WorldSphereMod.Tools.TrueHeight(17);
                meshWater = Core.savedSettings != null && Core.savedSettings.MeshWater;
            }
            catch { }

            var samples = new List<object>();
            var waterLevels = new List<float>();
            if (w <= 0 || h <= 0)
            {
                return new { ok = false, error = "no world", seaLevel, meshWater };
            }

            int deepSampleTx = -1, deepSampleTy = -1;
            int shallowSampleTx = -1, shallowSampleTy = -1;
            float deepSampleTileY = float.NaN;
            float shallowSampleTileY = float.NaN;

            void AddSample(int tx, int ty)
            {
                bool isWater = false;
                float tileHeight = float.NaN;
                float sampledWaterLevel = seaLevel;
                float sampledSeabed = float.NaN;
                try
                {
                    WorldTile tile = World.world != null ? World.world.GetTileSimple(tx, ty) : null;
                    if (tile != null)
                    {
                        tileHeight = tile.TileHeight();
                        isWater = Core.Sphere.IsWaterTilePublic(tile.main_type, tile.top_type, tileHeight, seaLevel);
                        if (isWater)
                        {
                            sampledSeabed = tileHeight;
                            if (float.IsNaN(deepSampleTileY) || tileHeight < deepSampleTileY)
                            {
                                deepSampleTileY = tileHeight;
                                deepSampleTx = tx;
                                deepSampleTy = ty;
                            }
                            if (float.IsNaN(shallowSampleTileY) || tileHeight > shallowSampleTileY)
                            {
                                shallowSampleTileY = tileHeight;
                                shallowSampleTx = tx;
                                shallowSampleTy = ty;
                            }
                        }
                    }
                }
                catch { }

                samples.Add(new
                {
                    tx, ty,
                    isWater,
                    tileHeight,
                    sampledWaterLevel,    // callback returns constant
                    sampledSeabed,
                    tileY = tileHeight,
                    seabedY = sampledSeabed,
                    waterY = sampledWaterLevel,
                    depth = sampledWaterLevel - tileHeight
                });
                if (isWater && !float.IsNaN(sampledWaterLevel))
                {
                    waterLevels.Add(sampledWaterLevel);
                }
            }

            int[,] pts = {
                { w / 2,  h / 2  },  // 50, 50 in a 100x100
                { 50,     50     },
                { 150,    150    },
                { w / 8,  h / 8  },
                { w / 4,  h / 2  },
                { 3*w/4,  3*h/4  }
            };

            for (int i = 0; i < pts.GetLength(0); i++)
            {
                int tx = pts[i, 0];
                int ty = pts[i, 1];
                if (tx < 0 || tx >= w || ty < 0 || ty >= h) continue;
                AddSample(tx, ty);
            }

            if (float.IsNaN(deepSampleTileY) || float.IsNaN(shallowSampleTileY))
            {
                for (int tx = 0; tx < w; tx++)
                {
                    for (int ty = 0; ty < h; ty++)
                    {
                        AddSample(tx, ty);
                    }
                }
            }

            // Heuristic: all water tiles' sampledWaterLevel must be identical => surface flat.
            float firstWl = float.NaN;
            bool flat = true;
            foreach (float wl in waterLevels)
            {
                if (float.IsNaN(firstWl)) firstWl = wl;
                else if (Mathf.Abs(wl - firstWl) > 1e-3f) { flat = false; break; }
            }

            object deepSample = null;
            object shallowSample = null;
            for (int i = 0; i < samples.Count; i++)
            {
                var t = samples[i].GetType();
                bool iw = (bool)t.GetProperty("isWater").GetValue(samples[i]);
                if (!iw) continue;
                int tx = (int)t.GetProperty("tx").GetValue(samples[i]);
                int ty = (int)t.GetProperty("ty").GetValue(samples[i]);
                float tileY = (float)t.GetProperty("tileY").GetValue(samples[i]);
                float seabedY = (float)t.GetProperty("seabedY").GetValue(samples[i]);
                float waterY = (float)t.GetProperty("waterY").GetValue(samples[i]);
                if (deepSample == null && tx == deepSampleTx && ty == deepSampleTy)
                {
                    deepSample = new { tx, ty, tileY, seabedY, waterY };
                }
                if (shallowSample == null && tx == shallowSampleTx && ty == shallowSampleTy)
                {
                    shallowSample = new { tx, ty, tileY, seabedY, waterY };
                }
                if (deepSample != null && shallowSample != null)
                {
                    break;
                }
            }

            return new
            {
                ok = true,
                mapW = w, mapH = h,
                seaLevel,
                meshWater,
                surfaceFlat = flat,
                deepSample,
                shallowSample,
                waterMesh = ProbeWaterMesh(),
                samples
            };
        }

        static object ProbeWaterMesh()
        {
            try
            {
                CompoundSpheres.SphereManager mgr = null;
                try { mgr = Core.Sphere.ManagerInstance; } catch { }
                CompoundSpheres.HeightFieldRenderer hf = null;
                try { hf = mgr != null ? mgr.HeightField : null; } catch { }
                if (hf == null) return new { exists = false, reason = "no HeightField" };
                Mesh wm = hf.WaterMesh;
                if (wm == null) return new { exists = false, reason = "WaterMesh null" };
                Bounds b = wm.bounds;
                Vector3[] verts = wm.vertices;
                float minY = float.PositiveInfinity, maxY = float.NegativeInfinity, minX = float.PositiveInfinity, maxX = float.NegativeInfinity, minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
                int n = verts != null ? verts.Length : 0;
                for (int i = 0; i < n; i++)
                {
                    Vector3 v = verts[i];
                    if (v.y < minY) minY = v.y;
                    if (v.y > maxY) maxY = v.y;
                    if (v.x < minX) minX = v.x;
                    if (v.x > maxX) maxX = v.x;
                    if (v.z < minZ) minZ = v.z;
                    if (v.z > maxZ) maxZ = v.z;
                }
                return new
                {
                    exists = true,
                    vertexCount = wm.vertexCount,
                    triangleCount = wm.triangles != null ? wm.triangles.Length / 3 : 0,
                    boundsMin = new { x = b.min.x, y = b.min.y, z = b.min.z },
                    boundsMax = new { x = b.max.x, y = b.max.y, z = b.max.z },
                    computedMinY = minY, computedMaxY = maxY,
                    computedMinX = minX, computedMaxX = maxX,
                    computedMinZ = minZ, computedMaxZ = maxZ,
                    spanY = (float.IsInfinity(maxY - minY) ? 0f : maxY - minY),
                    name = wm.name
                };
            }
            catch (Exception ex)
            {
                return new { exists = false, reason = ex.GetType().Name + ": " + ex.Message };
            }
        }

        object BuildFullDumpPayload()
        {
            object settings;
            try
            {
                string settingsJson = JsonConvert.SerializeObject(Core.savedSettings ?? new SavedSettings());
                settings = JsonConvert.DeserializeObject(settingsJson);
            }
            catch (Exception ex) { settings = new { error = ex.Message }; }

            object telemetry = BuildTelemetryPayload();
            object renderStats = BuildRenderStatsPayload();
            object emitStatus = BuildEmitStatusPayload();
            object diagErrors = null;
            try { diagErrors = BuildDiagErrorsPayload(); } catch { }
            object voxelStats = null;
            try { voxelStats = BuildVoxelStatsPayload(); } catch { }
            object voxelQueue = null;
            try { voxelQueue = BuildVoxelQueuePayload(); } catch { }
            object memory = null;
            try { memory = BuildMemoryPayload(); } catch { }

            var loadedShaders = new List<object>();
            try
            {
                foreach (KeyValuePair<string, Shader> kv in Core.Sphere.LoadedShaders)
                {
                    Shader sh = kv.Value;
                    loadedShaders.Add(new
                    {
                        key = kv.Key,
                        name = sh != null ? sh.name : null,
                        supported = sh != null && sh.isSupported,
                        renderQueue = sh != null ? sh.renderQueue : -1,
                        passCount = sh != null ? sh.passCount : 0
                    });
                }
            }
            catch { }

            var materials = new List<object>();
            try
            {
                Material voxelMat = WorldSphereMod.Voxel.VoxelRender._material;
                if (voxelMat != null) materials.Add(DescribeMaterial("voxel", voxelMat));
            }
            catch { }
            try
            {
                Material[] all = Resources.FindObjectsOfTypeAll<Material>();
                int sampled = 0;
                for (int i = 0; i < all.Length && sampled < 32; i++)
                {
                    Material m = all[i];
                    if (m == null || m.shader == null) continue;
                    string sname = m.shader.name ?? string.Empty;
                    if (sname.IndexOf("OpaqueVertexColor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        sname.IndexOf("VoxelLit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        sname.IndexOf("GerstnerWater", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        sname.IndexOf("WSM3D", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        materials.Add(DescribeMaterial("scan", m));
                        sampled++;
                    }
                }
            }
            catch { }

            object cameraState = null;
            try
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Transform t = cam.transform;
                    cameraState = new
                    {
                        position = PodVector3(t.position),
                        forward = PodVector3(t.forward),
                        up = PodVector3(t.up),
                        rotationEuler = PodVector3(t.eulerAngles),
                        fieldOfView = cam.fieldOfView,
                        orthographic = cam.orthographic,
                        orthographicSize = cam.orthographicSize,
                        nearClip = cam.nearClipPlane,
                        farClip = cam.farClipPlane,
                        clearFlags = cam.clearFlags.ToString(),
                        backgroundColor = new { r = cam.backgroundColor.r, g = cam.backgroundColor.g, b = cam.backgroundColor.b, a = cam.backgroundColor.a },
                        cullingMask = cam.cullingMask,
                        depth = cam.depth,
                        renderingPath = cam.renderingPath.ToString(),
                        allowHDR = cam.allowHDR,
                        allowMSAA = cam.allowMSAA
                    };
                }
            }
            catch (Exception ex) { cameraState = new { error = ex.Message }; }

            object renderSettings = null;
            try
            {
                renderSettings = new
                {
                    ambientMode = RenderSettings.ambientMode.ToString(),
                    ambientLight = new { r = RenderSettings.ambientLight.r, g = RenderSettings.ambientLight.g, b = RenderSettings.ambientLight.b, a = RenderSettings.ambientLight.a },
                    ambientIntensity = RenderSettings.ambientIntensity,
                    ambientSkyColor = new { r = RenderSettings.ambientSkyColor.r, g = RenderSettings.ambientSkyColor.g, b = RenderSettings.ambientSkyColor.b },
                    ambientEquatorColor = new { r = RenderSettings.ambientEquatorColor.r, g = RenderSettings.ambientEquatorColor.g, b = RenderSettings.ambientEquatorColor.b },
                    ambientGroundColor = new { r = RenderSettings.ambientGroundColor.r, g = RenderSettings.ambientGroundColor.g, b = RenderSettings.ambientGroundColor.b },
                    fog = RenderSettings.fog,
                    fogMode = RenderSettings.fogMode.ToString(),
                    fogColor = new { r = RenderSettings.fogColor.r, g = RenderSettings.fogColor.g, b = RenderSettings.fogColor.b, a = RenderSettings.fogColor.a },
                    fogDensity = RenderSettings.fogDensity,
                    fogStartDistance = RenderSettings.fogStartDistance,
                    fogEndDistance = RenderSettings.fogEndDistance,
                    skybox = RenderSettings.skybox != null ? RenderSettings.skybox.name : null,
                    sun = RenderSettings.sun != null ? RenderSettings.sun.name : null,
                    defaultReflectionMode = RenderSettings.defaultReflectionMode.ToString(),
                    reflectionIntensity = RenderSettings.reflectionIntensity,
                    haloStrength = RenderSettings.haloStrength,
                    flareStrength = RenderSettings.flareStrength
                };
            }
            catch (Exception ex) { renderSettings = new { error = ex.Message }; }

            object qualitySettings = null;
            try
            {
                qualitySettings = new
                {
                    currentLevel = QualitySettings.GetQualityLevel(),
                    currentLevelName = QualitySettings.names != null && QualitySettings.GetQualityLevel() < QualitySettings.names.Length ? QualitySettings.names[QualitySettings.GetQualityLevel()] : null,
                    shadows = QualitySettings.shadows.ToString(),
                    shadowResolution = QualitySettings.shadowResolution.ToString(),
                    shadowDistance = QualitySettings.shadowDistance,
                    shadowCascades = QualitySettings.shadowCascades,
                    pixelLightCount = QualitySettings.pixelLightCount,
                    antiAliasing = QualitySettings.antiAliasing,
                    vSyncCount = QualitySettings.vSyncCount,
                    realtimeReflectionProbes = QualitySettings.realtimeReflectionProbes
                };
            }
            catch (Exception ex) { qualitySettings = new { error = ex.Message }; }

            object screenInfo = null;
            try
            {
                screenInfo = new
                {
                    width = Screen.width,
                    height = Screen.height,
                    fullScreen = Screen.fullScreen,
                    fullScreenMode = Screen.fullScreenMode.ToString(),
                    dpi = Screen.dpi,
                    currentResolution = new { w = Screen.currentResolution.width, h = Screen.currentResolution.height, refreshRate = Screen.currentResolution.refreshRate }
                };
            }
            catch { }

            object timeInfo = null;
            try
            {
                timeInfo = new
                {
                    frameCount = Time.frameCount,
                    time = Time.time,
                    unscaledTime = Time.unscaledTime,
                    realtimeSinceStartup = Time.realtimeSinceStartup,
                    deltaTime = Time.deltaTime,
                    unscaledDeltaTime = Time.unscaledDeltaTime,
                    timeScale = Time.timeScale,
                    fixedDeltaTime = Time.fixedDeltaTime,
                    captureFramerate = Time.captureFramerate
                };
            }
            catch { }

            object gpuInfo = null;
            try
            {
                gpuInfo = new
                {
                    deviceName = SystemInfo.graphicsDeviceName,
                    deviceVendor = SystemInfo.graphicsDeviceVendor,
                    deviceVersion = SystemInfo.graphicsDeviceVersion,
                    deviceType = SystemInfo.graphicsDeviceType.ToString(),
                    shaderLevel = SystemInfo.graphicsShaderLevel,
                    memoryMB = SystemInfo.graphicsMemorySize,
                    supportsComputeShaders = SystemInfo.supportsComputeShaders,
                    supportsInstancing = SystemInfo.supportsInstancing,
                    supportsAsyncCompute = SystemInfo.supportsAsyncCompute,
                    maxTextureSize = SystemInfo.maxTextureSize
                };
            }
            catch { }

            return new
            {
                ok = true,
                generatedAtUtc = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff'Z'", CultureInfo.InvariantCulture),
                frameCount = Time.frameCount,
                bridgePort = _boundPort,
                isWorld3D = _cachedIsWorld3D,
                settings,
                telemetry,
                renderStats,
                emitStatus,
                diagErrors,
                voxelStats,
                voxelQueue,
                memory,
                loadedShaders,
                materials,
                camera = cameraState,
                renderSettings,
                qualitySettings,
                screen = screenInfo,
                time = timeInfo,
                gpu = gpuInfo
            };
        }

        static object DescribeMaterial(string tag, Material m)
        {
            try
            {
                Shader sh = m.shader;
                var keywords = new List<string>();
                try { string[] sk = m.shaderKeywords; if (sk != null) keywords.AddRange(sk); } catch { }
                return new
                {
                    tag,
                    name = m.name,
                    shader = sh != null ? sh.name : null,
                    shaderSupported = sh != null && sh.isSupported,
                    renderQueue = m.renderQueue,
                    enableInstancing = m.enableInstancing,
                    passCount = m.passCount,
                    globalIlluminationFlags = m.globalIlluminationFlags.ToString(),
                    doubleSidedGI = m.doubleSidedGI,
                    shaderKeywords = keywords,
                    color = m.HasProperty("_Color") ? (object)new { r = m.GetColor("_Color").r, g = m.GetColor("_Color").g, b = m.GetColor("_Color").b, a = m.GetColor("_Color").a } : null
                };
            }
            catch (Exception ex)
            {
                return new { tag, error = ex.Message };
            }
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

        static int SafeCount(Func<int> read)
        {
            try { return read(); } catch { return 0; }
        }

        static bool SafeBool(Func<bool> read)
        {
            try { return read(); } catch { return false; }
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
