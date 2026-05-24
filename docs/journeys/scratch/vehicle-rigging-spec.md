# Vehicle Rigging Spec

Scope: `WorldSphereMod/Code/QuantumSprites.cs`, `WorldSphereMod/Code/Voxel/VoxelRender.cs`, `WorldSphereMod/Code/WorldSphereAPI.cs`, and a new `WorldSphereMod/Code/Rig/VehicleRig.cs`.

## 1) Source mapping

I did not find a vehicle-specific registry in this checkout. The visible render pipeline is split by entity class:

- actors are driven from `ActorManager.visible_units` in `VoxelRender.ActorVoxelEmit`
- buildings are driven from `BuildingManager._array_visible_buildings` in `VoxelRender.BuildingVoxelEmit`
- projectiles have their own path in `QuantumSprites.SetProjectile`

`WorldSphereModAPI` only exposes `MakeActorPerp`, `MakeBuildingPerp`, and `MakeProjectilePerp`; there is no vehicle API today. That means the clean source-side mapping is:

- cars, tanks, and rider-carrying vehicles: actor assets
- missiles / RPG warheads: projectile assets
- static wrecks or map props: building assets if they never carry riders or animate wheels

So the “visible vehicle list” should not be a new top-level scene collection. It should be an actor-side filter over `ActorManager.visible_units`, keyed by a vehicle rig registry.

## 2) VehicleRig data model

Add a registry-backed rig type:

```csharp
public enum VehicleKind { Car, Tank, Missile, Flying, Tracked }

public sealed class VehicleRig
{
    public string AssetId;
    public VehicleKind Kind;
    public TransformSpec Body;
    public TransformSpec DriverSeat;
    public TransformSpec[] PassengerSeats;
    public WheelSpec[] Wheels;
    public TrackSpec[] Tracks;
    public PropellerSpec[] Propellers;
    public float GroundClearance;
    public float WheelRadius;
    public float MaxBodyPitchDeg;
}
```

`TransformSpec` is local-space offset + rotation + scale relative to the vehicle root. `WheelSpec` stores axle side, spin axis, and wheel radius. `TrackSpec` stores belt segment count and UV scroll scale. `PropellerSpec` stores blade count and spin axis.

## 3) Driving animation pipeline

At render-data time:

1. Resolve `VehicleRig` by `actor.asset.id`.
2. Sample terrain under the vehicle footprint and build a ground plane from the local slope.
3. Align the body to that plane, but clamp pitch/roll to `MaxBodyPitchDeg` so steep terrain does not flip the vehicle.
4. Compute linear velocity from the actor movement delta, project it onto the forward axis, and drive wheel spin by `distanceTravelled / WheelRadius`.
5. For tracks, use the same forward distance to scroll belt UVs or advance segment transforms.
6. For flying vehicles, drive propellers from throttle or speed; if throttle is unavailable, fall back to forward speed.
7. If a rider is present, attach the actor to `DriverSeat` or the first free `PassengerSeat`.

The animation state should live in a per-entity cache keyed by actor id so wheel phase, track phase, and propeller angle persist across frames.

## 4) Render integration

Add a new rig branch before the current voxel/impostor billboard submission in `VoxelRender.ActorVoxelEmit`:

- `tier == Impostor`: keep the current billboard fallback, no rig work
- `VehicleRig == null`: keep current actor behavior
- `VehicleRig != null`: submit a vehicle body mesh plus animated subparts

This preserves existing actor rendering for non-vehicles and keeps vehicles on the actor path, which is where moving/ridden entities already live.

## 5) API shape

Add one registration method:

```csharp
WorldSphereModAPI.RegisterVehicleRig(string assetId, object rigObj)
```

Downstream mods can register cars, tanks, and aircraft without hardcoding IDs in the renderer. If a vehicle is also marked `PerpActors`, that should only control camera-facing fallback, not the rig pipeline.

