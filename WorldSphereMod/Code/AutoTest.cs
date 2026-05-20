using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using WorldSphereMod.Voxel;

namespace WorldSphereMod
{
    public class AutoTestDriver : MonoBehaviour
    {
        static readonly string[] PhaseFlags =
        {
            nameof(SavedSettings.VoxelEntities),
            nameof(SavedSettings.ProceduralBuildings),
            nameof(SavedSettings.CrossedQuadFoliage),
            nameof(SavedSettings.MeshWater),
            nameof(SavedSettings.HighShadows),
            nameof(SavedSettings.SkeletalAnimation),
            nameof(SavedSettings.WorldspaceUI),
            nameof(SavedSettings.DayNightCycle),
            nameof(SavedSettings.PostFX),
            nameof(SavedSettings.ParticleEffects)
        };

        IEnumerator Start()
        {
            Debug.Log("[WSM3D] AutoTest: start");
            yield return new WaitForSeconds(5f);

            LoadLatestWorldOrGenerate();
            yield return new WaitForSeconds(5f);
            while (SmoothLoader.isLoading())
            {
                yield return null;
            }

            // Snapshot each flag's pre-test value so we can restore it after the
            // cycle. Without this, AutoTest leaves every phase OFF at runtime,
            // even when the disk config says ON — the user's enabled phases
            // would silently vanish after init.
            var preTestState = new System.Collections.Generic.Dictionary<string, bool>();
            foreach (string flagName in PhaseFlags)
            {
                FieldInfo snap = typeof(SavedSettings).GetField(flagName);
                if (snap == null) continue;
                preTestState[flagName] = (bool)snap.GetValue(Core.savedSettings);
            }

            foreach (string flagName in PhaseFlags)
            {
                FieldInfo field = typeof(SavedSettings).GetField(flagName);
                if (field == null)
                {
                    Debug.LogWarning("[WSM3D] AutoTest: missing phase flag " + flagName);
                    continue;
                }

                SetPhase(field, flagName, true);
                yield return null;
                ForceTilemapRefresh();
                yield return null;
                // Zero the static counters so the peak window measures only
                // THIS phase's work. Without this, FrameDrawCalls retains
                // the last Flush's values across phases that don't Submit
                // (and therefore don't Flush, so don't reset).
                MeshInstanceBatcher.FrameDrawCalls = 0;
                MeshInstanceBatcher.FrameInstances = 0;
                long peakDrawCalls = 0;
                long peakInstances = 0;
                for (int tick = 0; tick < 180; tick++)
                {
                    yield return null;
                    if (MeshInstanceBatcher.FrameDrawCalls > peakDrawCalls) peakDrawCalls = MeshInstanceBatcher.FrameDrawCalls;
                    if (MeshInstanceBatcher.FrameInstances > peakInstances) peakInstances = MeshInstanceBatcher.FrameInstances;
                }
                Debug.Log("[WSM3D] AutoTest: phase=" + flagName
                    + " peakDrawCalls=" + peakDrawCalls
                    + " peakInstances=" + peakInstances
                    + " useFallbackPath=" + MeshInstanceBatcher.UseFallbackPath
                    + " firstActorPos=" + GetFirstActorPos());
                SetPhase(field, flagName, false);
            }

            // Restore each flag to its pre-test value so the user's chosen
            // SavedSettings stays effective after AutoTest finishes.
            foreach (var kv in preTestState)
            {
                FieldInfo restore = typeof(SavedSettings).GetField(kv.Key);
                if (restore != null) SetPhase(restore, kv.Key, kv.Value);
            }

            Debug.Log("[WSM3D] AutoTest: complete (phase flags restored to pre-test values)");
        }

        static void SetPhase(FieldInfo field, string flagName, bool value)
        {
            field.SetValue(Core.savedSettings, value);
            // Do NOT persist AutoTest's mutations to disk — they leave the
            // user's default-on flags (CrossedQuadFoliage, MeshWater, etc.)
            // toggled OFF after every cycle, breaking subsequent normal runs.
            // Core.SaveSettings();
            Core.ApplyPhaseToggle(flagName, value);
        }

        static void LoadLatestWorldOrGenerate()
        {
            string path = FindLatestSavePath(out int slot);
            if (!string.IsNullOrEmpty(path))
            {
                Debug.Log("[WSM3D] AutoTest: loading save slot=" + slot + " path=" + path);
                SaveManager.setCurrentPathAndId(path, slot);
                World.world.save_manager.prepareLoading();
                World.world.save_manager.loadWorld(path);
                return;
            }

            Debug.LogWarning("[WSM3D] AutoTest: no save found; generating new world");
            World.world.startTheGame(true);
        }

        static string FindLatestSavePath(out int slot)
        {
            slot = 0;
            string root = SaveManager.persistentDataPath;
            if (string.IsNullOrEmpty(root))
            {
                root = Application.persistentDataPath;
            }

            string saves = Path.Combine(root, "saves");
            if (!Directory.Exists(saves))
            {
                return string.Empty;
            }

            string latestPath = string.Empty;
            DateTime latestWrite = DateTime.MinValue;
            foreach (string dir in Directory.GetDirectories(saves, "save*"))
            {
                DateTime write = GetSaveWriteTime(dir);
                if (write <= latestWrite)
                {
                    continue;
                }

                latestWrite = write;
                latestPath = dir;
                slot = ParseSlot(dir);
            }

            return latestPath;
        }

        static DateTime GetSaveWriteTime(string dir)
        {
            DateTime latest = DateTime.MinValue;
            foreach (string name in new[] { "map.wbox", "map.wbax", "map.json" })
            {
                string path = Path.Combine(dir, name);
                if (!File.Exists(path))
                {
                    continue;
                }

                DateTime write = File.GetLastWriteTime(path);
                if (write > latest)
                {
                    latest = write;
                }
            }

            return latest;
        }

        static int ParseSlot(string dir)
        {
            string name = new DirectoryInfo(dir).Name;
            if (name.StartsWith("save", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name.Substring(4), out int parsed))
            {
                return parsed;
            }

            return 0;
        }

        static void ForceTilemapRefresh()
        {
            try
            {
                WorldTilemap? wt = World.world?.tilemap as WorldTilemap;
                if (wt == null) wt = UnityEngine.Object.FindObjectOfType<WorldTilemap>();
                if (wt == null) return;
                MethodInfo m = typeof(WorldTilemap).GetMethod("rerenderEverything", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) m = typeof(WorldTilemap).GetMethod("refreshAll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) m = typeof(WorldTilemap).GetMethod("clearAndRedraw", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null && m.GetParameters().Length == 0)
                {
                    m.Invoke(wt, null);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WSM3D] AutoTest: ForceTilemapRefresh failed: " + e.GetType().Name);
            }
        }

        static string GetFirstActorPos()
        {
            try
            {
                foreach (Actor actor in World.world.units)
                {
                    return actor.cur_transform_position.ToString();
                }
            }
            catch (Exception e)
            {
                return "<error:" + e.GetType().Name + ">";
            }

            return "<none>";
        }
    }
}
