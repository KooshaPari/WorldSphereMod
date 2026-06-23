using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.Effects;
using WorldSphereMod.Rig;

namespace WorldSphereMod
{
    public static class Constants
    {
        public const int ZDisplacement = 100;


        //square root of 1/2
        public const float HalfRoot = 0.70710678118f;
        //idk
        public const float TileHeightDiffSpeed = 4f;

        public static readonly Quaternion ConstRot = Quaternion.Euler(0, 90, 180);
        public static readonly Quaternion ToUpright = Quaternion.Euler(90, 0, 0);
        public static readonly Quaternion FromUpright = Quaternion.Euler(-90, 0, 0);
        public static readonly ConcurrentDictionary<string, EffectData> EffectDatas = new ConcurrentDictionary<string, EffectData>()
        {
            {"fx_meteorite", new EffectData(false) },
            {"fx_fire_smoke", new EffectData(false) },
            {"fx_antimatter_effect", new EffectData(false) },
            {"fx_napalm_flash", new EffectData(false) },
            {"fx_boulder", new EffectData(true) },
            {"fx_explosion_wave", new EffectData(false) },
            {"fx_tile_effect", new EffectData(false) },
            {"fx_cloud", new EffectData(false, true, 21, false, emitCrossedQuad: true) }
        };
        public static readonly ConcurrentDictionary<string, bool> PerpActors = new ConcurrentDictionary<string, bool>();
        public static readonly ConcurrentDictionary<string, bool> PerpBuildings = new ConcurrentDictionary<string, bool>();
        public static readonly ConcurrentDictionary<string, bool> PerpProjectiles = new ConcurrentDictionary<string, bool>();
        public static readonly Dictionary<string, RigType> ActorRigTypes = CreateActorRigTypes();

        public static void RegisterActorRig(string assetId, RigType rig)
        {
            if (string.IsNullOrEmpty(assetId))
            {
                return;
            }

            ActorRigTypes[assetId] = rig;
        }

        static readonly string[] _humanoidPrefixes = { "human", "elf", "orc", "dwarf", "goblin", "skeleton", "zombie", "bandit", "mage", "warrior", "king", "demon", "angel", "druid", "necromancer" };
        static readonly string[] _quadrupedPrefixes = { "wolf", "bear", "horse", "cow", "sheep", "pig", "dog", "cat", "fox", "deer", "rabbit", "lion", "turtle", "rhino", "mammoth", "frog", "rat", "fire_elemental_horse" };
        static readonly string[] _birdPrefixes = { "bird", "eagle", "seagull", "pigeon", "bat", "crow", "owl", "chicken" };
        static readonly string[] _insectPrefixes = { "butterfly", "bee", "fly", "firefly" };
        static readonly string[] _snakePrefixes = { "snake", "worm" };

        static Dictionary<string, RigType> CreateActorRigTypes()
        {
            var rigTypes = new Dictionary<string, RigType>();
            AddRigGroup(rigTypes, RigType.Humanoid, "human", "villager", "swordsman", "archer", "mage", "orc", "elf", "dwarf", "goblin", "skeleton", "zombie", "bandit", "necromancer", "druid", "king", "warrior", "plague_doctor", "demon", "angel", "whiteMage", "evilMage");
            AddRigGroup(rigTypes, RigType.Quadruped, "wolf", "bear", "horse", "cow", "sheep", "pig", "dog", "cat", "fox", "deer", "rabbit", "lion", "turtle", "rhino", "mammoth", "frog", "rat");
            AddRigGroup(rigTypes, RigType.Bird, "bird", "eagle", "seagull", "pigeon", "bat", "crow", "owl", "chicken");
            AddRigGroup(rigTypes, RigType.Insect, "butterfly", "bee", "fly", "firefly");
            AddRigGroup(rigTypes, RigType.Snake, "snake");
            AddRigGroup(rigTypes, RigType.None, "sand_spider", "dragon", "crabzilla", "tumor", "ufo");
            return rigTypes;
        }

        static void AddRigGroup(Dictionary<string, RigType> rigTypes, RigType rigType, params string[] assetIds)
        {
            foreach (string assetId in assetIds)
            {
                rigTypes[assetId] = rigType;
            }
        }

        public static RigType ResolveActorRig(string assetId)
        {
            if (string.IsNullOrEmpty(assetId))
            {
                return RigType.Humanoid;
            }

            if (ActorRigTypes.TryGetValue(assetId, out RigType rig))
            {
                return rig;
            }

            if (Rig.VehicleShapeHints.IsVehicleAssetId(assetId))
            {
                return RigType.None;
            }

            string lower = assetId.ToLowerInvariant();
            if (MatchesAnyPrefix(lower, _humanoidPrefixes)) return RigType.Humanoid;
            if (MatchesAnyPrefix(lower, _quadrupedPrefixes)) return RigType.Quadruped;
            if (MatchesAnyPrefix(lower, _birdPrefixes)) return RigType.Bird;
            if (MatchesAnyPrefix(lower, _insectPrefixes)) return RigType.Insect;
            if (MatchesAnyPrefix(lower, _snakePrefixes)) return RigType.Snake;

            return RigType.Humanoid;
        }

        static bool MatchesAnyPrefix(string lower, string[] prefixes)
        {
            foreach (string prefix in prefixes)
            {
                // WHY: real prefix (StartsWith) or a "_"-delimited whole token, never a
                // bare Contains — that matched "bat"/"bee" inside unrelated IDs and
                // mis-rigged humans as birds/insects.
                if (lower.StartsWith(prefix) ||
                    lower.EndsWith("_" + prefix) ||
                    lower.Contains("_" + prefix + "_"))
                {
                    return true;
                }
            }
            return false;
        }
        public const int SpecialHeight = 4;
        public static float YConst => 1f / (81 / (Core.Sphere.HeightMult));
        public static Vector3 HighlightedZoneSize => new Vector3(1, 1 + (10 * YConst), 1);
        public static Vector3 Zero = Vector3.zero;
        public static readonly Quaternion Right = Quaternion.Euler(0, 90, 0);
    }
}
