using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NeoModLoader.utils;
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
                if (Mod.Object != null && Mod.Object.GetComponent<PrewarmRunner>() == null)
                {
                    Mod.Object.AddComponent<PrewarmRunner>();
                }
                else
                {
                    Debug.LogWarning("[WSM3D] VoxelMeshPrewarmPass: no runner host available, falling back to sync queue.");
                    int total = 0;
                    total += EnqueueTileSprites();
                    total += EnqueueActorSprites();
                    total += EnqueueBuildingSprites();
                    Debug.Log($"[WSM3D] VoxelMeshPrewarmPass: queued {total} unique sprite meshes.");
                    _queuedSpriteIds.Clear();
                    _queuedBuildings.Clear();
                    _patchRunning = false;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WSM3D] VoxelMeshPrewarmPass failed: " + ex);
                _queuedSpriteIds.Clear();
                _queuedBuildings.Clear();
                _patchRunning = false;
            }
        }

        sealed class PrewarmRunner : MonoBehaviour
        {
            bool _started;

            void Awake()
            {
                if (_started)
                {
                    return;
                }

                _started = true;
                StartCoroutine(Run());
            }

            IEnumerator Run()
            {
                int total = 0;
                yield return StartCoroutine(EnqueueTileSpritesBatched(result => total += result));
                yield return null;
                yield return StartCoroutine(EnqueueActorSpritesBatched(result => total += result));
                yield return null;
                yield return StartCoroutine(EnqueueBuildingSpritesBatched(result => total += result));
                Debug.Log($"[WSM3D] VoxelMeshPrewarmPass: queued {total} unique sprite meshes.");
                _queuedSpriteIds.Clear();
                _queuedBuildings.Clear();
                _patchRunning = false;
            }

            IEnumerator EnqueueTileSpritesBatched(Action<int> onComplete)
            {
                int count = 0;
                if (World.world?.tiles_list != null && World.world.tilemap != null)
                {
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
                            if (tileVariation == null)
                            {
                                continue;
                            }

                            Sprite sprite = tileVariation.sprite;
                            if (sprite == null)
                            {
                                continue;
                            }

                            if (EnqueueSprite(sprite))
                            {
                                count++;
                            }
                        }
                        catch (System.Exception)
                        {
                            continue;
                        }

                        if ((i & 31) == 31)
                        {
                            yield return null;
                        }
                    }
                }

                InitProfiler.Measure("VoxelPrewarm: TileSprites", () => { });
                onComplete?.Invoke(count);
            }

            IEnumerator EnqueueActorSpritesBatched(Action<int> onComplete)
            {
                int index = 0;
                int count = 0;
                foreach (Actor actor in World.world.units)
                {
                    if (actor == null)
                    {
                        continue;
                    }

                    Sprite sprite;
                    try
                    {
                        sprite = actor.calculateMainSprite();
                    }
                    catch (System.Exception)
                    {
                        continue;
                    }

                    if (sprite != null && EnqueueSprite(sprite))
                    {
                        count++;
                    }

                    if ((index++ & 31) == 31)
                    {
                        yield return null;
                    }
                }

                InitProfiler.Measure("VoxelPrewarm: ActorSprites", () => { });
                onComplete?.Invoke(count);
            }

            IEnumerator EnqueueBuildingSpritesBatched(Action<int> onComplete)
            {
                if (World.world.buildings == null)
                {
                    onComplete?.Invoke(0);
                    yield break;
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
                    if (value == null)
                    {
                        continue;
                    }

                    if (value is Building[] array)
                    {
                        for (int j = 0; j < array.Length; j++)
                        {
                            count += TryEnqueueBuildingSprite(array[j]);
                            if ((j & 31) == 31)
                            {
                                yield return null;
                            }
                        }
                        continue;
                    }

                    if (!(value is IEnumerable enumerable))
                    {
                        continue;
                    }

                    // Snapshot before yielding — building managers may mutate collections mid-enumeration.
                    var elements = new System.Collections.Generic.List<object>();
                    foreach (object element in enumerable)
                    {
                        elements.Add(element);
                    }

                    for (int inner = 0; inner < elements.Count; inner++)
                    {
                        if (elements[inner] is Building building)
                        {
                            count += TryEnqueueBuildingSprite(building);
                        }

                        if ((inner & 31) == 31)
                        {
                            yield return null;
                        }
                    }
                }

                InitProfiler.Measure("VoxelPrewarm: BuildingSprites", () => { });
                onComplete?.Invoke(count);
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
