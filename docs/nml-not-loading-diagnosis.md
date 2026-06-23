# NML not loading diagnosis

Fresh boot captured after force-stopping `worldbox` and relaunching `worldbox.exe`.

## What the fresh logs show

### 1) BepInEx injection is working

`BepInEx/LogOutput.log` shows the full preloader/chainloader sequence:

- `Preloader started`
- `Loaded 1 patcher method from [BepInEx.Preloader 5.4.22.0]`
- `Patching [UnityEngine.CoreModule] with [BepInEx.Chainloader]`
- `Preloader finished`
- `Chainloader ready`
- `Chainloader started`
- `2 plugins to load`
- `Chainloader startup complete`

Relevant lines in the fresh log:

- [BepInEx log](/C:/Program Files (x86)/Steam/steamapps/common/worldbox/BepInEx/LogOutput.log#L6)
- [BepInEx log](/C:/Program Files (x86)/Steam/steamapps/common/worldbox/BepInEx/LogOutput.log#L11)
- [BepInEx log](/C:/Program Files (x86)/Steam/steamapps/common/worldbox/BepInEx/LogOutput.log#L12)
- [BepInEx log](/C:/Program Files (x86)/Steam/steamapps/common/worldbox/BepInEx/LogOutput.log#L24)

`winhttp.dll` and `doorstop_config.ini` are both present in the game root, so Doorstop is not the failure point:

- `C:\Program Files (x86)\Steam\steamapps\common\worldbox\winhttp.dll` exists
- `C:\Program Files (x86)\Steam\steamapps\common\worldbox\doorstop_config.ini` exists

### 2) NML's BepInEx plugin does not load

The fresh `BepInEx/LogOutput.log` only shows these plugins being loaded:

- `BepinexModCompatibilityLayer 1.0.5`
- `KeyGUI 1.2.0`

There are no `NeoModLoader` / `NML` plugin load lines in the fresh BepInEx log, and no `Loading mod`, `Compile Mod`, or `Init Mod` lines after chainloader startup.

The plugin directory confirms the missing link:

- `C:\Program Files (x86)\Steam\steamapps\common\worldbox\BepInEx\plugins\`
- contents: only `KeyGUI`

So the chain breaks before NML ever gets a chance to initialize.

### 3) NML never reaches mod loading

Because the NML BepInEx plugin is not present, there are no fresh mod-load lines for NML to emit.

The `Player.log` fresh boot reaches Unity initialization but does not show any NML mod-loading markers in the captured search (`NeoModLoader`, `NML`, `Loading mod`, `Compile Mod`, `Init Mod`).

## Root cause

The broken link is:

`BepInEx chainloader -> NML BepInEx plugin`

That plugin is missing from `BepInEx/plugins`, so NML never loads and the mod chain stays dead.

## Most likely fix

Restore the NML BepInEx plugin DLL into:

`C:\Program Files (x86)\Steam\steamapps\common\worldbox\BepInEx\plugins\`

If the plugin was recently removed by an install step, Steam verify, or an overwrite from `wsm3d.ps1`, reinstall NML and confirm the plugin DLL lands back in `BepInEx/plugins` before relaunching.

## Secondary checks

- `Mods/WorldSphereMod3D/` is intact.
- `worldbox_Data/StreamingAssets/Mods/` exists and contains `NeoModLoader.dll`, but that is not sufficient by itself for this boot path.
- `StreamingAssets/Mods/` at the game root does not exist.

## Conclusion

Last successful stage: BepInEx preloader/chainloader startup.

First failure: NML does not load because its BepInEx plugin is missing from `BepInEx/plugins`.
