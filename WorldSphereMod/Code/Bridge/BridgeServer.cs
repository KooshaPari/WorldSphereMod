using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Drawing;
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

        readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        HttpListener? _listener;
        Thread? _listenerThread;
        volatile bool _running;
        int _mainThreadId;
        int _boundPort = Port;

        public static bool EnableFailed;

        public static void EnsureCreated()
        {
            if (Mod.Object == null) return;
            try
            {
                if (Mod.Object.GetComponent<BridgeServer>() != null) return;
                Mod.Object.AddComponent<BridgeServer>();
            }
            catch (Exception ex)
            {
                EnableFailed = true;
                Debug.LogWarning("[WSM3D][Bridge] failed to create bridge server: " + ex.Message);
            }
        }

        void Awake()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            StartListener();
        }

        void Update()
        {
            while (_mainThreadQueue.TryDequeue(out Action? work))
            {
                try { work?.Invoke(); }
                catch (Exception ex) { Debug.LogWarning("[WSM3D][Bridge] main-thread work failed: " + ex.Message); }
            }
        }

        void OnDestroy() => StopListener();

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
                    if (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase)) { WriteJson(context.Response, InvokeOnMainThread(BuildHealthPayload)); return; }
                    if (string.Equals(path, "/telemetry", StringComparison.OrdinalIgnoreCase)) { WriteJson(context.Response, InvokeOnMainThread(BuildTelemetryPayload)); return; }
                    if (string.Equals(path, "/settings", StringComparison.OrdinalIgnoreCase)) { WriteRawJson(context.Response, InvokeOnMainThread(BuildSettingsJson)); return; }
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
                    WriteJson(context.Response, InvokeOnMainThread(() => UpdateSetting(key, rawValue)));
                    return;
                }
                else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && string.Equals(path, "/actions/load_save", StringComparison.OrdinalIgnoreCase))
                {
                    string slotText = context.Request.QueryString["slot"] ?? string.Empty;
                    WriteJson(context.Response, InvokeOnMainThread(() => LoadSave(slotText)));
                    return;
                }
                else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && string.Equals(path, "/actions/screenshot", StringComparison.OrdinalIgnoreCase))
                {
                    string outputPath = context.Request.QueryString["path"] ?? string.Empty;
                    WriteJson(context.Response, InvokeOnMainThread(() => CaptureScreenshot(outputPath)));
                    return;
                }
                else if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && string.Equals(path, "/diag/dump_now", StringComparison.OrdinalIgnoreCase))
                {
                    WriteJson(context.Response, InvokeOnMainThread(ForceDiagDumpNow));
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
            version = Core.savedSettings != null ? Core.savedSettings.Version : "unknown",
            isWorld3D = Core.IsWorld3D
        };

        object BuildTelemetryPayload() => new
        {
            frameMs = Time.unscaledDeltaTime * 1000f,
            voxelCacheHit = SafeHitRate(() => WorldSphereMod.Voxel.VoxelMeshCache.HitCount, () => WorldSphereMod.Voxel.VoxelMeshCache.MissCount),
            impostorCacheHit = SafeHitRate(() => WorldSphereMod.LOD.ImpostorBillboard.HitCount, () => WorldSphereMod.LOD.ImpostorBillboard.MissCount),
            drawCalls = SafeLong(() => WorldSphereMod.Voxel.MeshInstanceBatcher.FrameDrawCalls),
            instances = SafeLong(() => WorldSphereMod.Voxel.MeshInstanceBatcher.FrameInstances)
        };

        string BuildSettingsJson() => JsonConvert.SerializeObject(Core.savedSettings ?? new SavedSettings(), Formatting.Indented);

        object UpdateSetting(string key, string rawValue)
        {
            if (string.IsNullOrWhiteSpace(key)) return new { ok = false, error = "missing_setting_key" };
            FieldInfo? field = typeof(SavedSettings).GetField(key, SettingFlags);
            if (field == null) return new { ok = false, error = "unknown_setting", key };
            if (!TryParseSettingValue(field.FieldType, rawValue, out object? parsed, out string parseError))
                return new { ok = false, error = parseError, key, value = rawValue };

            field.SetValue(Core.savedSettings, parsed);
            Core.SaveSettings();
            if (field.FieldType == typeof(bool)) Core.ApplyPhaseToggle(field.Name, (bool)parsed);
            return new { ok = true, key = field.Name, value = parsed };
        }

        object LoadSave(string slotText)
        {
            if (!int.TryParse(slotText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) || slot < 0)
            {
                return new { ok = false, error = "invalid_slot", slot = slotText };
            }

            string path = FindSavePath(slot);
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return new { ok = false, error = "missing_save", slot, path };
            }

            if (World.world == null || World.world.save_manager == null)
            {
                return new { ok = false, error = "world_not_ready", slot, path };
            }

            SaveManager.setCurrentPathAndId(path, slot);
            World.world.save_manager.prepareLoading();
            World.world.save_manager.loadWorld(path);
            return new { ok = true, slot, path };
        }

        object CaptureScreenshot(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return new { ok = false, error = "missing_path" };
            }

            try
            {
                string fullPath = Path.GetFullPath(outputPath);
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                int width = UnityEngine.Screen.width;
                int height = UnityEngine.Screen.height;
                if (width <= 0 || height <= 0)
                {
                    return new { ok = false, error = "invalid_screen_size", width, height, path = fullPath };
                }

                using (var bitmap = new Bitmap(width, height))
                using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(0, 0, 0, 0, new Size(width, height));
                    bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
                }

                return new { ok = true, path = fullPath, width, height };
            }
            catch (Exception ex)
            {
                return new { ok = false, error = ex.Message, path = outputPath };
            }
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

        static bool TryParseSettingValue(Type fieldType, string rawValue, out object? parsed, out string error)
        {
            parsed = null;
            error = string.Empty;
            try
            {
                if (fieldType == typeof(string)) { parsed = rawValue; return true; }
                if (fieldType == typeof(bool))
                {
                    if (bool.TryParse(rawValue, out bool boolValue) || string.Equals(rawValue, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(rawValue, "0", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed = !string.Equals(rawValue, "0", StringComparison.OrdinalIgnoreCase) && (bool.TryParse(rawValue, out boolValue) ? boolValue : true);
                        return true;
                    }
                    error = "invalid_bool";
                    return false;
                }
                if (fieldType == typeof(int)) { if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue)) { parsed = intValue; return true; } error = "invalid_int"; return false; }
                if (fieldType == typeof(float)) { if (float.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float floatValue)) { parsed = floatValue; return true; } error = "invalid_float"; return false; }
                if (fieldType.IsEnum) { parsed = Enum.Parse(fieldType, rawValue, ignoreCase: true); return true; }
                parsed = Convert.ChangeType(rawValue, fieldType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                error = "invalid_value:" + ex.Message;
                return false;
            }
        }

        T InvokeOnMainThread<T>(Func<T> func)
        {
            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId) return func();
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
                done.Wait();
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
