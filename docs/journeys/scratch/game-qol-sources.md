# WSM3D Game QoL Sources (Input Remap, Audio, UI/UX, Saves, Perf HUD)

Top 3 references to start implementation work on player-facing QoL systems:

1. **Unity runtime feature set (core engine building blocks)**
   - Unity package/docs stack that directly maps to all requested areas: `Input System`, `UI Toolkit`, `Audio`, `Serialization`, and `Profiler`.
   - Why it helps:
     - **Input remap:** action maps + rebinding APIs (`InputActionRebindingExtensions`) and runtime action maps.
     - **Audio:** volume/control via `AudioMixer`, DSP/snapshot control, and mixer group routing.
     - **UI/UX:** immediate-mode vs retained UI via `UI Toolkit`, `IMGUI`, and `UIToolkit runtime` docs for in-game menus and HUD overlays.
     - **Saves:** serialization patterns for mod data (binary/json), `Application.persistentDataPath`, and managed file IO.
     - **Perf HUD:** profiling hooks, frame timing, GC allocation visibility, and custom overlay/diagnostic UI.
   - URL: https://docs.unity3d.com/Manual/

2. **BepInEx + Configuration Manager (mod-runtime configuration and user-facing options)**
   - Most practical ecosystem for exposing configurable QoL settings (including keybinds) and runtime toggles without rebuilding the whole mod pipeline.
   - Why it helps:
     - Mature pattern for declarative settings definitions + config persistence.
     - Commonly used for input binding/key rebinding workflows and debug/perf toggles.
     - Useful bridge for hot-loadable experimentation while the game is running.
   - URL: https://github.com/BepInEx/BepInEx

3. **Harmony (safe runtime patching for hooking WorldBox/WSM3D behavior)**
   - For QoL work that requires intercepting existing game methods (e.g., save prompts, menu flow, audio event hooks, HUD draw calls), Harmony remains the standard patching baseline.
   - Why it helps:
     - Supports prefix/postfix/transpiler hooks with minimal invasive changes.
     - Works well when combined with a Unity mod loader and keeps changes isolated to deltas rather than forks.
     - Enables targeted QoL injections: custom input pre-check, save versioning guards, perf marker wrappers, and lightweight HUD augmentations.
   - URL: https://github.com/pardeike/Harmony

Recommended starter order:

1. Use Unity docs first to align feature implementation with engine-supported patterns.
2. Use BepInEx config/settings model for user-tweakable controls and debug options.
3. Use Harmony for behavioral hooks only where necessary, then gate each feature behind SavedSettings flags for in-game validation.
