using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// Background prewarm pass for voxel mesh cache. Runs on world/sphere start so
    /// sprite-derived meshes begin building immediately and first rendered frames are
    /// much more likely cache-warm.
    /// </summary>
    [HarmonyPatch(typeof(Core.Sphere), nameof(Core.Sphere.Begin))]
    public static class VoxelMeshPrewarmPass
    {
        static readonly HashSet<int> _queuedSpriteIds = new HashSet<int>();
        static readonly HashSet<Building> _queuedBuildings = new HashSet<Building>(ReferenceEqualityComparer<Building>.Instance);
        static bool _patchRunning;

        [HarmonyPostfix]
        public static void OnSphereBegin()
        {
            if (World.world == null || !Core.IsWorld3D)
            {
                return;
            }

            if (!Core.savedSettings.VoxelEntities && !Core.savedSettings.ProceduralBuildings)
            {
                return;
            }

            if (_patchRunning) return;
            _patchRunning = true;
            try
            {
                int total = 0;
                total += EnqueueTileSprites();
                total += EnqueueActorSprites();
                total += EnqueueBuildingSprites();
                Debug.Log($"[WSM3D] VoxelMeshPrewarmPass: queued {total} unique sprite meshes.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WSM3D] VoxelMeshPrewarmPass failed: " + ex);
            }
            finally
            {
                _queuedSpriteIds.Clear();
                _queuedBuildings.Clear();
                _patchRunning = false;
            }
        }

        static int EnqueueTileSprites()
        {
            int count = 0;
            if (World.world?.tiles_list == null || World.world.tilemap == null)
            {
                return 0;
            }

            WorldTile[] tiles = World.world.tiles_list;
            for (int i = 0; i < tiles.Length; i++)
            {
                WorldTile tile = tiles[i];
                if (tile == null)
                {
                    continue;
                }

                try
                {
                    var tileVariation = World.world.tilemap.getVariation(tile);
                    if (tileVariation == null) continue;
                    Sprite sprite = tileVariation.sprite;
                    if (sprite == null) continue;

                    if (EnqueueSprite(sprite))
                    {
                        count++;
                    }
                }
                catch (System.Exception)
                {
                    continue;
                }
            }

            return count;
        }

        static int EnqueueActorSprites()
        {
            int count = 0;
            foreach (Actor actor in World.world.units)
            {
                if (actor == null) continue;
                Sprite sprite;
                try
                {
                    sprite = actor.calculateMainSprite();
                }
                catch (System.Exception)
                {
                    continue;
                }

                if (sprite == null) continue;
                if (EnqueueSprite(sprite))
                {
                    count++;
                }
            }

            return count;
        }

        static int EnqueueBuildingSprites()
        {
            if (World.world.buildings == null)
            {
                return 0;
            }

            int count = 0;
            Type managerType = World.world.buildings.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo[] fields = managerType.GetFields(flags);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                Type fieldType = field.FieldType;
                if (fieldType == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(fieldType))
                {
                    continue;
                }

                object value = field.GetValue(World.world.buildings);
                if (value == null) continue;

                if (value is Building[] array)
                {
                    for (int j = 0; j < array.Length; j++)
                    {
                        count += TryEnqueueBuildingSprite(array[j]);
                    }
                    continue;
                }

                if (!(value is IEnumerable enumerable))
                {
                    continue;
                }

                foreach (object element in enumerable)
                {
                    if (element is Building building)
                    {
                        count += TryEnqueueBuildingSprite(building);
                    }
                }
            }

            return count;
        }

        static int TryEnqueueBuildingSprite(Building building)
        {
            if (building == null || !_queuedBuildings.Add(building))
            {
                return 0;
            }

            Sprite sprite;
            try
            {
                sprite = building.calculateMainSprite();
            }
            catch (System.Exception)
            {
                return 0;
            }

            if (sprite == null) return 0;
            return EnqueueSprite(sprite) ? 1 : 0;
        }

        static bool EnqueueSprite(Sprite sprite)
        {
            if (sprite == null) return false;

            int key = sprite.GetInstanceID();
            if (!_queuedSpriteIds.Add(key))
            {
                return false;
            }

            VoxelMeshCache.Get(sprite, -1, false);
            return true;
        }

        sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();
            public bool Equals(T x, T y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(T obj)
            {
                return obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
