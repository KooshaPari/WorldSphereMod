TASK: Build and INSTALL the current fixed WSM3D build to the game so the user can launch without crashing. Repo E:/Dev/WorldSphereMod. Work on branch fix/postfx-shaders-rebake (has the crash-proof shader-bundle hash gate, commit 976473a0). Rules: never stash (if tree dirty, commit to a wip branch — do NOT git stash); never force-push; do NOT merge to trunk; do NOT launch the game (the user will launch via Steam themselves).

CONTEXT: The installed mod at "C:/Program Files (x86)/Steam/steamapps/common/Worldbox/Mods/WorldSphereMod3D/" is STALE (version 2.0.0-beta.6, old Code/, old 65KB CompoundSpheres.dll, and a stale/bad wsm3d-shaders bundle from 02:44) — that stale bundle is what native-crashed Unity on launch. The repo's current bundle (157328 bytes, AssetFileHash 34b1a0aea10a1405dd5fae3546776d2d) is VERIFIED-VALID (all 7 SafeShaders load, supported=True). Re-installing the current build deploys the good bundle + the new IsVerifiedSafeShaderBundle hash gate so launch is crash-safe either way.

DO THIS:
1. Confirm you are on fix/postfx-shaders-rebake at 976473a0 (or later): `git checkout fix/postfx-shaders-rebake`. If the working tree is dirty from a prior agent, commit those changes to a new branch wip/<desc> first — NEVER stash.
2. Build: dotnet build WorldSphereMod.csproj -c Release (expect 0 err).
3. Run the installer: ./Tools/install.ps1 (use pwsh / PS7, NOT powershell 5.1). It copies Code/ + Assemblies/CompoundSpheres.dll + AssetBundles/win/* to the game Mods folder. If install.ps1 needs a target param, check the script header + the deploy note (reference_worldspheremod3d_deploys mentions a stale-DLL fix).
4. VERIFY the deployment (critical):
   - Installed "C:/Program Files (x86)/Steam/steamapps/common/Worldbox/Mods/WorldSphereMod3D/AssetBundles/win/wsm3d-shaders.manifest" AssetFileHash == 34b1a0aea10a1405dd5fae3546776d2d (so the hash gate PASSES and postFX shaders load — not just fall back to Standard).
   - Installed wsm3d-shaders bundle size == 157328 bytes.
   - Installed Code/Core.cs contains IsVerifiedSafeShaderBundle (fix deployed).
   - Installed mod.json version + Code/ file count refreshed (no longer stale beta.6).
   - Report installed CompoundSpheres.dll size (trunk-based #36 has the 65024 trunk DLL — fine; GPU #37 is a separate later verify).
5. Do NOT install #37/#199 GPU work — this install is the #36 postFX + crash-fix + prior-trunk (terrain/voxel/rig) state, a clean verify target.

REPORT: build result, install.ps1 output summary, the 4 verification checks (hash match Y/N, bundle size, Core.cs has the gate, version refreshed), installed DLL size, and a clear GO/NO-GO: "safe to launch via Steam for visual verify of #36 postFX + terrain/voxel/rig". If the hash did NOT match after install, say postFX will fall back to Standard (no crash, but postFX won't show) and why.
