# WSM3D Voxel Disk Cache — Spec

## Problem

Every game launch re-voxelizes all ~417 sprites through the async
`VoxelMeshCache` pipeline. Each sprite hits `SpriteVoxelizer.Build*`,
greedy-meshes the result, and uploads to a Unity `Mesh`. The total
wall-clock cost on first load is significant — placeholder cubes are
visible for seconds while the background queue drains via
`PumpQueuedBuilds` / `DrainCompletedBuilds`.

The meshes are deterministic: identical sprite texture data + depth +
inflation style = identical vertex/index/color buffers. There is no
reason to recompute them across launches.

## Goal

Persist voxelized mesh data to a local SQLite database so that the second
launch (and every launch thereafter) gets ~417 instant cache hits with
zero voxelization work. Sprites whose texture has changed between
launches (mod updates, atlas swaps) are detected and re-voxelized.

## Database location

```
<mods_config>/wsm3d-voxel-cache.db
```

Resolved at runtime via `NeoModLoader.General.Paths.ModsConfigPath` (the
same root that holds `WorldSphereMod.json`). The file is gitignored and
user-local. Deleting it is safe — the mod falls back to the existing
async-build path and rebuilds the cache transparently.

## SQLite schema

Single table, one row per voxelized sprite:

```sql
CREATE TABLE IF NOT EXISTS voxel_cache (
    sprite_name   TEXT    NOT NULL PRIMARY KEY,
    vertex_data   BLOB    NOT NULL,   -- IEEE 754 float32 triples (x,y,z)
    index_data    BLOB    NOT NULL,   -- int32 triangle indices
    color_data    BLOB    NOT NULL,   -- RGBA8 per-vertex Color32
    normal_data   BLOB,               -- float32 triples; NULL = recalculate
    vertex_count  INTEGER NOT NULL,
    index_count   INTEGER NOT NULL,
    depth         INTEGER NOT NULL,   -- voxel extrusion depth used
    style         TEXT    NOT NULL,   -- inflation style ("greedy_pertexel", "lathe", "balloon", etc.)
    sprite_hash   TEXT    NOT NULL,   -- hex SHA-256 of sprite texture rect pixels
    created_at    TEXT    NOT NULL DEFAULT (datetime('now'))
);
```

### Why `sprite_name` as PK (not instance ID)

`Sprite.GetInstanceID()` changes every process launch — it is a Unity
session-local handle. `sprite.name` is stable across launches and is
already indexed in `VoxelMeshCache._nameToSpriteId`. Collisions between
sprites that share a name but differ in texture data are handled by the
hash column: a name match with a hash mismatch triggers re-voxelization.

### BLOB encoding

| Column | Encoding | Size per vertex |
|---|---|---|
| `vertex_data` | Little-endian `float[3]` packed contiguously | 12 bytes |
| `index_data` | Little-endian `int[1]` packed contiguously | 4 bytes per index |
| `color_data` | `byte[4]` RGBA per vertex | 4 bytes |
| `normal_data` | Little-endian `float[3]` packed contiguously | 12 bytes |

Typical sprite (~11x16 texels, greedy-meshed): ~200 verts, ~400 tris.
Row payload: ~200*12 + 1200*4 + 200*4 + 200*12 = ~10.4 KB.
Full database for 417 sprites: ~4.3 MB. Well within acceptable limits.

## Sprite hash computation

```
SHA-256( sprite_rect_pixels_as_RGBA8 )
```

Pixels are read from the atlas sub-rect via `SpriteVoxelizer.GetPixelsCached`
using `sprite.textureRect`. The hash covers exactly the texels that feed
into voxelization — not the full atlas texture. This ensures:

- Atlas repacking (texel positions change but pixel content doesn't) does
  NOT invalidate the cache.
- Texture edits (new palette, added detail) DO invalidate the cache.
- Two sprites with different names but identical pixel content get
  separate rows (keyed by name) but could theoretically share BLOBs in a
  future normalization pass.

The hash is stored as lowercase hex (64 chars). `System.Security.Cryptography.SHA256`
is available in net48.

## Cache invalidation

On load, for each row fetched from SQLite:

1. Resolve the live `Sprite` by name.
2. Compute `sprite_hash` from the live texture rect pixels.
3. Compare against the stored `sprite_hash`.
4. **Match** -- deserialize BLOBs into a `Mesh`, insert into
   `VoxelMeshCache._cache`. No voxelization needed.
5. **Mismatch** -- delete the stale row, enqueue the sprite for normal
   async voxelization. The new result will be written back on completion.

Additionally, rows whose `style` doesn't match the current
`SavedSettings.VoxelInflationStyle` (after resolution) are treated as
stale. This handles the case where a user changes their inflation style
between sessions.

## Load path

Injected into `VoxelMeshCache` initialization (called once from
`VoxelFrameDriver` or `Core.PostInit`):

```
VoxelDiskCache.WarmFromDisk()
  |
  +-- Open SQLite connection (read-only)
  +-- SELECT * FROM voxel_cache
  +-- For each row:
  |     Resolve Sprite by name from loaded atlas
  |     Compute live sprite_hash
  |     If hash matches AND style matches:
  |       Deserialize vertex_data, index_data, color_data, normal_data
  |       Create Unity Mesh on main thread
  |       Insert into VoxelMeshCache._cache with sprite instance ID key
  |       Increment warm-hit counter
  |     Else:
  |       Mark row for deletion (batch DELETE after loop)
  |       Sprite falls through to normal async build path
  +-- Close connection
  +-- Log: "[WSM3D] Disk cache: {warm_hits} warm hits, {stale} stale, {missing} not cached"
```

### Threading constraint

`new Mesh()` and `mesh.vertices = ...` must run on Unity's main thread.
The SQLite read and BLOB deserialization into raw arrays can happen on a
background thread, but mesh object creation must be marshalled back.
Recommendation: read all rows into a `List<RawMeshData>` on a ThreadPool
worker, then iterate and create `Mesh` objects in a coroutine or in
`PumpQueuedBuilds`-style batched main-thread frames (e.g., 50 meshes per
frame to avoid a frame spike).

## Save path

After `DrainCompletedBuilds` applies a `BuildCompletion` to the cache:

```
VoxelDiskCache.EnqueueSave(sprite_name, mesh, depth, style, sprite_hash)
  |
  +-- Serialize mesh.vertices -> vertex_data BLOB
  +-- Serialize mesh.GetTriangles(0) -> index_data BLOB
  +-- Serialize mesh.colors32 -> color_data BLOB
  +-- Serialize mesh.normals -> normal_data BLOB (nullable)
  +-- Enqueue row to a ConcurrentQueue<PendingWrite>
```

A background flush task drains the queue periodically (e.g., every 60
frames or on `OnApplicationQuit`):

```
VoxelDiskCache.FlushPendingWrites()
  |
  +-- Open SQLite connection (read-write)
  +-- BEGIN TRANSACTION
  +-- For each pending write:
  |     INSERT OR REPLACE INTO voxel_cache (...)
  +-- COMMIT
  +-- Close connection
```

Batching writes into a single transaction avoids per-row fsync overhead.
SQLite WAL mode is recommended for concurrent read/write access.

## Integration with existing VoxelMeshCache

The disk cache is a layer below the in-memory `VoxelMeshCache`. The
existing API surface does not change:

```
Caller -> VoxelMeshCache.Get(sprite)
            |
            +-- In-memory hit? Return mesh.
            +-- Disk cache hit? Deserialize, insert in-memory, return mesh.
            +-- Miss? Enqueue async build (existing path).
                On completion: insert in-memory + enqueue disk write.
```

`VoxelMeshCache.Get` gains an additional check between the in-memory
lookup and the `EnqueueBuild` fallback. This is the only change to the
existing `Get` method's control flow.

`VoxelMeshCache.Clear()` does NOT wipe the disk cache — it only clears
the in-memory dictionary. The disk cache persists across world reloads
within the same session. A separate `VoxelDiskCache.Purge()` method
allows explicit deletion (e.g., from a settings UI button or debug
command).

## Settings integration

New `SavedSettings` fields:

| Field | Type | Default | Purpose |
|---|---|---|---|
| `VoxelDiskCache` | `bool` | `true` | Master enable/disable for disk persistence |
| `VoxelDiskCacheMaxSizeMB` | `int` | `50` | Purge oldest rows when DB exceeds this size |

When `VoxelDiskCache` is `false`, `VoxelMeshCache.Get` skips the disk
lookup entirely and the save path is a no-op.

## Performance budget

| Operation | Target | Notes |
|---|---|---|
| Full warm load (417 sprites) | < 500 ms | SQLite read + BLOB deserialize + Mesh creation |
| Per-sprite disk read | < 1 ms | Single indexed row fetch |
| Per-sprite disk write | < 0.5 ms | Batched in transaction, amortized |
| DB file size (417 sprites) | ~4-5 MB | Well under the 50 MB default cap |
| Memory overhead | ~0 | BLOBs are transient; only the Unity Mesh objects persist (same as today) |

For comparison, the current async voxelization path takes 3-8 seconds to
drain 417 sprites depending on hardware, with visible placeholder cubes
during the build window.

## Error handling

- **Corrupt DB**: If SQLite open or any query throws, log a warning and
  fall back to the existing async-build path. The mod functions normally
  without the disk cache.
- **Schema migration**: The `CREATE TABLE IF NOT EXISTS` is idempotent.
  Future column additions use `ALTER TABLE ... ADD COLUMN` with defaults.
  Breaking schema changes drop and recreate the table (losing the cache
  is acceptable — it rebuilds transparently).
- **Disk full**: SQLite write failure is caught and logged. The in-memory
  cache is unaffected. Writes are retried on the next flush cycle.

## SQLite dependency

Unity's Mono runtime (net48) does not ship SQLite bindings.
Options in priority order:

1. **`Mono.Data.Sqlite`** — shipped with Unity's Mono runtime. Available
   as `Mono.Data.Sqlite.dll` in the Unity managed assemblies folder.
   Wraps the native `sqlite3` library that Unity already bundles. Zero
   additional dependencies.
2. **`Microsoft.Data.Sqlite`** (NuGet) — modern, lightweight ADO.NET
   provider. Requires shipping `e_sqlite3.dll` native binary alongside
   the mod. More maintenance burden but better API.
3. **Raw file I/O fallback** — if neither SQLite option is viable, use a
   flat binary file with a header index. Loses query flexibility but
   avoids the dependency entirely.

Recommendation: option 1 (`Mono.Data.Sqlite`) for zero-dep simplicity.

## Future extensions

- **Bone weight persistence**: `VoxelMeshCache.BuildWithBoneWeights`
  produces `SkinnedVoxelMesh` with `BoneIndices`. A `bone_data BLOB`
  column could cache these for Phase 6 skeletal meshes.
- **Smoothed mesh caching**: If `VoxelMeshSmoothing` is enabled, cache
  the post-smoothing mesh to avoid re-running `MeshSmoother.Smooth` on
  load. Requires adding a `smoothed BOOLEAN` column or storing both
  variants.
- **Shared BLOB dedup**: Sprites with identical pixel content could share
  BLOB storage via a normalized `mesh_data` table keyed by
  `sprite_hash`. Not worth the complexity for ~4 MB total.
- **Incremental hash**: For large sprites, compute a rolling hash during
  voxelization instead of a separate SHA-256 pass. Marginal gain.
