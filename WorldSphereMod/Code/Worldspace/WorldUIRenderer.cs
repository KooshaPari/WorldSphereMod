using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using WorldSphereMod.LOD;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.Worldspace
{
    /// <summary>
    /// Phase 7 worldspace UI driver: per-actor rigs, nameplates, health bars, faction
    /// badges, and damage popups. Visibility follows <see cref="LodSelector.UiTier"/>.
    /// </summary>
    public sealed class WorldUIRenderer : MonoBehaviour
    {
        public static WorldUIRenderer? Instance { get; private set; }

        public const float kRigLift = 0.5f;

        readonly Dictionary<Actor, Transform> _rigs = new Dictionary<Actor, Transform>();
        readonly HashSet<Actor> _seenThisFrame = new HashSet<Actor>();
        readonly List<Actor> _scratchRemove = new List<Actor>();
        readonly Dictionary<Actor, float> _lastHpRatio = new Dictionary<Actor, float>();
        Transform? _root;

        public IReadOnlyDictionary<Actor, Transform> Rigs => _rigs;

        public static void EnsureCreated()
        {
            if (Instance != null) return;
            if (!Core.IsWorld3D || !Core.savedSettings.WorldspaceUI) return;
            if (Mod.Object == null) return;
            Mod.Object.AddComponent<WorldUIRenderer>();
        }

        public static void OnWorldUnload()
        {
            if (Instance == null) return;
            SelectionRing.Clear();
            DamagePopup.Clear();
            foreach (var kv in Instance._rigs)
            {
                if (kv.Value != null) Object.Destroy(kv.Value.gameObject);
            }
            Instance._rigs.Clear();
            Instance._seenThisFrame.Clear();
            Instance._lastHpRatio.Clear();
            HealthBar.Reset();
            NameplateWorld.Reset();
            FactionBadge.Reset();
            if (Instance._root != null) Object.Destroy(Instance._root.gameObject);
            Instance._root = null;
            var dying = Instance;
            Instance = null;
            Object.Destroy(dying);
        }

        void Awake()
        {
            Instance = this;
            GameObject rootGo = new GameObject("WSM3D.UIRigs");
            _root = rootGo.transform;
            _root.SetParent(transform, worldPositionStays: false);
            SyncDamagePopupSettings();
            DamagePopup.Init(_root);
            VoxelRender.OnActorDamaged += OnActorDamaged;
        }

        void OnDestroy()
        {
            VoxelRender.OnActorDamaged -= OnActorDamaged;
            DamagePopup.Clear();
            if (Instance == this) Instance = null;
        }

        static void SyncDamagePopupSettings()
        {
            if (Core.savedSettings == null) return;
            DamagePopup.PoolSize = Mathf.Max(8, Core.savedSettings.DamagePopPoolSize);
            DamagePopup.RiseSpeed = Core.savedSettings.DamagePopRiseHeight / Mathf.Max(0.1f, Core.savedSettings.DamagePopDuration);
            DamagePopup.Lifetime = Core.savedSettings.DamagePopDuration;
        }

        static void OnActorDamaged(Actor actor, int damageAmount)
        {
            if (Instance == null || actor == null || damageAmount <= 0) return;
            if (!Instance._rigs.TryGetValue(actor, out Transform rig) || rig == null) return;
            DamagePopup.Spawn(rig.position + Vector3.up * 0.5f, damageAmount, Color.yellow);
        }

        void LateUpdate()
        {
            if (!Core.IsWorld3D || !Core.savedSettings.WorldspaceUI) return;
            if (World.world == null || World.world.units == null) return;

            SyncDamagePopupSettings();
            FactionBadgeAtlasBuilder.MaybeRebuild();

            _seenThisFrame.Clear();

            var arr = World.world.units.visible_units.array;
            int n = World.world.units.visible_units.count;
            for (int i = 0; i < n; i++)
            {
                Actor a = arr[i];
                if (a == null) continue;
                _seenThisFrame.Add(a);

                if (!_rigs.TryGetValue(a, out Transform rig))
                {
                    rig = RegisterActor(a);
                }
                if (rig == null) continue;

                rig.position = Tools.To3DTileHeight(a.current_position, kRigLift);

                Vector3 cullPos = rig.position;
                LodSelector.Select(cullPos, a.GetHashCode());
                UiTier uiTier = LodSelector.GetUiTier(a.GetHashCode());
                ApplyUiTier(a, rig, uiTier);

                TrackDamagePopups(a, rig);
            }

            _scratchRemove.Clear();
            foreach (var kv in _rigs)
            {
                if (!_seenThisFrame.Contains(kv.Key)) _scratchRemove.Add(kv.Key);
            }
            for (int i = 0; i < _scratchRemove.Count; i++)
            {
                UnregisterActor(_scratchRemove[i]);
            }

            SelectionRing.UpdateAll();
            DamagePopup.Tick();
        }

        void TrackDamagePopups(Actor a, Transform rig)
        {
            float hp = GetHpRatio(a);
            if (_lastHpRatio.TryGetValue(a, out float prev) && hp < prev - 0.0001f)
            {
                int damage = Mathf.Max(1, Mathf.RoundToInt((prev - hp) * 100f));
                VoxelRender.NotifyActorDamaged(a, damage);
            }
            _lastHpRatio[a] = hp;
        }

        static float GetHpRatio(Actor a)
        {
            try
            {
                var m = a.GetType().GetMethod("getHealthRatio");
                if (m != null && m.Invoke(a, null) is float r) return Mathf.Clamp01(r);
            }
            catch { }
            return 1f;
        }

        static void ApplyUiTier(Actor a, Transform rig, UiTier tier)
        {
            var nameplate = rig.GetComponentInChildren<NameplateWorld>(true);
            var health = rig.GetComponentInChildren<HealthBar>(true);
            var badge = rig.GetComponentInChildren<FactionBadge>(true);

            bool showName = tier == UiTier.Full;
            bool showHealth = tier == UiTier.Full || tier == UiTier.HealthOnly;
            bool showBadge = tier == UiTier.Full;

            if (nameplate != null) nameplate.SetUiVisible(showName);
            if (health != null) health.SetUiVisible(showHealth);
            if (badge != null) badge.SetVisible(showBadge);
        }

        internal Transform RegisterActor(Actor a)
        {
            GameObject go = new GameObject("rig:" + RuntimeHelpers.GetHashCode(a));
            Transform rig = go.transform;
            if (_root != null) rig.SetParent(_root, worldPositionStays: false);
            _rigs[a] = rig;
            NameplateWorld.Attach(a, rig);
            HealthBar.Attach(a, rig);
            FactionBadge.Attach(a, rig);
            _lastHpRatio[a] = GetHpRatio(a);
            return rig;
        }

        internal void UnregisterActor(Actor a)
        {
            if (!_rigs.TryGetValue(a, out Transform rig)) return;
            NameplateWorld.Detach(a);
            HealthBar.Detach(a);
            FactionBadge.Detach(a);
            LodSelector.Remove(a.GetHashCode());
            _lastHpRatio.Remove(a);
            if (rig != null) Object.Destroy(rig.gameObject);
            _rigs.Remove(a);
        }
    }
}
