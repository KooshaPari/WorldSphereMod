# WorldUIRenderer audit

Scope: `WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs`

1. **Cull-lift bug: no.** `LateUpdate()` iterates `World.world.units.visible_units` and positions each rig with `Tools.To3DTileHeight(a.current_position, kRigLift)` directly. There is no second read of a raw, unlifted position after culling, so the Phase 3-style “cull position lifted, render position still raw” bug pattern does not appear here. See `WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs:80-106`.

2. **Lifecycle / despawn leaks: mostly handled.** Each frame, actors missing from `visible_units` are removed from `_rigs`, their nameplate/health-bar children are detached, and the rig `GameObject` is destroyed in `UnregisterActor()`. World unload also clears selection rings, damage popups, rigs, the root object, and health-bar shared assets before destroying the component. I do not see an actor-despawn leak in the current flow. See `WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs:109-145` and `WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs:39-63`.

3. **Thread-safety / parallel postfix: no.** This class is a `MonoBehaviour` driven by Unity `LateUpdate()`, not a Harmony postfix. The only creation path is `Mod.Init() -> WorldUIRenderer.EnsureCreated()`, and the work in this file runs on the main thread. See `WorldSphereMod/Code/Mod.cs:65-70` and `WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs:31-37,80-122`.

4. **Off-screen culling: no explicit camera cull.** The renderer does not test camera frustum or screen visibility for UI elements. It updates every rig it has attached, plus `SelectionRing.UpdateAll()` and `DamagePopup.Tick()`, once per frame. The only pruning is at the actor list level via `visible_units`, not per UI element. See `WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs:87-121`, `WorldSphereMod/Code/Worldspace/SelectionRing.cs:53-76`, and `WorldSphereMod/Code/Worldspace/DamagePopup.cs:82-118`.
