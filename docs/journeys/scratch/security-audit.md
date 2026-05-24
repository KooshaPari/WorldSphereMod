# WSM3D Security Audit

Scope: MCP bridge, AssetBundle loading, `SavedSettings` JSON, and Harmony patches.
Note: the WSM3D MCP server in this repo defaults to `127.0.0.1:8766`, not `8765`.

## High

- The MCP HTTP surface is loopback-bound by default, but it has no visible authn/authz gate and exposes high-impact operations to any local process that can reach the socket. The server starts with `--host` defaulting to `127.0.0.1`, then runs `mcp.run(transport="http", host=args.host, port=args.port)`, and the tool set includes game control, settings mutation, build/install, and `codex_exec`. A malicious local process could therefore kill the game, inject input, rewrite settings, or trigger builds without further checks. See [server.py](C:/Users/koosh/Dev/WorldSphereMod/Tools/wsm3d-mcp/wsm3d_mcp/server.py#L12), [server.py](C:/Users/koosh/Dev/WorldSphereMod/Tools/wsm3d-mcp/wsm3d_mcp/server.py#L338), [server.py](C:/Users/koosh/Dev/WorldSphereMod/Tools/wsm3d-mcp/wsm3d_mcp/server.py#L343), [server.py](C:/Users/koosh/Dev/WorldSphereMod/Tools/wsm3d-mcp/wsm3d_mcp/server.py#L63), [server.py](C:/Users/koosh/Dev/WorldSphereMod/Tools/wsm3d-mcp/wsm3d_mcp/server.py#L142), [server.py](C:/Users/koosh/Dev/WorldSphereMod/Tools/wsm3d-mcp/wsm3d_mcp/server.py#L177), [server.py](C:/Users/koosh/Dev/WorldSphereMod/Tools/wsm3d-mcp/wsm3d_mcp/server.py#L239).

## Medium

- AssetBundle integrity is not verified in the mod code. The runtime simply asks NeoModLoader for `AssetBundleUtils.GetAssetBundle("worldsphere")` and immediately dereferences assets by path; I found no checksum, signature, hash, or manifest validation step before use. If an attacker can replace the local bundle files, the mod will trust whatever Unity object data is present. See [Core.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Core.cs#L456) and [Info.txt](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/AssetBundles/Info.txt#L1).

## Low

- `SavedSettings` deserialization does not look like a code-execution or object-injection vector. The loader uses plain `JsonConvert.DeserializeObject<SavedSettings>(...)` into a fixed POCO of primitive fields, catches all parse failures, and falls back to rewriting defaults. I did not find `TypeNameHandling`, custom converters, or polymorphic deserialization in this path. The main risk is config tampering, not JSON injection. See [Core.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Core.cs#L35) and [SavedSettings.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/SavedSettings.cs#L4).

## No Finding

- I did not find Harmony patches that target save-file deserialization or network APIs. The patch inventory is dominated by camera, rendering, world-state, and UI hooks, and `Core.Patch()` wires the same sort of render/world methods. That makes the patch surface broad, but not security-sensitive in the deserialization/network sense you asked about. See [harmony-patch-inventory.md](C:/Users/koosh/Dev/WorldSphereMod/docs/journeys/scratch/harmony-patch-inventory.md#L10) and [Core.cs](C:/Users/koosh/Dev/WorldSphereMod/WorldSphereMod/Code/Core.cs#L117).
