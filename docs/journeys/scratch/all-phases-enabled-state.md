# All Phases Enabled — Bridge Verified State

Achieved via:
```
pwsh Tools/wsm3d.ps1 kill && pwsh Tools/wsm3d.ps1 launch
# then via bridge POST (NOT JSON edit — PlayerConfig.dict shadows it on launch):
for f in VoxelEntities ProceduralBuildings CrossedQuadFoliage MeshWater HighShadows WorldspaceUI DayNightCycle PostFX ParticleEffects; do
  curl -X POST -H "Content-Length: 0" "http://127.0.0.1:8766/settings/$f?value=true"
done
```

## Verified bridge state

| Phase | enabled | patchedTypes |
|---|---|---|
| VoxelEntities | ✅ true | 2 |
| ProceduralBuildings | ✅ true | 1 |
| CrossedQuadFoliage | ✅ true | 2 |
| MeshWater | ✅ true | 5 |
| HighShadows | ✅ true | 1 |
| SkeletalAnimation | 🚫 false (dragonfly bug) | 1 |
| WorldspaceUI | ✅ true | 1 |
| DayNightCycle | ✅ true | 1 |
| PostFX | ✅ true | 1 |
| ParticleEffects | ✅ true | 3 |

9/10 phases active and inventoried via PhasePatchManager. SkeletalAnimation
left off pending bone-weight bind-pose audit.

## Known launch-time issue

Settings written to JSON file do NOT survive a kill+launch cycle — NML's
PlayerConfig.dict has its own boolVal persistence that wins over our
JSON. POST /settings/<key> via bridge bypasses this by using reflection
to write SavedSettings directly + calling ApplyPhaseToggle.

Long-term: WorldSphereTab's toggle registration should write SavedSettings
on Init (currently reads from PlayerConfig at startup). For now, all
phase activation flows through the bridge.

