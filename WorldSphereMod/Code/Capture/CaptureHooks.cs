using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WorldSphereMod.Capture
{
    /// <summary>
    /// Harmony Postfix hooks that passively normalize the user's in-game actions into
    /// <see cref="CaptureEvent"/>s and append them to the active session (see
    /// <see cref="CaptureRecorder"/>).
    ///
    /// Each hook targets the SAME backing WorldBox method that the bridge /actions/* drivers
    /// (<see cref="WorldSphereMod.Bridge.BridgeActions"/>) invoke, so what we record is exactly
    /// what we can replay — the capture/replay round-trip is symmetric by construction.
    ///
    /// All hooks are Postfix (record only after the engine accepted the action), swallow every
    /// exception (a capture failure must never perturb gameplay), and self-suppress during replay
    /// via <see cref="CaptureRecorder.SuppressForReplay"/>.
    ///
    /// Registered explicitly from Core.Initialize via Harmony.PatchAll(type) — matching the
    /// existing Bridge* hook registration convention.
    /// </summary>
    internal static class CaptureReflection
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Read a public/backing field or property named <paramref name="member"/> as string.</summary>
        public static string ReadId(object instance, string member = "id")
        {
            if (instance == null) return null;
            try
            {
                Type t = instance.GetType();
                FieldInfo f = t.GetField(member, All);
                if (f != null) return f.GetValue(instance) as string;
                PropertyInfo p = t.GetProperty(member, All);
                if (p != null) return p.GetValue(instance, null) as string;
            }
            catch { }
            return null;
        }
    }

    /// <summary>use_tool: every applied god-power click on a tile. PlayerControl.clickedFinal is the
    /// shared sink for finger/brush clicks and resolves the selected power when pPower is null —
    /// the same one BridgeActions.UseTool drives through click_action.</summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.clickedFinal))]
    public static class CaptureClickHook
    {
        [HarmonyPostfix]
        public static void Postfix(Vector2Int pPos, GodPower pPower)
        {
            if (!CaptureRecorder.Enabled) return;
            try
            {
                GodPower power = pPower;
                if (power == null && World.world != null && World.world.selected_buttons != null && World.world.selected_buttons.selectedButton != null)
                    power = World.world.selected_buttons.selectedButton.godPower;
                string id = power != null ? power.id : null;
                if (string.IsNullOrEmpty(id)) return; // inspect/empty clicks carry no tool — skip
                CaptureRecorder.Record(
                    CaptureRecorder.Emit(CaptureEventTypes.UseTool)
                        .Arg("id", id).Arg("x", pPos.x).Arg("y", pPos.y));
            }
            catch (Exception ex) { Debug.LogWarning("[WSM3D][Capture] click hook: " + ex.Message); }
        }
    }

    /// <summary>select_tool: toolbar power selection (PowerButtonSelector.setSelectedPower) — the
    /// path BridgeActions.SelectTool routes through.</summary>
    [HarmonyPatch(typeof(PowerButtonSelector), nameof(PowerButtonSelector.setSelectedPower))]
    public static class CaptureSelectToolHook
    {
        [HarmonyPostfix]
        public static void Postfix(GodPower pPower)
        {
            if (!CaptureRecorder.Enabled) return;
            try
            {
                string id = pPower != null ? pPower.id : null;
                if (string.IsNullOrEmpty(id)) return;
                CaptureRecorder.Record(CaptureRecorder.Emit(CaptureEventTypes.SelectTool).Arg("id", id));
            }
            catch (Exception ex) { Debug.LogWarning("[WSM3D][Capture] select_tool hook: " + ex.Message); }
        }
    }

    /// <summary>new_world: the "Generate New World" menu button (MapBox.clickGenerateNewMap) —
    /// BridgeActions.NewWorld's primary target.</summary>
    [HarmonyPatch(typeof(MapBox), nameof(MapBox.clickGenerateNewMap))]
    public static class CaptureNewWorldHook
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!CaptureRecorder.Enabled) return;
            try { CaptureRecorder.Record(CaptureRecorder.Emit(CaptureEventTypes.NewWorld)); }
            catch (Exception ex) { Debug.LogWarning("[WSM3D][Capture] new_world hook: " + ex.Message); }
        }
    }

    /// <summary>load_save: a save being loaded (SaveManager.loadWorld(string,bool)) — mirrors the
    /// bridge POST /actions/load_save path. Records the slot via SaveManager.currentSlotId when
    /// resolvable so replay can re-target the right slot.</summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadWorld), new Type[] { typeof(string), typeof(bool) })]
    public static class CaptureLoadSaveHook
    {
        [HarmonyPostfix]
        public static void Postfix(string pPath)
        {
            if (!CaptureRecorder.Enabled) return;
            try
            {
                int slot = ResolveSlot(pPath);
                CaptureEvent evt = CaptureRecorder.Emit(CaptureEventTypes.LoadSave).Arg("slot", slot);
                if (!string.IsNullOrEmpty(pPath)) evt.Arg("path", pPath);
                CaptureRecorder.Record(evt);
            }
            catch (Exception ex) { Debug.LogWarning("[WSM3D][Capture] load_save hook: " + ex.Message); }
        }

        // Best-effort: parse the trailing "saveN" folder from the path so replay maps onto the
        // bridge's slot-based load. Falls back to 0 (the bridge re-resolves the path from slot).
        static int ResolveSlot(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return 0;
                string leaf = new System.IO.DirectoryInfo(path.TrimEnd('/', '\\')).Name;
                if (leaf != null && leaf.StartsWith("save", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(leaf.Substring(4), out int n)) return n;
            }
            catch { }
            return 0;
        }
    }

    /// <summary>set_speed: world time-scale changes via the named-id overload
    /// (Config.setWorldSpeed(string,bool)) — what BridgeActions.SetSpeed uses.</summary>
    [HarmonyPatch(typeof(Config), nameof(Config.setWorldSpeed), new Type[] { typeof(string), typeof(bool) })]
    public static class CaptureSetSpeedByIdHook
    {
        [HarmonyPostfix]
        public static void Postfix(string pID)
        {
            if (!CaptureRecorder.Enabled) return;
            try
            {
                if (string.IsNullOrEmpty(pID)) return;
                CaptureRecorder.Record(CaptureRecorder.Emit(CaptureEventTypes.SetSpeed).Arg("speed", pID));
            }
            catch (Exception ex) { Debug.LogWarning("[WSM3D][Capture] set_speed(id) hook: " + ex.Message); }
        }
    }

    /// <summary>set_speed: the asset overload (Config.setWorldSpeed(WorldTimeScaleAsset,bool)) used
    /// by the in-game speed buttons; we read the asset id so it replays through the named-id path.</summary>
    [HarmonyPatch(typeof(Config), nameof(Config.setWorldSpeed), new Type[] { typeof(WorldTimeScaleAsset), typeof(bool) })]
    public static class CaptureSetSpeedByAssetHook
    {
        [HarmonyPostfix]
        public static void Postfix(WorldTimeScaleAsset pAsset)
        {
            if (!CaptureRecorder.Enabled) return;
            try
            {
                string id = CaptureReflection.ReadId(pAsset);
                if (string.IsNullOrEmpty(id)) return;
                CaptureRecorder.Record(CaptureRecorder.Emit(CaptureEventTypes.SetSpeed).Arg("speed", id));
            }
            catch (Exception ex) { Debug.LogWarning("[WSM3D][Capture] set_speed(asset) hook: " + ex.Message); }
        }
    }

    /// <summary>camera (zoom): user zoom via MapBox.setZoomOrthographic — pairs with the periodic
    /// camera snapshot in CaptureCameraSampler for pan. Records the post-zoom camera frame so the
    /// replay reproduces both zoom and position in one /actions/camera call.</summary>
    [HarmonyPatch(typeof(MapBox), nameof(MapBox.setZoomOrthographic))]
    public static class CaptureZoomHook
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!CaptureRecorder.Enabled) return;
            try { CaptureCameraSampler.RecordCameraFrame(force: true); }
            catch (Exception ex) { Debug.LogWarning("[WSM3D][Capture] zoom hook: " + ex.Message); }
        }
    }
}
