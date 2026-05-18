using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace WorldSphereMod.Worldspace
{
    /// <summary>
    /// Phase 7 Step 1 skeleton. Owns one follow-rig <see cref="Transform"/> per
    /// visible <see cref="Actor"/> under a single root child of <see cref="Mod.Object"/>,
    /// repositioned in <see cref="LateUpdate"/>. Nameplate/health-bar/selection/popup
    /// children attach to these rigs in Steps 2-6; this step only owns the rig graph.
    ///
    /// Gated behind <see cref="SavedSettings.WorldspaceUI"/>. World-end teardown is a
    /// follow-up — Step 2 will Postfix <c>MapBox.addClearWorld</c> to call
    /// <see cref="OnWorldUnload"/>; until then static state persists across reloads.
    /// </summary>
    public sealed class WorldUIRenderer : MonoBehaviour
    {
        public static WorldUIRenderer? Instance { get; private set; }

        /// <summary>Mid-body lift above tile surface for the rig anchor (world units).</summary>
        public const float kRigLift = 0.5f;

        readonly Dictionary<Actor, Transform> _rigs = new Dictionary<Actor, Transform>();
        readonly HashSet<Actor> _seenThisFrame = new HashSet<Actor>();
        readonly List<Actor> _scratchRemove = new List<Actor>();
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
            if (Instance._root != null) Object.Destroy(Instance._root.gameObject);
            Instance._root = null;
            GameObject go = Instance.gameObject;
            // Null Instance first so the OnDestroy guard does not double-clear another
            // freshly-created singleton (paranoia — EnsureCreated short-circuits on
            // Instance != null, but world-unload + immediate re-entry must be safe).
            var dying = Instance;
            Instance = null;
            // Only destroy the component, not the Mod.Object GameObject it lives on.
            Object.Destroy(dying);
            // Suppress unused warning if go is otherwise unreferenced in future edits.
            _ = go;
        }

        void Awake()
        {
            Instance = this;
            GameObject rootGo = new GameObject("WSM3D.UIRigs");
            _root = rootGo.transform;
            _root.SetParent(transform, worldPositionStays: false);
            DamagePopup.Init(_root);
        }

        void OnDestroy()
        {
            DamagePopup.Clear();
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            if (!Core.IsWorld3D || !Core.savedSettings.WorldspaceUI) return;
            if (World.world == null || World.world.units == null) return;

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

                // Tools.To3DTileHeight(pos, extra) := To3D(pos, GetTileHeightSmooth(pos) + extra),
                // so passing kRigLift directly matches the docs' formula
                // `To3D(pos, GetTileHeightSmooth(tile) + kRigLift)` without double-adding the
                // tile height. (`Actor.current_tile` is a WorldTile here, not a Vector2 —
                // current_position is the canonical world-position input.)
                rig.position = Tools.To3DTileHeight(a.current_position, kRigLift);
            }

            // Reap rigs whose actor is no longer visible (or has been destroyed).
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

        internal Transform RegisterActor(Actor a)
        {
            // Actor is a plain class (not a UnityEngine.Object), so use the runtime hash
            // for the debug name. Uniqueness is best-effort — names are not load-bearing.
            GameObject go = new GameObject("rig:" + RuntimeHelpers.GetHashCode(a));
            Transform rig = go.transform;
            if (_root != null) rig.SetParent(_root, worldPositionStays: false);
            _rigs[a] = rig;
            NameplateWorld.Attach(a, rig);
            return rig;
        }

        internal void UnregisterActor(Actor a)
        {
            if (!_rigs.TryGetValue(a, out Transform rig)) return;
            NameplateWorld.Detach(a);
            if (rig != null) Object.Destroy(rig.gameObject);
            _rigs.Remove(a);
        }
    }
}
