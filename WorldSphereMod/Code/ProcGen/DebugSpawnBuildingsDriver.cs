using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace WorldSphereMod.ProcGen
{
    public sealed class DebugSpawnBuildingsDriver : MonoBehaviour
    {
        static readonly SpawnSpec[] SpawnSpecs =
        {
            new SpawnSpec("house_human_0", 200, 270),
            new SpawnSpec("house_human_1", 204, 270),
            new SpawnSpec("house_human_2", 208, 270),
            new SpawnSpec("house_human_3", 212, 270),
            new SpawnSpec("watch_tower_human", 216, 270),
            new SpawnSpec("barracks_human", 221, 270),
            new SpawnSpec("temple_human", 200, 276),
            new SpawnSpec("library_human", 205, 276),
            new SpawnSpec("market_human", 210, 276),
            new SpawnSpec("windmill_human_0", 216, 276),
            new SpawnSpec("flame_tower", 221, 276),
            new SpawnSpec("ice_tower", 224, 282),
        };

        bool _worldLoaded;
        bool _ran;

        void OnEnable()
        {
            MapBox.on_world_loaded += OnWorldLoaded;
        }

        void OnDisable()
        {
            MapBox.on_world_loaded -= OnWorldLoaded;
        }

        void Update()
        {
            if (_ran || !Core.savedSettings.DebugSpawnBuildings)
            {
                return;
            }

            _worldLoaded |= IsWorldReady();
            if (!_worldLoaded)
            {
                return;
            }

            BuildingManager manager = World.world?.buildings;
            if (manager == null)
            {
                return;
            }

            MethodInfo addBuilding = ResolveAddBuildingMethod();
            if (addBuilding == null)
            {
                Debug.LogWarning("[WSM3D] DebugSpawnBuildings: BuildingManager.addBuilding(string, WorldTile, ...) not found");
                return;
            }

            _ran = true;
            int spawned = 0;
            foreach (SpawnSpec spec in SpawnSpecs)
            {
                if (TrySpawn(manager, addBuilding, spec))
                {
                    spawned++;
                }
            }

            Debug.Log($"[WSM3D] DebugSpawnBuildings: completed {spawned} spawns");
            enabled = false;
        }

        void OnWorldLoaded()
        {
            _worldLoaded = true;
        }

        static bool IsWorldReady()
        {
            return World.world != null
                && World.world.buildings != null
                && World.world.tiles_map != null
                && MapBox.width > 225
                && MapBox.height > 285;
        }

        static MethodInfo ResolveAddBuildingMethod()
        {
            return typeof(BuildingManager)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "addBuilding") return false;
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 5
                        && p[0].ParameterType == typeof(string)
                        && p[1].ParameterType == typeof(WorldTile);
                });
        }

        static bool TrySpawn(BuildingManager manager, MethodInfo addBuilding, SpawnSpec spec)
        {
            WorldTile tile = World.world.GetTile(spec.X, spec.Y);
            if (tile == null)
            {
                Debug.LogWarning($"[WSM3D] DebugSpawnBuildings: missing tile at ({spec.X}, {spec.Y})");
                return false;
            }

            if (AssetManager.buildings.get(spec.AssetId) == null)
            {
                Debug.LogWarning($"[WSM3D] DebugSpawnBuildings: missing asset {spec.AssetId}");
                return false;
            }

            ParameterInfo[] p = addBuilding.GetParameters();
            object placingType = Enum.Parse(p[4].ParameterType, "New");
            object building = addBuilding.Invoke(manager, new object[] { spec.AssetId, tile, false, false, placingType });
            if (building == null)
            {
                Debug.LogWarning($"[WSM3D] DebugSpawnBuildings: failed {spec.AssetId} at ({spec.X}, {spec.Y})");
                return false;
            }

            Debug.Log($"[WSM3D] DebugSpawnBuildings: spawned {spec.AssetId} at ({spec.X}, {spec.Y})");
            return true;
        }

        readonly struct SpawnSpec
        {
            public readonly string AssetId;
            public readonly int X;
            public readonly int Y;

            public SpawnSpec(string assetId, int x, int y)
            {
                AssetId = assetId;
                X = x;
                Y = y;
            }
        }
    }
}
