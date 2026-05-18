# Journey: Extend via the API

**Persona:** A mod author writing a second NeoModLoader mod that needs to
plug into WorldSphereMod3D — supplying a hand-made mesh for one of their
units, registering a custom rig, or reacting to time-of-day.
**Time:** ~30 minutes.
**Prerequisites:** A working WorldBox + NeoModLoader environment, a mod
project of your own, and `WorldSphereMod3D` installed.

## Goal

Link against `WorldSphereAPI.dll`, detect the fork at runtime, and call
one of the v2 API methods to inject custom content into a phase the fork
controls.

## Steps

1. **Reference `WorldSphereAPI.dll`** in your mod's `.csproj`. The DLL is
   the public, Unity-free surface — same one shipped by the upstream mod,
   with backwards-compatible v2 additions:

   ```xml
   <ItemGroup>
     <Reference Include="WorldSphereAPI">
       <HintPath>$(WorldBoxPath)/Mods/WorldSphereMod3D/Assemblies/WorldSphereAPI.dll</HintPath>
     </Reference>
   </ItemGroup>
   ```

   ![External mod .csproj open in editor with the WorldSphereAPI Reference and $(WorldBoxPath) HintPath highlighted](./assets/extend-via-api/01-api-link.png)

2. **Connect at runtime.** The API is delegate-based and reflection-loaded
   so your mod doesn't hard-link against the host:

   ```csharp
   if (WorldSphereAPI.Connect(out var api))
   {
       bool fork = api.IsModel3D;     // true on this fork, false on upstream
       bool world3D = api.IsWorld3D;  // true on both
   }
   ```

   `IsModel3D` is the fork-specific bit. Use it to gate v2 calls so your
   mod works against both upstream and the fork.

   ![C# source snippet showing WorldSphereAPI.Connect with IsModel3D and IsWorld3D reads, IDE-highlighted](./assets/extend-via-api/02-connect-call.png)

3. **Pick what to extend.**

   | API call | Purpose | Bypasses |
   |---|---|---|
   | `RegisterCustomMesh(assetId, mesh, albedo)` | Supply a hand-made `Mesh` for an actor / building asset | [Phase 1 voxelization](/phase1-review) for that asset |
   | `RegisterBuildingRules(assetId, rules)` | Override procgen heuristics (story count, roof shape, door positions) | [Phase 2 procgen](/phase2-architecture) for that asset |
   | `RegisterRig(assetId, rigData)` | Assign a custom skeleton + bone weights | Auto-rig in [phase 6](/phase6-architecture) |
   | `RegisterEffectMesh(effectId, mesh)` | Replace voxelized effect with author mesh | [Phase 9](/phase9-architecture) voxel-mesh particle bursts |
   | `event Action<float> OnTimeOfDayChanged` | Hook day/night cycle | n/a — observe-only |
   | `MakeActorNonUpright(assetId)` / etc. | v1 surface, preserved | Camera-facing rotation |

4. **Register in your mod's `OnLoad`** (after WorldSphereMod3D has set up
   its caches but before the first frame is drawn).

   ```csharp
   public void OnLoad()
   {
       if (!WorldSphereAPI.Connect(out var api)) return;
       if (!api.IsModel3D) return;        // safely no-op on upstream

       api.RegisterCustomMesh(
           assetId: "my_unicorn",
           mesh:    LoadMeshFromBundle("unicorn.glb"),
           albedo:  LoadTextureFromBundle("unicorn_albedo.png"));

       api.OnTimeOfDayChanged += t =>
       {
           // 0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset
           MyAmbientMusic.SetIntensity(t);
       };
   }
   ```

5. **Verify.** Spawn an instance of the asset in-game. With Phase 1
   voxelization enabled and your `RegisterCustomMesh` call winning, you
   should see *your* mesh, not the voxel mesh that would otherwise be
   generated from the sprite.

   ![In-game GIF: a custom-mesh actor (registered via RegisterCustomMesh) rendered with the author-supplied mesh, camera orbiting 360 degrees](./assets/extend-via-api/03-registered-mesh.gif)

## Outcome

Your mod cleanly extends WorldSphereMod3D without patching it, and remains
compatible with vanilla WorldBox + upstream `WorldSphereMod` (the v2 calls
no-op).

## Variants

- **Hot-reload**: WorldSphereMod3D does not currently hot-reload the API
  surface. Restart WorldBox after re-registering.
- **Custom shaders**: not part of the v2 surface yet; the URP shaders ship
  inside `WorldSphereMod3D`'s AssetBundles. Open a feature request or PR
  if you need this.
- **Settings reads**: `GetSetting<T>(string key)` is the v1 reflection-based
  reader; it picks up new v2 settings automatically. No code change in your
  mod required when WorldSphereMod3D adds a new flag.

## Pitfalls

- *`api.IsModel3D` is false* but you expected the fork → either upstream is
  installed instead, or `Is3D` master switch is off in user settings. Don't
  proceed with v2 calls.
- *Mesh appears at origin, not on the unit* → the mesh's pivot must match
  what voxelization assumes (centered horizontally, bottom-aligned
  vertically). Mismatch = wrong attach point.
- *Custom rig ignored* → the rig must be registered **before** the first
  actor of that `assetId` is spawned. Register in `OnLoad`, not on first
  use.
