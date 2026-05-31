using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace WorldSphereMod.Bridge
{
    /// <summary>
    /// Headless drivers for WorldBox's toolbar + on-screen actions so a remote operator never clicks.
    /// All WorldBox members are resolved by reflection/AccessTools-style lookups (publicizer trap:
    /// the build sees publicized fields, but we never assume a name exists — missing names return
    /// {ok:false,error:"not found:X"} instead of throwing). MUST be called on the Unity main thread.
    /// </summary>
    public static class BridgeActions
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // MapBox.instance is the live world host; it exists from game boot (WorldBox has no separate title scene).
        static MapBox Map => MapBox.instance;

        // ---- WORLD (highest priority) ----

        /// <summary>Drive WorldBox's "create world" button programmatically — clickGenerateNewMap is exactly what the menu button calls.</summary>
        public static object NewWorld()
        {
            MapBox map = Map;
            if (map == null) return Fail("world_host_not_ready");
            // clickGenerateNewMap = the menu "Generate New World" button entry; generateNewMap = lower-level fallback.
            object r = TryInvoke(map, "clickGenerateNewMap");
            if (r != null) return r;
            r = TryInvoke(map, "generateNewMap");
            if (r != null) return r;
            return NotFound("MapBox.clickGenerateNewMap/generateNewMap");
        }

        /// <summary>Regenerate the current map without touching the menu (startTheGame(true) forces a fresh gen).</summary>
        public static object Regenerate()
        {
            MapBox map = Map;
            if (map == null) return Fail("world_host_not_ready");
            object r = TryInvoke(map, "startTheGame", new object[] { true }, new[] { typeof(bool) });
            if (r != null) return r;
            r = TryInvoke(map, "generateNewMap");
            if (r != null) return r;
            return NotFound("MapBox.startTheGame/generateNewMap");
        }

        /// <summary>Save the current world into its current slot path (saveToCurrentPath mirrors the toolbar Save button).</summary>
        public static object Save()
        {
            MapBox map = Map;
            if (map == null || map.save_manager == null) return Fail("save_manager_not_ready");
            object r = TryInvoke(map.save_manager, "saveToCurrentPath");
            if (r != null) return r;
            return NotFound("SaveManager.saveToCurrentPath");
        }

        // ---- TIME ----

        /// <summary>Pause the simulation via Config.set_paused(true) — same flag the pause button toggles.</summary>
        public static object Pause() => SetPaused(true);

        /// <summary>Resume the simulation via Config.set_paused(false).</summary>
        public static object Play() => SetPaused(false);

        static object SetPaused(bool paused)
        {
            Type cfg = ResolveType("Config");
            if (cfg == null) return NotFound("Config");
            MethodInfo m = cfg.GetMethod("set_paused", All, null, new[] { typeof(bool) }, null);
            if (m == null) return NotFound("Config.set_paused");
            m.Invoke(null, new object[] { paused }); // why: drives WorldBox's global pause used by every sim system
            return new { ok = true, paused };
        }

        /// <summary>
        /// Set world speed. Accepts a named time-scale id (e.g. "normal","fast","faster"); falls back to
        /// numeric step count via Config.nextWorldSpeed when the arg is an integer with no matching id.
        /// </summary>
        public static object SetSpeed(string speed)
        {
            if (string.IsNullOrWhiteSpace(speed)) return Fail("missing_speed");
            Type cfg = ResolveType("Config");
            if (cfg == null) return NotFound("Config");

            // Named id path: Config.setWorldSpeed(string, bool).
            MethodInfo byId = cfg.GetMethod("setWorldSpeed", All, null, new[] { typeof(string), typeof(bool) }, null);
            bool isNumeric = int.TryParse(speed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int steps);
            if (byId != null && !isNumeric)
            {
                try { byId.Invoke(null, new object[] { speed, true }); return new { ok = true, speed }; }
                catch (Exception ex) { return Fail("set_speed_failed:" + ex.Message); }
            }

            // Numeric path: step nextWorldSpeed N times (0 = pause via set_paused).
            MethodInfo next = cfg.GetMethod("nextWorldSpeed", All, null, new[] { typeof(bool) }, null);
            if (next == null) return NotFound("Config.setWorldSpeed/nextWorldSpeed");
            if (steps <= 0) return SetPaused(true);
            SetPaused(false);
            for (int i = 0; i < Math.Min(steps, 8); i++) next.Invoke(null, new object[] { false }); // why: cap at 8 so a bad arg can't spin
            return new { ok = true, speedSteps = steps };
        }

        // ---- CAMERA ----

        /// <summary>Frame the 3D camera at world tile (x,y) with the given orthographic zoom so the operator can compose screenshots.</summary>
        public static object Camera(string xText, string yText, string zoomText)
        {
            MapBox map = Map;
            if (map == null) return Fail("world_host_not_ready");

            bool hasZoom = float.TryParse(zoomText, NumberStyles.Float, CultureInfo.InvariantCulture, out float zoom);
            if (hasZoom)
            {
                object zr = TryInvoke(map, "setZoomOrthographic", new object[] { zoom }, new[] { typeof(float) });
                if (zr == null) return NotFound("MapBox.setZoomOrthographic");
            }

            UnityEngine.Camera cam = SafeMainCamera(map);
            if (cam == null) return Fail("camera_not_ready");

            bool hasX = float.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
            bool hasY = float.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
            if (hasX || hasY)
            {
                Vector3 p = cam.transform.position; // why: keep depth (z), only reframe the XY plane the map lives on
                if (hasX) p.x = x;
                if (hasY) p.y = y;
                cam.transform.position = p;
            }

            Vector3 cp = cam.transform.position;
            return new { ok = true, x = cp.x, y = cp.y, zoom = cam.orthographic ? cam.orthographicSize : (float?)null, applied = new { x = hasX, y = hasY, zoom = hasZoom } };
        }

        /// <summary>Snap the camera to a named target: "village"/"unit"/"center". Uses WorldBox's own focus helpers where present.</summary>
        public static object CameraFocus(string target)
        {
            MapBox map = Map;
            if (map == null) return Fail("world_host_not_ready");
            string t = (target ?? "center").Trim().ToLowerInvariant();
            switch (t)
            {
                case "village":
                case "city":
                    if (TryInvoke(map, "locateSelectedVillage") != null) return new { ok = true, target = t };
                    break;
                case "center":
                    if (TryInvoke(map, "centerCamera") != null) return new { ok = true, target = t };
                    break;
            }
            // Generic fallback: center camera.
            if (TryInvoke(map, "centerCamera") != null) return new { ok = true, target = "center", note = "fell_back_to_center" };
            return NotFound("MapBox.centerCamera");
        }

        // ---- TOOLS ----

        /// <summary>List all god-power tool ids/names from AssetManager.powers (PowerLibrary).</summary>
        public static object ListTools()
        {
            PowerLibrary lib = ResolvePowerLibrary();
            if (lib == null) return NotFound("AssetManager.powers");
            var tools = new List<object>();
            try
            {
                System.Collections.IEnumerable list = lib.getList() as System.Collections.IEnumerable;
                if (list != null)
                    foreach (object o in list)
                    {
                        GodPower gp = o as GodPower;
                        if (gp != null) tools.Add(new { id = gp.id });
                    }
            }
            catch (Exception ex) { return Fail("enumerate_failed:" + ex.Message); }
            return new { ok = true, count = tools.Count, tools };
        }

        /// <summary>Select a god power by id — drives the toolbar selection (PowerButtonSelector) so it behaves like a click.</summary>
        public static object SelectTool(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return Fail("missing_tool_id");
            MapBox map = Map;
            if (map == null) return Fail("world_host_not_ready");
            GodPower power = ResolvePower(id);
            if (power == null) return Fail("unknown_tool:" + id);

            // Preferred: route through PowerButtonSelector.setSelectedPower so toolbar UI stays consistent.
            object sel = map.selected_buttons;
            if (sel != null)
            {
                MethodInfo m = sel.GetType().GetMethod("setSelectedPower", All, null, new[] { typeof(PowerButton), typeof(GodPower), typeof(bool) }, null);
                if (m != null)
                {
                    try { m.Invoke(sel, new object[] { null, power, true }); return new { ok = true, id = power.id, via = "PowerButtonSelector" }; }
                    catch { /* fall through to field-set */ }
                }
            }

            // Fallback: set MapBox's selected_power backing field directly (publicizer-safe via reflection).
            FieldInfo f = typeof(MapBox).GetField("selected_power", All) ?? typeof(MapBox).GetField("<selected_power>k__BackingField", All);
            if (f != null) { f.SetValue(map, power); return new { ok = true, id = power.id, via = "field" }; }
            return NotFound("PowerButtonSelector.setSelectedPower / MapBox.selected_power");
        }

        /// <summary>Apply a god power by id at world tile (x,y) — invokes the power's click_action delegate, the same one a click fires.</summary>
        public static object UseTool(string id, string xText, string yText)
        {
            if (string.IsNullOrWhiteSpace(id)) return Fail("missing_tool_id");
            MapBox map = Map;
            if (map == null) return Fail("world_host_not_ready");
            if (!int.TryParse(xText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(yText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
                return Fail("invalid_coords");

            GodPower power = ResolvePower(id);
            if (power == null) return Fail("unknown_tool:" + id);

            WorldTile tile = map.GetTile(x, y);
            if (tile == null) return Fail("tile_out_of_bounds");

            // GodPower.click_action : Boolean(WorldTile, String); falls back to click_power_action : Boolean(WorldTile, GodPower).
            object actWithId = GetFieldValue(power, "click_action");
            if (actWithId is Delegate dWithId)
            {
                bool ok = (bool)dWithId.DynamicInvoke(tile, power.id);
                return new { ok = true, applied = ok, id = power.id, x, y, via = "click_action" };
            }
            object actPow = GetFieldValue(power, "click_power_action");
            if (actPow is Delegate dPow)
            {
                bool ok = (bool)dPow.DynamicInvoke(tile, power);
                return new { ok = true, applied = ok, id = power.id, x, y, via = "click_power_action" };
            }
            return NotFound("GodPower.click_action/click_power_action");
        }

        // ---- STATE ----

        /// <summary>Full world snapshot for the operator: size, age, population, pause/speed, camera, isWorld3D.</summary>
        public static object WorldState(bool isWorld3D)
        {
            MapBox map = Map;
            bool hasWorld = map != null;

            int width = 0, height = 0;
            try { width = MapBox.width; height = MapBox.height; } catch { }

            long population = 0;
            string worldAge = null;
            double worldTime = 0;
            try
            {
                MapStats stats = hasWorld ? map.map_stats : null;
                if (stats != null) { population = stats.population; worldAge = stats.world_age_id; }
            }
            catch { }
            try { if (hasWorld) worldTime = map.getCurWorldTime(); } catch { }

            bool isPaused = false;
            try { Type cfg = ResolveType("Config"); MethodInfo gp = cfg?.GetMethod("get_paused", All, null, Type.EmptyTypes, null); if (gp != null) isPaused = (bool)gp.Invoke(null, null); } catch { }

            string speedId = null;
            try { Type cfg = ResolveType("Config"); FieldInfo tsf = cfg?.GetField("time_scale_asset", All); object asset = tsf?.GetValue(null); FieldInfo idf = asset?.GetType().GetField("id", All); speedId = idf?.GetValue(asset) as string; } catch { }

            object cam = null;
            try
            {
                UnityEngine.Camera c = SafeMainCamera(map);
                if (c != null)
                    cam = new { x = c.transform.position.x, y = c.transform.position.y, z = c.transform.position.z, zoom = c.orthographic ? c.orthographicSize : (float?)null };
            }
            catch { }

            return new
            {
                ok = true,
                hasWorld,
                isWorld3D,
                mapSize = new { width, height },
                population,
                worldAge,
                worldTime,
                isPaused,
                speed = speedId,
                camera = cam,
            };
        }

        // ---- reflection helpers ----

        static UnityEngine.Camera SafeMainCamera(MapBox map)
        {
            try { UnityEngine.Camera c = map != null ? map.camera : null; if (c != null) return c; } catch { }
            return UnityEngine.Camera.main;
        }

        static PowerLibrary ResolvePowerLibrary()
        {
            try
            {
                Type am = ResolveType("AssetManager");
                FieldInfo f = am?.GetField("powers", All);
                return f?.GetValue(null) as PowerLibrary;
            }
            catch { return null; }
        }

        static GodPower ResolvePower(string id)
        {
            PowerLibrary lib = ResolvePowerLibrary();
            if (lib == null) return null;
            try { GodPower p = lib.get(id); if (p != null) return p; } catch { }
            try { return lib.getSimple(id); } catch { return null; }
        }

        static Type ResolveType(string simpleName)
        {
            // WorldBox types live in Assembly-CSharp with no namespace; resolve by simple name.
            Type direct = typeof(MapBox).Assembly.GetType(simpleName, false);
            if (direct != null) return direct;
            foreach (Type t in typeof(MapBox).Assembly.GetTypes())
                if (t.Name == simpleName) return t;
            return null;
        }

        static object GetFieldValue(object instance, string field)
        {
            try { FieldInfo f = instance.GetType().GetField(field, All); return f?.GetValue(instance); }
            catch { return null; }
        }

        /// <summary>Invoke a void/any WorldBox method by name; returns {ok:true} on success, null when the method is absent.</summary>
        static object TryInvoke(object instance, string method, object[] args = null, Type[] sig = null)
        {
            try
            {
                Type t = instance.GetType();
                MethodInfo m = sig != null
                    ? t.GetMethod(method, All, null, sig, null)
                    : t.GetMethod(method, All, null, Type.EmptyTypes, null);
                if (m == null) return null; // signal "not found" to caller for guarded fallback
                object ret = m.Invoke(instance, args);
                return new { ok = true, method, result = ret as object };
            }
            catch (Exception ex)
            {
                return new { ok = false, method, error = ex.InnerException?.Message ?? ex.Message };
            }
        }

        static object Fail(string error) => new { ok = false, error };
        static object NotFound(string member) => new { ok = false, error = "not found:" + member };
    }
}
