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

            foreach (string flagName in PhaseFlags)
            {
                FieldInfo field = typeof(SavedSettings).GetField(flagName);
                if (field == null)
                {
                    Debug.LogWarning("[WSM3D] AutoTest: missing phase flag " + flagName);
                    continue;
                }

                SetPhase(field, flagName, true);
                // Give the new phase a frame to register patches, then settle.
                yield return null;
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

            Debug.Log("[WSM3D] AutoTest: complete");
        }

        static void SetPhase(FieldInfo field, string flagName, bool value)
        {
            field.SetValue(Core.savedSettings, value);
            Core.SaveSettings();
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
