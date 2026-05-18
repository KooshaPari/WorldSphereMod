using System.Collections.Generic;
using HarmonyLib;

namespace WorldSphereMod.Worldspace
{
    /// <summary>
    /// Phase 7 Step 4. Drives <see cref="SelectionRing.Show"/>/<see cref="SelectionRing.Hide"/>
    /// off the vanilla selection API.
    ///
    /// WorldBox tracks the player selection through the global static
    /// <c>SelectedUnit</c> class (not a "SelectionManager" — that doesn't exist).
    /// All mutations go through <c>select</c> / <c>unselect</c> / <c>removeSelected</c> /
    /// <c>clear</c>; <c>selectMultiple</c> calls <c>select(..., pSetMainUnit:false)</c>
    /// in a loop so it's covered by the <c>select</c> postfix transitively.
    ///
    /// <c>clear()</c> empties <c>_units_hashset</c> in-place so the actor identities
    /// are gone by the time a Postfix runs; we snapshot in a Prefix and hide them
    /// in the Postfix.
    /// </summary>
    [HarmonyPatch(typeof(SelectedUnit))]
    public static class SelectionHooks
    {
        [HarmonyPatch(nameof(SelectedUnit.select))]
        [HarmonyPostfix]
        public static void OnSelect(Actor pActor)
        {
            if (!Core.IsWorld3D || !Core.savedSettings.WorldspaceUI) return;
            if (pActor == null) return;
            SelectionRing.Show(pActor);
        }

        [HarmonyPatch(nameof(SelectedUnit.unselect))]
        [HarmonyPostfix]
        public static void OnUnselect(Actor pActor)
        {
            if (pActor == null) return;
            SelectionRing.Hide(pActor);
        }

        [HarmonyPatch(nameof(SelectedUnit.removeSelected))]
        [HarmonyPostfix]
        public static void OnRemoveSelected(Actor pActor)
        {
            if (pActor == null) return;
            SelectionRing.Hide(pActor);
        }

        [HarmonyPatch(nameof(SelectedUnit.clear))]
        [HarmonyPrefix]
        public static void OnClearPrefix(out List<Actor>? __state)
        {
            __state = null;
            var set = SelectedUnit._units_hashset;
            if (set == null || set.Count == 0) return;
            __state = new List<Actor>(set);
        }

        [HarmonyPatch(nameof(SelectedUnit.clear))]
        [HarmonyPostfix]
        public static void OnClearPostfix(List<Actor>? __state)
        {
            if (__state == null) return;
            for (int i = 0; i < __state.Count; i++)
            {
                Actor a = __state[i];
                if (a != null) SelectionRing.Hide(a);
            }
        }
    }
}
