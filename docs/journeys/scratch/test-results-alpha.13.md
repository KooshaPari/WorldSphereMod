# Test results-alpha.13
- Generated: 2026-05-21 18:55:17 -07:00
- Result counts: Passed projects=1, Failed projects=1, Skipped/not-found=0
## Commands
- dotnet test tests/WorldSphereMod.Tests.Unit/WorldSphereMod.Tests.Unit.csproj
- dotnet test tests/WorldSphereMod.Tests.E2E/WorldSphereMod.Tests.E2E.csproj (if project exists)

## tests/WorldSphereMod.Tests.Unit/WorldSphereMod.Tests.Unit.csproj
- Status: passed
- Exit code: 0
### Extracted summary
```
C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI\WorldSphereAPI.cs(70,19): warning CS8632: The annotation for nullable reference types should only be used in code within a '#nullable' annotations context. [C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI.csproj]
C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI\WorldSphereAPI.cs(75,19): warning CS8632: The annotation for nullable reference types should only be used in code within a '#nullable' annotations context. [C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI.csproj]
C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI\WorldSphereAPI.cs(80,19): warning CS8632: The annotation for nullable reference types should only be used in code within a '#nullable' annotations context. [C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI.csproj]
  WorldSphereAPI -> C:\Users\koosh\Dev\WorldSphereMod\bin\Debug\netstandard2.0\WorldSphereAPI.dll
  WorldSphereMod.Tests.Unit -> C:\Users\koosh\Dev\WorldSphereMod\tests\WorldSphereMod.Tests.Unit\bin\Debug\net8.0\WorldSphereMod.Tests.Unit.dll
Test run for C:\Users\koosh\Dev\WorldSphereMod\tests\WorldSphereMod.Tests.Unit\bin\Debug\net8.0\WorldSphereMod.Tests.Unit.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.75]     DelegateBindingTests.Reflective_ctor_leaves_v2_members_safe_when_v1_only_host [SKIP]
  Skipped DelegateBindingTests.Reflective_ctor_leaves_v2_members_safe_when_v1_only_host [1 ms]
[xUnit.net 00:00:00.80]     DelegateBindingTests.Reflective_ctor_binds_full_v1_surface_against_v1_host [SKIP]
[xUnit.net 00:00:00.80]     DelegateBindingTests.Reflective_ctor_binds_v2_surface_against_v2_host [SKIP]
  Skipped DelegateBindingTests.Reflective_ctor_binds_full_v1_surface_against_v1_host [1 ms]
  Skipped DelegateBindingTests.Reflective_ctor_binds_v2_surface_against_v2_host [1 ms]

Passed!  - Failed:     0, Passed:    74, Skipped:     3, Total:    77, Duration: 207 ms - WorldSphereMod.Tests.Unit.dll (net8.0)
```
### Full output
```
  Determining projects to restore...
  Restored C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI.csproj (in 338 ms).
  1 of 2 projects are up-to-date for restore.
C:\program files\dotnet\sdk\11.0.100-preview.2.26159.112\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.RuntimeIdentifierInference.targets(383,5): message NETSDK1057: You are using a preview version of .NET. See: https://aka.ms/dotnet-support-policy [C:\Users\koosh\Dev\WorldSphereMod\tests\WorldSphereMod.Tests.Unit\WorldSphereMod.Tests.Unit.csproj]
C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI\WorldSphereAPI.cs(25,14): warning CS8632: The annotation for nullable reference types should only be used in code within a '#nullable' annotations context. [C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI.csproj]
C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI\WorldSphereAPI.cs(26,23): warning CS8632: The annotation for nullable reference types should only be used in code within a '#nullable' annotations context. [C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI.csproj]
C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI\WorldSphereAPI.cs(27,26): warning CS8632: The annotation for nullable reference types should only be used in code within a '#nullable' annotations context. [C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI.csproj]
C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI\WorldSphereAPI.cs(70,19): warning CS8632: The annotation for nullable reference types should only be used in code within a '#nullable' annotations context. [C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI.csproj]
C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI\WorldSphereAPI.cs(75,19): warning CS8632: The annotation for nullable reference types should only be used in code within a '#nullable' annotations context. [C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI.csproj]
C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI\WorldSphereAPI.cs(80,19): warning CS8632: The annotation for nullable reference types should only be used in code within a '#nullable' annotations context. [C:\Users\koosh\Dev\WorldSphereMod\WorldSphereAPI.csproj]
  WorldSphereAPI -> C:\Users\koosh\Dev\WorldSphereMod\bin\Debug\netstandard2.0\WorldSphereAPI.dll
  WorldSphereMod.Tests.Unit -> C:\Users\koosh\Dev\WorldSphereMod\tests\WorldSphereMod.Tests.Unit\bin\Debug\net8.0\WorldSphereMod.Tests.Unit.dll
Test run for C:\Users\koosh\Dev\WorldSphereMod\tests\WorldSphereMod.Tests.Unit\bin\Debug\net8.0\WorldSphereMod.Tests.Unit.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.75]     DelegateBindingTests.Reflective_ctor_leaves_v2_members_safe_when_v1_only_host [SKIP]
  Skipped DelegateBindingTests.Reflective_ctor_leaves_v2_members_safe_when_v1_only_host [1 ms]
[xUnit.net 00:00:00.80]     DelegateBindingTests.Reflective_ctor_binds_full_v1_surface_against_v1_host [SKIP]
[xUnit.net 00:00:00.80]     DelegateBindingTests.Reflective_ctor_binds_v2_surface_against_v2_host [SKIP]
  Skipped DelegateBindingTests.Reflective_ctor_binds_full_v1_surface_against_v1_host [1 ms]
  Skipped DelegateBindingTests.Reflective_ctor_binds_v2_surface_against_v2_host [1 ms]

Passed!  - Failed:     0, Passed:    74, Skipped:     3, Total:    77, Duration: 207 ms - WorldSphereMod.Tests.Unit.dll (net8.0)
```

## tests/WorldSphereMod.Tests.E2E/WorldSphereMod.Tests.E2E.csproj
- Status: failed
- Exit code: 1
### Extracted summary
```

            WorldSphereMod.Fx.Environmental.Tick();

            WorldSphereMod.Fx.PostFxController.ApplySetting(Core.savedSettings.PostFX);
        }
    }
}
" to contain ""Sprites/Default"" because VoxelRender.EnsureMaterial must exclude Sprites/Default — it's a dummy fallback.
  Stack Trace:
     at FluentAssertions.Primitives.StringAssertions`1.NotContain(String unexpected, String because, Object[] becauseArgs)
   at SourceContentInvariantsTests.VoxelRender_cs_EnsureMaterial_excludes_default_sprites_and_hidden_colored() in C:\Users\koosh\Dev\WorldSphereMod\tests\WorldSphereMod.Tests.E2E\SourceContentInvariantsTests.cs:line 73
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     5, Passed:    18, Skipped:     0, Total:    23, Duration: 449 ms - WorldSphereMod.Tests.E2E.dll (net8.0)
```
### Full output
```
  Determining projects to restore...
  All projects are up-to-date for restore.
C:\program files\dotnet\sdk\11.0.100-preview.2.26159.112\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.RuntimeIdentifierInference.targets(383,5): message NETSDK1057: You are using a preview version of .NET. See: https://aka.ms/dotnet-support-policy [C:\Users\koosh\Dev\WorldSphereMod\tests\WorldSphereMod.Tests.E2E\WorldSphereMod.Tests.E2E.csproj]
  WorldSphereMod.Tests.E2E -> C:\Users\koosh\Dev\WorldSphereMod\tests\WorldSphereMod.Tests.E2E\bin\Debug\net8.0\WorldSphereMod.Tests.E2E.dll
Test run for C:\Users\koosh\Dev\WorldSphereMod\tests\WorldSphereMod.Tests.E2E\bin\Debug\net8.0\WorldSphereMod.Tests.E2E.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.95]     RepositoryArtifactsTests.Adr_index_listed [FAIL]
[xUnit.net 00:00:00.95]     WorldSphereTesterCoverageTests.LodSelector_hysteresis_requires_three_frames_before_tier_change [FAIL]
[xUnit.net 00:00:00.96]     Alpha8To9CoverageTests.MeshInstanceBatcher_applies_Color_per_instance_in_material_property_block [FAIL]
[xUnit.net 00:00:00.96]     SourceContentInvariantsTests.WorldSphereTab_cs_contains_all_eleven_TogglePhase_handlers [FAIL]
  Failed WorldSphereTesterCoverageTests.LodSelector_hysteresis_requires_three_frames_before_tier_change [231 ms]
  Error Message:
   Expected source "using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.LOD
{
    public enum LodTier { Voxel, Proxy, Impostor }

    public static class LodSelector
    {
        public static bool ImpostorOnlyMode;
        public static float VoxelThreshold = 0.08f;
        public static float ProxyThreshold = 0.020f;

        struct LodHysteresis
        {
            public LodTier current;
            public LodTier pending;
            public int pendingFrames;
        }

        static readonly Dictionary<int, LodHysteresis> _hyst = new Dictionary<int, LodHysteresis>();

        // Cached squared-distance LOD thresholds; recomputed only when any of the inputs
        // (camera FOV, LODScale, VoxelThreshold, ProxyThreshold) change. Saves an Mathf.Tan,
        // two divides and two muls per actor per frame; per-actor cost collapses to a
        // squared-distance compare.
        static float _cachedFov = float.NaN;
        static float _cachedLodScale = float.NaN;
        static float _cachedVoxelThreshold = float.NaN;
        static float _cachedProxyThreshold = float.NaN;
        static float _voxelMaxDistSqr;
        static float _proxyMaxDistSqr;
        // Entity height is the assumed world-units height used to compute the LOD
        // screen-projected size threshold. Phase 1 ships with VoxelScaleMultiplier=8
        // (see project_wsm3d_phase1_visible — meshes are 8x oversize so they're visible
        // at vanilla strategy-view altitude). Pre-multiplying entityHeight here keeps
        // the LOD math in sync with the actual rendered size without forcing the user
        // to set LODScale=8 manually.
        // Bumped 8→16 at alpha.8 to match VoxelScaleMultiplier=16 (commit 698883e).
        const float _entityHeight = 0.5f * 16.0f;

        public static LodTier Select(Vector3 worldPos, int instanceId)
        {
            if (ImpostorOnlyMode) return LodTier.Impostor;

            Camera cam = CameraManager.MainCamera;
            if (cam == null) return LodTier.Voxel;

            float fov = cam.fieldOfView;
            float lodScale = Core.savedSettings.LODScale;
            if (fov != _cachedFov || lodScale != _cachedLodScale
                || VoxelThreshold != _cachedVoxelThreshold || ProxyThreshold != _cachedProxyThreshold)
            {
                float tanHalfFov = Mathf.Max(0.0001f, Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
                float voxelMaxDist = _entityHeight * lodScale / (VoxelThreshold * tanHalfFov);
                float proxyMaxDist = _entityHeight * lodScale / (ProxyThreshold * tanHalfFov);
                _voxelMaxDistSqr = voxelMaxDist * voxelMaxDist;
                _proxyMaxDistSqr = proxyMaxDist * proxyMaxDist;
                _cachedFov = fov;
                _cachedLodScale = lodScale;
                _cachedVoxelThreshold = VoxelThreshold;
                _cachedProxyThreshold = ProxyThreshold;
            }

            Vector3 camPos = cam.transform.position;
            float dx = worldPos.x - camPos.x;
            float dy = worldPos.y - camPos.y;
            float dz = worldPos.z - camPos.z;
            float distSqr = dx * dx + dy * dy + dz * dz;

            LodTier proposed;
            if (distSqr < _voxelMaxDistSqr) proposed = LodTier.Voxel;
            else if (distSqr < _proxyMaxDistSqr) proposed = LodTier.Proxy;
            else proposed = LodTier.Impostor;

            if (!_hyst.TryGetValue(instanceId, out LodHysteresis h))
            {
                h = new LodHysteresis { current = proposed, pending = proposed, pendingFrames = 0 };
                _hyst[instanceId] = h;
                return h.current;
            }

            if (h.current == proposed)
            {
                h.pending = proposed;
                h.pendingFrames = 0;
                _hyst[instanceId] = h;
                return h.current;
            }

            if (h.pending == proposed)
            {
                h.pendingFrames++;
                if (h.pendingFrames >= 3)
                {
                    h.current = proposed;
                    h.pendingFrames = 0;
                }
            }
            else
            {
                h.pending = proposed;
                h.pendingFrames = 1;
            }

            _hyst[instanceId] = h;
            return h.current;
        }

        public static void ResetHysteresis()
        {
            _hyst.Clear();
        }

        public static void Remove(int instanceId) { _hyst.Remove(instanceId); }
    }
}
" to contain "else { h.pending = proposed; h.pendingFrames = 1; }".
  Stack Trace:
     at FluentAssertions.Primitives.StringAssertions`1.Contain(String expected, String because, Object[] becauseArgs)
   at WorldSphereTesterCoverageTests.LodSelector_hysteresis_requires_three_frames_before_tier_change() in C:\Users\koosh\Dev\WorldSphereMod\tests\WorldSphereMod.Tests.E2E\WorldSphereTesterCoverageTests.cs:line 77
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed Alpha8To9CoverageTests.MeshInstanceBatcher_applies_Color_per_instance_in_material_property_block [231 ms]
  Error Message:
   Expected methodBody "
            int end = Mathf.Min(bucket.Matrices.Count, start + total);
            if (_fallbackDrawDiagFrames < 5)
            {
                _fallbackDrawDiagFrames++;
                Vector4 firstPos = bucket.Matrices.Count > start ? bucket.Matrices[start].GetColumn(3) : new Vector4(0,0,0,0);
                Debug.Log($"[WSM3D][DIAG-FB] DrawFallbackPath entry frame={_fallbackDrawDiagFrames} mesh={key.Mesh?.name ?? "<null>"} material={key.Material?.name ?? "<null>"} bucket.Matrices.Count={bucket.Matrices.Count} start={start} total={total} end={end} firstPos=({firstPos.x:F2},{firstPos.y:F2},{firstPos.z:F2}) layer={layer}");
            }
            for (int i = start; i < end; i++)
            {
                bucket.Block.Clear();
                Vector4 tint = bucket.Colors[i];
                Color colorTint = new Color(tint.x, tint.y, tint.z, tint.w);
                bucket.Block.SetVector(_colorProp, tint);
                bucket.Block.SetColor(_baseColorProp, colorTint);
                bucket.Block.SetColor(_colorPropUnlit, colorTint);
                Graphics.DrawMesh(
                    key.Mesh,
                    bucket.Matrices[i],
                    key.Material,
                    layer,
                    null,
                    0,
                    bucket.Block,
                    shadows,
                    receive,
                    null,
                    LightProbeUsage.Off);
                FrameDrawCalls++;

                if (Core.savedSettings.DebugVoxelOutline)
                {
                    Vector4 p = bucket.Matrices[i].GetColumn(3);
                    Matrix4x4 debugTrs = Matrix4x4.TRS(
                        new Vector3(p.x, p.y, p.z),
                        Quaternion.identity,
                        Vector3.one * kDebugCubeSize);
                    Color debugTint = tint.w > 0f
                        ? new Color(tint.x, tint.y, tint.z, tint.w)
                        : new Color(1f, 0f, 1f, 1f);
                    bucket.Block.Clear();
                    bucket.Block.SetVector(_colorProp, debugTint);
                    bucket.Block.SetColor(_baseColorProp, debugTint);
                    bucket.Block.SetColor(_colorPropUnlit, debugTint);
                    Graphics.DrawMesh(
                        GetDebugCubeMesh(),
                        debugTrs,
                        key.Material,
                        layer,
                        null,
                        0,
                        bucket.Block,
                        shadows,
                        receive,
                        null,
                        LightProbeUsage.Off);
                    FrameDrawCalls++;
                }
            }
        " to contain "bucket.Block.SetColor(_colorPropUnlit, tint);".
  Stack Trace:
     at FluentAssertions.Primitives.StringAssertions`1.Contain(String expected, String because, Object[] becauseArgs)
   at Alpha8To9CoverageTests.MeshInstanceBatcher_applies_Color_per_instance_in_material_property_block() in C:\Users\koosh\Dev\WorldSphereMod\tests\WorldSphereMod.Tests.E2E\Alpha8To9CoverageTests.cs:line 117
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed RepositoryArtifactsTests.Adr_index_listed [225 ms]
  Error Message:
   Expected indexText "# Architecture Decision Records

ADRs capture *why* the fork looks the way it does. Each ADR is a single
decision with context, alternatives, and consequences. They are
**append-only**: superseded ADRs are marked, not deleted.

| ID | Title | Status |
|---|---|---|
| [0001](/adr/0001-hybrid-sprite-to-3d-strategy) | Hybrid sprite→3D strategy (voxel actors + procgen buildings + crossed-quad foliage) | Accepted |
| [0002](/adr/0002-defer-shader-bake-to-unity-2022-3) | Defer lit-shader bake to Unity 2022.3 (Phase 5b dependency) | Accepted |
| [0003](/adr/0003-reflective-urp-bindings) | Reflective URP bindings (`ShadowCascadeConfig` + `PostFxController`) | Accepted |
| [0004](/adr/0004-rigid-skinning-over-blended) | Rigid (one-bone-per-vertex) skinning over blended for voxel meshes | Accepted |
| [0005](/adr/0005-default-on-flags-per-phase-ship-gate) | Per-phase `SavedSettings` flag flips default-on only after in-game validation | Accepted |
| [0016](/adr/0016-thread-safe-meshinstancebatcher-submit-deferred-queue) | Thread-safe `MeshInstanceBatcher.Submit` via deferred queue | Accepted |

## How to add an ADR

1. Copy [`template.md`](/adr/template) to `docs/adr/NNNN-short-slug.md`.
2. Add it to the table above and to `docs/.vitepress/config.mts` sidebar.
3. Set status to **Proposed**. Open a PR.
4. After review, flip to **Accepted** in the same or a follow-up PR.

If a later ADR supersedes this one, **don't delete it** — change its
status to **Superseded by ADR-NNNN** with the date, and link the new ADR.
" to contain "0011-phase-1-visibility-postmortem" because ADR file '0011-phase-1-visibility-postmortem.md' must be listed in docs/adr/index.md.
  Stack Trace:
     at FluentAssertions.Primitives.StringAssertions`1.Contain(String expected, String because, Object[] becauseArgs)
   at RepositoryArtifactsTests.Adr_index_listed() in C:\Users\koosh\Dev\WorldSphereMod\tests\WorldSphereMod.Tests.E2E\RepositoryArtifactsTests.cs:line 128
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
  Failed SourceContentInvariantsTests.WorldSphereTab_cs_contains_all_eleven_TogglePhase_handlers [358 ms]
  Error Message:
   Expected tab "using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using NCMS.Utils;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
namespace WorldSphereMod.UI
{
    struct ButtonData
    {
        public PowerToggleAction Action;
        public string Name;
        public string Description;
        public string IconPath;
        public bool IsActive;
        public bool CanBeFalse;
        public ButtonData(string Name, string Description, string IconPath, bool IsActive, PowerToggleAction Action, bool CanBeFalse = true)
        {
            this.Name = Name;
            this.Description = Description;
            this.IconPath = IconPath;
            this.IsActive = IsActive;
            this.Action = Action;
            this.CanBeFalse = CanBeFalse;
        }
    }
    public static class WorldSphereTab
    {
        public static PowersTab Tab;
        public static Sprite ModIcon;
        const string FallbackIconPath = "WorldSphereMod/ModIcon";
        const string PhasesWindowId = "3D Phases";
        const string PhasesWindowTitle = "phases_window";
        static readonly Dictionary<string, Sprite?> IconCache = new Dictionary<string, Sprite?>();
        static GameObject Space;
        static GameObject Line;
        static bool _isPhasesWindowSuppressionHooked;
        static void CreateTabTools()
        {
            Space = ResourcesFinder.FindResource<GameObject>("_space");
            Line = Object.Instantiate(ResourcesFinder.FindResource<GameObject>("_line"));
            Line.transform.localScale = new Vector3(Line.transform.localScale.x, Line.transform.localScale.y * 6, Line.transform.localScale.z);
        }

        public static void Begin()
        {
            CreateTabTools();
            CreateTab();
            CreateButtons();
            SuppressPhasesWindow();
            EnsurePhasesWindowAutoCloseHook();
        }
        static void AddLine()
        {
            Object.Instantiate(Line).transform.SetParent(Tab.transform);
        }

        static void CreateTab()
        {
            ModIcon = SafeLoadSprite("WorldSphereMod/ModIcon");
            Tab = TabManager.CreateTab("WorldSphereMod", "world_sphere_tab", "world_sphere_tab_desc", ModIcon, "world_sphere_tab_author");
        }
        public static Sprite SafeLoadSprite(string path)
        {
            if (IconCache.TryGetValue(path, out var cachedSprite))
            {
                return cachedSprite;
            }

            Sprite? sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                global::UnityEngine.Debug.LogWarning($"[WSM3D] Sprite resource not found: {path} - falling back to ModIcon");
                if (!IconCache.TryGetValue(FallbackIconPath, out sprite))
                {
                    sprite = Resources.Load<Sprite>(FallbackIconPath);
                    IconCache[FallbackIconPath] = sprite;
                }
            }
            IconCache[path] = sprite;
            return sprite;
        }

        public static void SetGodPowerSprite(ref GodPower power, string iconPath)
        {
            var sprite = SafeLoadSprite(iconPath);
            if (sprite == null)
            {
                return;
            }

            var powerType = typeof(GodPower);
            const BindingFlags Binding = BindingFlags.Public | BindingFlags.Instance;
            var iconField = powerType.GetField("icon", Binding);
            if (iconField != null && iconField.FieldType == typeof(Sprite))
            {
                iconField.SetValue(power, sprite);
                return;
            }
            var spriteField = powerType.GetField("sprite", Binding);
            if (spriteField != null && spriteField.FieldType == typeof(Sprite))
            {
                spriteField.SetValue(power, sprite);
                return;
            }
            var iconProperty = powerType.GetProperty("icon", Binding);
            if (iconProperty != null && iconProperty.CanWrite && iconProperty.PropertyType == typeof(Sprite))
            {
                iconProperty.SetValue(power, sprite, null);
                return;
            }
            var spriteProperty = powerType.GetProperty("sprite", Binding);
            if (spriteProperty != null && spriteProperty.CanWrite && spriteProperty.PropertyType == typeof(Sprite))
            {
                spriteProperty.SetValue(power, sprite, null);
            }
        }
        public static Text addText(string window, string textString, GameObject parent, int sizeFont, Vector3 pos, Vector2 addSize = default(Vector2))
        {
            GameObject textRef = GameObject.Find($"/Canvas Container Main/Canvas - Windows/windows/" + window + "/Background/Title");
            GameObject textGo = Object.Instantiate(textRef, parent.transform);
            textGo.SetActive(true);

            var textComp = textGo.GetComponent<Text>();
            textComp.fontSize = sizeFont;
            textComp.resizeTextMaxSize = sizeFont;
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.position = new Vector3(0, 0, 0);
            textRect.localPosition = pos + new Vector3(0, -50, 0);
            textRect.sizeDelta = new Vector2(100, 100) + addSize;
            textGo.AddComponent<GraphicRaycaster>();
            textComp.text = textString;

            return textComp;
        }
        static Slider GenerateSlider(string Name,float Min, float Max, float Current, UnityAction<float> Func, string Window)
        {
            GameObject sliderGO = new GameObject(Name, typeof(Slider), typeof(Image));
            Transform Parent = WindowManager.windows[Window].Object.transform;
            sliderGO.transform.SetParent(Parent, false);
            RectTransform rt = sliderGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(25, 5);
            rt.anchoredPosition = new Vector2(0, 0);
            Slider slider = sliderGO.GetComponent<Slider>();
            slider.minValue = Min;
            slider.maxValue = Max;
            slider.value = Current;
            slider.onValueChanged.AddListener(Func);

            GameObject trackGO = new GameObject("Track");
            trackGO.transform.SetParent(sliderGO.transform, false);
            Image trackImage = trackGO.AddComponent<Image>();
            RectTransform trackRect = trackGO.GetComponent<RectTransform>();
            trackRect.sizeDelta = new Vector2(100, 2);
            trackRect.anchoredPosition = Vector2.zero;
            trackImage.color = Color.gray;

            GameObject handleAreaGO = new GameObject("Handle Slide Area");
            handleAreaGO.transform.SetParent(sliderGO.transform, false);
            RectTransform handleAreaRect = handleAreaGO.AddComponent<RectTransform>();
            handleAreaRect.sizeDelta = new Vector2(100, 0);

            GameObject handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(handleAreaGO.transform, false);
            Image handleImage = handleGO.AddComponent<Image>();
            RectTransform handleRect = handleGO.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(10, 10);
            handleImage.color = Color.white;

            slider.targetGraphic = handleImage;
            slider.handleRect = handleGO.GetComponent<RectTransform>();

            Text textGO = addText(Window, $"{Name} : {Current}", sliderGO, 10, new Vector3(0, -2));
            textGO.text = $"{LM.Get(Name)} : {Current}";
            slider.onValueChanged.AddListener((float x) => textGO.text = $"{LM.Get(Name)} : {x}");

            return slider;
        }
        static void CreateButtons()
        {
            CreateToggleButton("Is3D", "WorldSphereMod/ModIcon", "is_3d", "is_3d_description", Toggle3D, Core.savedSettings.Is3D);
            CreateWindowButton("Sprite Settings", "WorldSphereMod/Rotate", "sprite_settings_window", new List<ButtonData>()
            {
               ///new ButtonData("sprites_rotate_to_camera", "sprites_rotate_to_camera_description", "WorldSphereMod/Rotate", Core.savedSettings.RotateStuffToCamera, ToggleRotations),
               new ButtonData("sprites_rotate_to_camera", "sprites_rotate_to_camera_description", "WorldSphereMod/Rotate", Core.savedSettings.RotateStuffToCamera, ToggleRotations),
               new ButtonData("building_style_procgen", "building_style_procgen_description", "WorldSphereMod/World", Core.savedSettings.BuildingStyleProcgen, ToggleBuildingStyleProcgen)
            }
            );
            GenerateSlider("building_size", 0.1f, 5f, Core.savedSettings.BuildingSize, (float val) => { Core.savedSettings.BuildingSize = val; Core.SaveSettings(); }, "Sprite Settings");
            CreateWindowButton("Camera Settings", "WorldSphereMod/Camera", "camera_settings_window", new List<ButtonData>()
            {
                new ButtonData("inverted_camera", "inverted_camera_description", "WorldSphereMod/Camera", Core.savedSettings.InvertedCameraMovement, ToggleCamera),
                new ButtonData("first_person", "first_person_description", "WorldSphereMod/Camera", Core.savedSettings.FirstPerson, ToggleFirtPerson),
                new ButtonData("camera_rotates_with_world", "camera_rotates_with_world_description", "WorldSphereMod/Camera", Core.savedSettings.CameraRotatesWithWorld, ToggleRotateToWorld),
                new ButtonData("upside_down_movement", "upside_down_movement_description", "WorldSphereMod/Camera", Core.savedSettings.UpsideDownMovement, UpsideDown)
            });
            GenerateSlider("render_distance", 1, 20, Core.savedSettings.RenderRange, (float val) => { Core.savedSettings.RenderRange = val; Core.SaveSettings(); }, "Camera Settings");
            CreateWindowButton("World Settings", "WorldSphereMod/World", "world_settings_window", new List<ButtonData>()
            {
                new ButtonData("cylindrical_shape", "cylindrical_shape_description", "WorldSphereMod/Round", Core.savedSettings.CurrentShape == 0, SetShape, false),
                new ButtonData("flat_shape", "flat_shape_description", "WorldSphereMod/Flat", Core.savedSettings.CurrentShape == 1, SetShape, false),
                new ButtonData("perlin_noise", "perlin_noise_description", "WorldSphereMod/PerlinNoise", Core.savedSettings.PerlinNoise, PerlinNoise)
            });
            GenerateSlider("tile_length_multiplier", 1, 10, Core.savedSettings.TileHeight, (float x) => { Core.savedSettings.TileHeight = x; Core.SaveSettings(); }, "World Settings");

            // v2 fork: per-phase toggles. The default values come from
            // SavedSettings; the toggle action flips + persists. Without
            // surfacing these here the user has no way to turn Phase 1's
            // voxel actors on, so sprites stay 2D and the fork looks like
            // a no-op compared to upstream.
            CreateWindowButton(PhasesWindowId, "WorldSphereMod/ModIcon", PhasesWindowTitle, new List<ButtonData>()
            {
                new ButtonData("voxel_entities",       "voxel_entities_description",       "WorldSphereMod/Round",        Core.savedSettings.VoxelEntities,       TogglePhase),
                new ButtonData("procedural_buildings", "procedural_buildings_description", "WorldSphereMod/World",         Core.savedSettings.ProceduralBuildings, TogglePhase),
                new ButtonData("crossed_quad_foliage", "crossed_quad_foliage_description", "WorldSphereMod/Flat",          Core.savedSettings.CrossedQuadFoliage, TogglePhase),
                new ButtonData("biome_blending",       "biome_blending_description",       "WorldSphereMod/World",         Core.savedSettings.BiomeBlending,       TogglePhase),
                new ButtonData("mesh_water",           "mesh_water_description",           "WorldSphereMod/PerlinNoise",   Core.savedSettings.MeshWater,           TogglePhase),
                new ButtonData("mountain_slope_smoothing", "mountain_slope_smoothing_description", "WorldSphereMod/World", Core.savedSettings.MountainSlopeSmoothing, TogglePhase),
                new ButtonData("high_shadows",         "high_shadows_description",         "WorldSphereMod/SkyBox",        Core.savedSettings.HighShadows,         TogglePhase),
                new ButtonData("hdr_skybox",           "hdr_skybox_description",           "WorldSphereMod/SkyBox",        Core.savedSettings.HdrSkybox,           TogglePhase),
                new ButtonData("color_grading_lut",    "color_grading_lut_description",    "WorldSphereMod/ModIcon",       Core.savedSettings.ColorGradingLut,      TogglePhase),
                new ButtonData("ssao_enabled",         "ssao_enabled_description",         "WorldSphereMod/ModIcon",       Core.savedSettings.SSAOEnabled,          TogglePhase),
                new ButtonData("ssgi_enabled",         "ssgi_enabled_description",         "WorldSphereMod/ModIcon",       Core.savedSettings.SSGIEnabled,          TogglePhase),
                new ButtonData("skeletal_animation",   "skeletal_animation_description",   "WorldSphereMod/Rotate",        Core.savedSettings.SkeletalAnimation,   TogglePhase),
                new ButtonData("worldspace_ui",        "worldspace_ui_description",        "WorldSphereMod/Camera",        Core.savedSettings.WorldspaceUI,        TogglePhase),
                new ButtonData("worldspace_health_3d", "worldspace_health_3d_description", "WorldSphereMod/ModIcon",      Core.savedSettings.WorldspaceHealth3D,  TogglePhase),
                new ButtonData("day_night_cycle",      "day_night_cycle_description",      "WorldSphereMod/SkyBox",        Core.savedSettings.DayNightCycle,       TogglePhase),
                new ButtonData("weather_rain",          "weather_rain_description",         "WorldSphereMod/ModIcon",       Core.savedSettings.WeatherRain,           TogglePhase),
                new ButtonData("weather_snow",          "weather_snow_description",         "WorldSphereMod/ModIcon",       Core.savedSettings.WeatherSnow,           TogglePhase),
                new ButtonData("weather_lightning",     "weather_lightning_description",    "WorldSphereMod/ModIcon",       Core.savedSettings.WeatherLightning,      TogglePhase),
                new ButtonData("post_fx",              "post_fx_description",              "WorldSphereMod/ModIcon",       Core.savedSettings.PostFX,              TogglePhase),
                new ButtonData("particle_effects",     "particle_effects_description",     "WorldSphereMod/Logo",          Core.savedSettings.ParticleEffects,     TogglePhase),
                new ButtonData("sanity_cube",           "sanity_cube_description",           "WorldSphereMod/ModIcon",       Core.savedSettings.DebugSanityCube,     ToggleDebugSanityCube),
            });

            CreateButton("Open Sprites", "WorldSphereMod/ModIcon", OpenSprites);

            // Phase 10 / R&D QoL: ProfilerDump toggle (also drives the in-game
            // RuntimeStatsOverlay since the overlay's OnGUI gates on the same
            // flag) and a destructive Reset-to-defaults action.
            CreateToggleButton("ProfileMode", "WorldSphereMod/ModIcon", "profile_mode", "profile_mode_description", ToggleProfileMode, Core.savedSettings.ProfilerDump);
            CreateButton("Reset Defaults", "WorldSphereMod/ModIcon", ResetToDefaults);
        }

        static void TogglePhase(string phaseToggleId)
        {
            if (!TryResolvePhaseToggleField(phaseToggleId, out FieldInfo? settingField))
            {
                global::UnityEngine.Debug.LogWarning($"[WSM3D] Missing SavedSettings field for phase toggle '{phaseToggleId}'.");
                return;
            }

            bool nextValue = !(settingField.GetValue(Core.savedSettings) as bool? ?? false);
            settingField.SetValue(Core.savedSettings, nextValue);
            Core.ApplyPhaseToggle(settingField.Name, nextValue);
            Core.SaveSettings();

            if (!PlayerConfig.dict.ContainsKey(phaseToggleId))
            {
                PlayerConfig.dict.Add(phaseToggleId, new PlayerOptionData(phaseToggleId));
            }
            PlayerConfig.dict[phaseToggleId].boolVal = nextValue;

            if (settingField.Name == nameof(SavedSettings.BiomeBlending) && Core.IsWorld3D)
            {
                Core.Sphere.RefreshColors();
            }
        }

        static void EnsurePhasesWindowAutoCloseHook()
        {
            if (_isPhasesWindowSuppressionHooked)
            {
                return;
            }

            _isPhasesWindowSuppressionHooked = true;
            MapBox.on_world_loaded += SuppressPhasesWindowOnWorldLoad;
        }

        static void SuppressPhasesWindowOnWorldLoad()
        {
            try
            {
                SuppressPhasesWindow();
            }
            catch (System.Exception ex)
            {
                global::UnityEngine.Debug.LogWarning($"[WSM3D] Failed to suppress 3D Phases modal on world load: {ex.Message}");
            }
            finally
            {
                MapBox.on_world_loaded -= SuppressPhasesWindowOnWorldLoad;
            }
        }

        static void SuppressPhasesWindow()
        {
            bool configChanged = false;
            if (!PlayerConfig.dict.TryGetValue(PhasesWindowId, out var optionData))
            {
                optionData = new PlayerOptionData(PhasesWindowId);
                PlayerConfig.dict.Add(PhasesWindowId, optionData);
                configChanged = true;
            }

            if (optionData.boolVal)
            {
                optionData.boolVal = false;
                configChanged = true;
            }

            if (configChanged)
            {
                PlayerConfig.saveData();
            }

            ClosePhasesWindow();
        }

        static void ClosePhasesWindow()
        {
            TryHideWindowByName(PhasesWindowId);
            TryCloseWindowViaReflection(PhasesWindowId);
        }

        static void TryHideWindowByName(string windowId)
        {
            GameObject windowRoot = GameObject.Find($"/Canvas Container Main/Canvas - Windows/windows/{windowId}");
            if (windowRoot != null)
            {
                windowRoot.SetActive(false);
            }
        }

        static void TryCloseWindowViaReflection(string windowId)
        {
            MethodInfo? hideMethod = typeof(Windows).GetMethod(
                "HideWindow",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            );

            if (hideMethod == null)
            {
                hideMethod = typeof(Windows).GetMethod(
                    "CloseWindow",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                );
            }

            if (hideMethod == null)
            {
                return;
            }

            ParameterInfo[] parameters = hideMethod.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
            {
                return;
            }

            hideMethod.Invoke(null, new object[] { windowId });
        }

        static bool TryResolvePhaseToggleField(string toggleId, out FieldInfo? settingField)
        {
            settingField = typeof(SavedSettings).GetField(toggleId);
            if (settingField != null)
            {
                return true;
            }

            string normalizedToggle = NormalizeSettingId(toggleId);
            foreach (FieldInfo field in typeof(SavedSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(bool))
                {
                    continue;
                }
                if (string.Equals(NormalizeSettingId(field.Name), normalizedToggle, System.StringComparison.OrdinalIgnoreCase))
                {
                    settingField = field;
                    return true;
                }
            }

            return false;
        }
        static string NormalizeSettingId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            System.Text.StringBuilder normalized = new System.Text.StringBuilder(id.Length);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (char.IsLetterOrDigit(c))
                {
                    normalized.Append(char.ToLowerInvariant(c));
                }
            }
            return normalized.ToString();
        }
        static void ToggleDebugSanityCube(string _)           { Core.savedSettings.DebugSanityCube     = !Core.savedSettings.DebugSanityCube;     Core.SaveSettings(); }
        static void ToggleProfileMode()
        {
            Core.savedSettings.ProfilerDump = !Core.savedSettings.ProfilerDump;
            Core.SaveSettings();
        }
        static void ResetToDefaults()
        {
            bool previousVoxelEntities = Core.savedSettings.VoxelEntities;
            bool previousProceduralBuildings = Core.savedSettings.ProceduralBuildings;
            bool previousCrossedQuadFoliage = Core.savedSettings.CrossedQuadFoliage;
            bool previousBiomeBlending = Core.savedSettings.BiomeBlending;
            bool previousMeshWater = Core.savedSettings.MeshWater;
            bool previousMountainSlopeSmoothing = Core.savedSettings.MountainSlopeSmoothing;
            bool previousHighShadows = Core.savedSettings.HighShadows;
            bool previousHdrSkybox = Core.savedSettings.HdrSkybox;
            bool previousColorGradingLut = Core.savedSettings.ColorGradingLut;
            bool previousSSAOEnabled = Core.savedSettings.SSAOEnabled;
            bool previousSSGIEnabled = Core.savedSettings.SSGIEnabled;
            bool previousSkeletalAnimation = Core.savedSettings.SkeletalAnimation;
            bool previousWorldspaceUI = Core.savedSettings.WorldspaceUI;
            bool previousWorldspaceHealth3D = Core.savedSettings.WorldspaceHealth3D;
            bool previousDayNightCycle = Core.savedSettings.DayNightCycle;
            bool previousWeatherRain = Core.savedSettings.WeatherRain;
            bool previousWeatherSnow = Core.savedSettings.WeatherSnow;
            bool previousWeatherLightning = Core.savedSettings.WeatherLightning;
            bool previousPostFX = Core.savedSettings.PostFX;
            bool previousParticleEffects = Core.savedSettings.ParticleEffects;

            Core.savedSettings = new SavedSettings();
            Core.SaveSettings();

            if (previousVoxelEntities != Core.savedSettings.VoxelEntities)               Core.ApplyPhaseToggle(nameof(SavedSettings.VoxelEntities),       Core.savedSettings.VoxelEntities);
            if (previousProceduralBuildings != Core.savedSettings.ProceduralBuildings)   Core.ApplyPhaseToggle(nameof(SavedSettings.ProceduralBuildings), Core.savedSettings.ProceduralBuildings);
            if (previousCrossedQuadFoliage != Core.savedSettings.CrossedQuadFoliage)     Core.ApplyPhaseToggle(nameof(SavedSettings.CrossedQuadFoliage),  Core.savedSettings.CrossedQuadFoliage);
            if (previousBiomeBlending != Core.savedSettings.BiomeBlending && Core.IsWorld3D) Core.Sphere.RefreshColors();
            if (previousMeshWater != Core.savedSettings.MeshWater)                       Core.ApplyPhaseToggle(nameof(SavedSettings.MeshWater),           Core.savedSettings.MeshWater);
            if (previousMountainSlopeSmoothing != Core.savedSettings.MountainSlopeSmoothing)         Core.ApplyPhaseToggle(nameof(SavedSettings.MountainSlopeSmoothing),    Core.savedSettings.MountainSlopeSmoothing);
            if (previousHighShadows != Core.savedSettings.HighShadows)                   Core.ApplyPhaseToggle(nameof(SavedSettings.HighShadows),         Core.savedSettings.HighShadows);
            if (previousHdrSkybox != Core.savedSettings.HdrSkybox)                       Core.ApplyPhaseToggle(nameof(SavedSettings.HdrSkybox),           Core.savedSettings.HdrSkybox);
            if (previousColorGradingLut != Core.savedSettings.ColorGradingLut)         Core.ApplyPhaseToggle(nameof(SavedSettings.ColorGradingLut),      Core.savedSettings.ColorGradingLut);
            if (previousSSAOEnabled != Core.savedSettings.SSAOEnabled)                 Core.ApplyPhaseToggle(nameof(SavedSettings.SSAOEnabled),          Core.savedSettings.SSAOEnabled);
            if (previousSSGIEnabled != Core.savedSettings.SSGIEnabled)                 Core.ApplyPhaseToggle(nameof(SavedSettings.SSGIEnabled),          Core.savedSettings.SSGIEnabled);
            if (previousSkeletalAnimation != Core.savedSettings.SkeletalAnimation)       Core.ApplyPhaseToggle(nameof(SavedSettings.SkeletalAnimation),   Core.savedSettings.SkeletalAnimation);
            if (previousWorldspaceUI != Core.savedSettings.WorldspaceUI)                 Core.ApplyPhaseToggle(nameof(SavedSettings.WorldspaceUI),        Core.savedSettings.WorldspaceUI);
            if (previousWorldspaceHealth3D != Core.savedSettings.WorldspaceHealth3D)     Core.ApplyPhaseToggle(nameof(SavedSettings.WorldspaceHealth3D), Core.savedSettings.WorldspaceHealth3D);
            if (previousDayNightCycle != Core.savedSettings.DayNightCycle)               Core.ApplyPhaseToggle(nameof(SavedSettings.DayNightCycle),       Core.savedSettings.DayNightCycle);
            if (previousWeatherRain != Core.savedSettings.WeatherRain)                   Core.ApplyPhaseToggle(nameof(SavedSettings.WeatherRain),         Core.savedSettings.WeatherRain);
            if (previousWeatherSnow != Core.savedSettings.WeatherSnow)                   Core.ApplyPhaseToggle(nameof(SavedSettings.WeatherSnow),         Core.savedSettings.WeatherSnow);
            if (previousWeatherLightning != Core.savedSettings.WeatherLightning)         Core.ApplyPhaseToggle(nameof(SavedSettings.WeatherLightning),    Core.savedSettings.WeatherLightning);
            if (previousPostFX != Core.savedSettings.PostFX)                             Core.ApplyPhaseToggle(nameof(SavedSettings.PostFX),              Core.savedSettings.PostFX);
            if (previousParticleEffects != Core.savedSettings.ParticleEffects)           Core.ApplyPhaseToggle(nameof(SavedSettings.ParticleEffects),     Core.savedSettings.ParticleEffects);

            UnityEngine.Debug.Log("[WSM3D] SavedSettings reset to defaults. Restart recommended for full effect.");
        }
        static void OpenSprites()
        {
            Application.OpenURL("file://" + Mod.ModDirectory + "/GameResources/WorldSphereMod");
        }
        static Dictionary<string, int> WorldShapes = new Dictionary<string, int>()
        {
            { "cylindrical_shape", 0 },
            { "flat_shape", 1 }
        };
        static void PerlinNoise(string ID)
        {
            Core.savedSettings.PerlinNoise = !Core.savedSettings.PerlinNoise;
            Core.SaveSettings();
        }
        static void UpsideDown(string ID)
        {
            Core.savedSettings.UpsideDownMovement = !Core.savedSettings.UpsideDownMovement;
            Core.SaveSettings();
        }
        static void SetShape(string ID)
        {
            Core.savedSettings.CurrentShape = WorldShapes[ID];
            foreach(string shape in WorldShapes.Keys)
            {
                if(shape != ID)
                {
                    PlayerOptionData tData = PlayerConfig.dict[shape];
                    tData.boolVal = false;
                }
                PowerButtonSelector.instance.checkToggleIcons();
            }
            Core.SaveSettings();
        }
        static void Toggle3D()
        {
            Core.savedSettings.Is3D = !Core.savedSettings.Is3D;
            Core.SaveSettings();
        }
        static void ToggleRotations(string _)
        {
            Core.savedSettings.RotateStuffToCamera = !Core.savedSettings.RotateStuffToCamera;
            Core.SaveSettings();
        }
        static void ToggleBuildingStyleProcgen(string _)
        {
            Core.savedSettings.BuildingStyleProcgen = !Core.savedSettings.BuildingStyleProcgen;
            Core.SaveSettings();
        }
        static void ToggleFirtPerson(string _)
        {
            Core.savedSettings.FirstPerson = !Core.savedSettings.FirstPerson;
            Core.SaveSettings();
        }
        static void ToggleRotateToWorld(string _)
        {
            Core.savedSettings.CameraRotatesWithWorld = !Core.savedSettings.CameraRotatesWithWorld;
            Core.SaveSettings();
        }
        static void ToggleCamera(string _)
        {
            Core.savedSettings.InvertedCameraMovement = !Core.savedSettings.InvertedCameraMovement;
            Core.SaveSettings();
        }
        #region Buttons
        static PowerWindow CreateWindowButton(string ID, string IconPath, string WindowDescription, List<ButtonData> Buttons)
        {
            WindowManager.CreateWindow(ID, WindowDescription, Buttons);
            CreateButton(ID, IconPath, delegate () { WindowManager.OpenWindow(ID); });
            return WindowManager.windows[ID];
        }
        static void CreateButton(string ID, string IconPath, UnityAction Action)
        {
            PowerButton button = PowerButtonCreator.CreateSimpleButton(ID, Action, SafeLoadSprite(IconPath));
            PowerButtonCreator.AddButtonToTab(button, Tab);
        }
        static void CreateToggleButton(string ID, string IconPath, string name, string Description, UnityAction toggleAction, bool Enabled)
        {
            GodPower power = new GodPower()
            {
                id = ID,
                name = name,
                toggle_name = ID,
                toggle_action = delegate
                {
                    toggleAction();
                    PlayerConfig.dict[ID].boolVal = !PlayerConfig.dict[ID].boolVal;
                    PowerButtonSelector.instance.checkToggleIcons();
                }
            };
            SetGodPowerSprite(ref power, IconPath);
            AssetManager.powers.add(power);
            if (!PlayerConfig.dict.ContainsKey(ID))
            {
                PlayerConfig.dict.Add(ID, new PlayerOptionData(ID));
            }
            var Button = PowerButtonCreator.CreateToggleButton(
                ID,
                SafeLoadSprite(IconPath),
                null,
                default,
                true
            );
            AssetManager.options_library.add(new OptionAsset()
            {
                id = ID
            });
            PowerButtonCreator.AddButtonToTab(Button, Tab);
            // PlayerConfig.dict.Add() sets boolVal=false by default.
            // Set to match the Enabled parameter passed in — without this,
            // 'Enabled=true' phases came up disabled after every game launch
            // because PlayerConfig.dict shadowed SavedSettings (this is the
            // 'bridge POST after each launch' workaround we documented at
            // docs/journeys/scratch/all-phases-enabled-state.md).
            PlayerConfig.dict[ID].boolVal = Enabled;
            // Mirror into SavedSettings via reflection so phase code agrees.
            try
            {
                var field = typeof(SavedSettings).GetField(ID);
                if (field != null && field.FieldType == typeof(bool) && Core.savedSettings != null)
                {
                    field.SetValue(Core.savedSettings, Enabled);
                }
            }
            catch { }
            PowerButtonSelector.instance.checkToggleIcons();
        }
      }
        #endregion
    static class WindowManager
    {
        public static Dictionary<string, PowerWindow> windows = new Dictionary<string, PowerWindow>();
        public static void CreateWindow(string id, string title, List<ButtonData> Buttons)
        {
            ScrollWindow window;
            GameObject content;
            window = WindowCreator.CreateEmptyWindow(id, title);

            GameObject scrollView = GameObject.Find($"/Canvas Container Main/Canvas - Windows/windows/{window.name}/Background/Scroll View");
            content = GameObject.Find($"/Canvas Container Main/Canvas - Windows/windows/{window.name}/Background/Scroll View/Viewport/Content");
            if (scrollView == null || content == null)
            {
                global::UnityEngine.Debug.LogWarning($"[WSM3D] WindowManager: failed to create window {id}; scroll/content path missing");
                return;
            }
            var powerWindow = scrollView.AddComponent<PowerWindow>();
            windows.Add(id, powerWindow);
            powerWindow.init(id, content, Buttons);
            scrollView.gameObject.SetActive(true);
        }
        public static void OpenWindow(string ID)
        {
            windows[ID].openWindow();
        }
    }
    class PowerWindow : MonoBehaviour
    {
        public GameObject Object;
        string ID;
        public void init(string id, GameObject content, List<ButtonData> Buttons)
        {
            ID = id;
            Object = content;
            if (Object == null)
            {
                global::UnityEngine.Debug.LogWarning("[WSM3D] PowerWindow.init: content GameObject is null/destroyed for id=" + id + " — skipping layout setup");
                return;
            }
            VerticalLayoutGroup layoutGroup = Object.AddComponent<VerticalLayoutGroup>();
            if (layoutGroup == null)
            {
                global::UnityEngine.Debug.LogWarning("[WSM3D] PowerWindow.init: AddComponent<VerticalLayoutGroup> returned null for id=" + id);
                return;
            }
            layoutGroup.childControlHeight = false;
            layoutGroup.childControlWidth = false;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childForceExpandWidth = false;
            layoutGroup.childScaleHeight = true;
            layoutGroup.childScaleWidth = true;
            layoutGroup.childAlignment = TextAnchor.UpperCenter;
            layoutGroup.spacing = 50;
            LoadInputOptions(Buttons);
        }
        public void openWindow()
        {
            Windows.ShowWindow(ID);
        }
        static void toggleOption(string pPower)
        {
            GodPower godPower2 = AssetManager.powers.get(pPower);
            WorldTip.instance.showToolbarText(godPower2);
            if (!PlayerConfig.dict.TryGetValue(godPower2.toggle_name, out var value2))
            {
                value2 = new PlayerOptionData(godPower2.toggle_name)
                {
                    boolVal = false
                };
                PlayerConfig.instance.data.add(value2);
            }

            value2.boolVal = true;
            if (value2.boolVal && godPower2.map_modes_switch)
            {
                PowerLibrary.disableAllOtherMapModes(pPower);
            }

            PlayerConfig.saveData();
        }
        private void LoadInputOptions(List<ButtonData> Buttons)
        {
            Object.GetComponent<RectTransform>().sizeDelta += new Vector2(0, Buttons.Count * 125);
            foreach (var data in Buttons)
            {
                GodPower power = new GodPower()
                {
                    id = data.Name,
                    name = data.Name,
                    toggle_name = data.Name,
                    toggle_action = data.Action
                };
                WorldSphereTab.SetGodPowerSprite(ref power, data.IconPath);
                AssetManager.powers.add(power);
                if (!data.CanBeFalse)
                {
                    power.toggle_action = (PowerToggleAction)System.Delegate.Combine(power.toggle_action, new PowerToggleAction(toggleOption));
                }
                if (!PlayerConfig.dict.ContainsKey(data.Name))
                {
                    PlayerConfig.dict.Add(data.Name, new PlayerOptionData(data.Name));
                }
                AssetManager.options_library.add(new OptionAsset()
                {
                    id = data.Name
                });
                PowerButton activeButton = PowerButtonCreator.CreateToggleButton(
                    $"{data.Name}",
                    WorldSphereTab.SafeLoadSprite(data.IconPath),
                    Object.transform,
                    default,
                    !data.CanBeFalse
                );
                PlayerConfig.dict[data.Name].boolVal = data.IsActive;
                activeButton.gameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(64, 64);
                if (ID == "phases_window")
                {
                    WorldSphereTab.addText(ID, LM.Get(data.Name), activeButton.gameObject, 10, new Vector3(0, -40, 0), new Vector2(28, 24));
                }
            }
            PowerButtonSelector.instance.checkToggleIcons();
        }
    }
}




" to contain "TogglePhase_VoxelEntities" because WorldSphereTab must define TogglePhase_VoxelEntities to wire the phase toggle.
  Stack Trace:
     at FluentAssertions.Primitives.StringAssertions`1.Contain(String expected, String because, Object[] becauseArgs)
   at SourceContentInvariantsTests.WorldSphereTab_cs_contains_all_eleven_TogglePhase_handlers() in C:\Users\koosh\Dev\WorldSphereMod\tests\WorldSphereMod.Tests.E2E\SourceContentInvariantsTests.cs:line 144
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:01.03]     SourceContentInvariantsTests.VoxelRender_cs_EnsureMaterial_excludes_default_sprites_and_hidden_colored [FAIL]
  Failed SourceContentInvariantsTests.VoxelRender_cs_EnsureMaterial_excludes_default_sprites_and_hidden_colored [3 ms]
  Error Message:
   Did not expect voxelRender "using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using WorldSphereMod.Textures;
using WorldSphereMod.NewCamera;
using Debug = UnityEngine.Debug;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// Phase 1 wiring. Sits on top of <see cref="VoxelMeshCache"/> + <see cref="MeshInstanceBatcher"/>
    /// and provides the two integration points the rest of the mod needs:
    ///   • <see cref="EnsureMaterial"/> — lazy material resolution (no shader asset shipped yet).
    ///   • <see cref="Submit"/> / <see cref="Flush"/> — the per-frame submission API.
    ///
    /// The actual per-actor / per-building submission happens in this file's Harmony
    /// patches as a Postfix on the existing render-data calculation passes. They run
    /// AFTER the upstream Prefix has populated <c>render_data</c>, then walk it,
    /// emit voxel meshes, and finally suppress the upstream sprite render by clearing
    /// <c>has_normal_render</c> for the actors we drew in 3D.
    ///
    /// Gated behind <see cref="SavedSettings.VoxelEntities"/>. Default off during
    /// alpha — flip in the in-game settings tab once a tester confirms it renders.
    /// </summary>
    public static class VoxelRender
    {
        const float BuildingMaxScale = 3.0f;
        static Material? _material;
        static bool _materialAttempted;
        static bool _materialProbeLogged;
        static bool _materialDebugLogged;
        static bool _firstActorPosLogged;
        static int _actorVoxelColorSampleCount;
        static bool _actorVoxelDiagnosticLogged;
        static bool _actorImpostorDiagnosticLogged;
        static bool _actorSkeletalDiagnosticLogged;
        static readonly List<Vector3> _actorVoxelSubmitTranslations = new(5);

        /// <summary>
        /// Destroy the cached material and clear the resolve-attempted latch. Call when
        /// the world reloads — static fields outlive Unity's scene teardown and the
        /// underlying Material may have been invalidated.
        /// TODO: wire from a world-reload Postfix once one exists in Core. Until then
        /// only matters across multiple in-session world generations.
        /// </summary>
        public static void Reset()
        {
            if (_material != null) Object.Destroy(_material);
            _material = null;
            _materialAttempted = false;
            _materialProbeLogged = false;
            SanityTestCube.Reset();
            _materialDebugLogged = false;
            _firstActorPosLogged = false;
            _actorVoxelDiagnosticLogged = false;
            _actorVoxelColorSampleCount = 0;
            _actorImpostorDiagnosticLogged = false;
            _actorSkeletalDiagnosticLogged = false;
            _actorVoxelSubmitTranslations.Clear();
        }

        /// <summary>
        /// Resolve a material capable of rendering the voxel mesh's per-vertex colors.
        /// Walks a fallback chain of Unity built-in shaders so we don't need to ship a
        /// new shader asset in Phase 1 (Phase 5 introduces VoxelLit.shader and a real
        /// lit + shadow-casting material via the AssetBundle).
        /// </summary>
        public static bool EnsureMaterial()
        {
                if (_materialAttempted || _material != null)
                {
                    if ((MeshInstanceBatcher.UseFallbackPath || !Core.savedSettings.UseBRG) && _material != null && _material.enableInstancing)
                    {
                        _material.enableInstancing = false;
                    }
                    return _material != null;
                }
            _materialAttempted = true;

            string[] candidates =
            {
                // Unlit/Color first: simplest possible shader, outputs solid _Color
                // with no texture sample, no alpha-test, no deferred-pipeline pass
                // ambiguity. If voxel meshes are invisible because of any of those
                // shader-side issues, Unlit/Color rules them all out — geometry
                // either renders as solid white blocks or there's a non-shader
                // problem (frustum/Flush/scale).
                "Unlit/Color",
                "Unlit/Texture",
                // Prefer Simple Lit first: it keeps per-vertex color routes active for
                // tinting while still staying in a URP-lit pipeline.
                "Universal Render Pipeline/Simple Lit",
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Particles/Unlit",
                // OPAQUE vertex-color shaders. Sprites/Default IS vertex-color aware
                // but is TRANSPARENT (renderQueue=3000), which makes voxel cubes
                // render see-through with all inner faces visible — looks like an
                // open box. Use Particles/Standard Surface or Particles/Standard Unlit
                // (which consume vertex colors as albedo) above Standard. Sprites/Default
                // kept last (after Standard) as 'visible but wrong' fallback only.
                // Sprites/Default is vertex-color aware but transparent by default.
                // We force it to alpha-cutout opaque via material properties below.
                // Order: try the more sophisticated ones first; fall through to
                // Sprites/Default+opaque-cutout as the dependable last-resort.
                "Particles/Standard Surface",
                "Particles/Standard Unlit",
                "Standard",
                "Sprites/Default",
            };
            var shaderLookup = new Dictionary<string, Shader>();
            foreach (var name in candidates)
            {
                Shader s = Shader.Find(name);
                shaderLookup[name] = s;
                if (!_materialProbeLogged)
                {
                    Debug.Log($"[WSM3D][MATERIAL] Shader probe: '{name}' {(s != null ? "FOUND" : "MISSING")}");
                }
            }
            _materialProbeLogged = true;
            // First try a custom inline opaque-vertex-color shader. Built-in
            // candidates that DON'T consume vertex colors (Standard) leave voxel
            // meshes gray/black; ones that DO are typically transparent
            // (Sprites/Default) — the open-box-see-through bug. This inline
            // shader is opaque AND consumes vertex colors as the only albedo.
            Material? inlineMat = TryCompileInlineVoxelShader();
            if (inlineMat != null)
            {
                _material = inlineMat;
                McPackLoader.ApplyToMaterial(_material);
                Debug.Log("[WSM3D] Voxel material resolved via inline 'WSM3D/OpaqueVertexColor'.");
                return true;
            }

            foreach (var name in candidates)
            {
                Shader? s = shaderLookup.TryGetValue(name, out Shader? resolved) ? resolved : null;
                if (s == null) continue;
                Material m = new Material(s) { name = "WSM3D.Voxel.Placeholder" };
                m.enableInstancing = true;
                if (MeshInstanceBatcher.UseFallbackPath || Core.savedSettings.UseBRG)
                {
                    m.enableInstancing = false;
                }
                // Force opaque alpha-cutout on transparent shaders (Sprites/Default
                // especially) so voxel cubes render with vertex colors but stop
                // showing all inner faces. ZWrite ON + One/Zero blend + AlphaTest
                // keyword + Cutoff = solid voxel pixels visible, transparent
                // pixels punched out, no see-through.
                try
                {
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    m.SetInt("_ZWrite", 1);
                    m.DisableKeyword("_ALPHABLEND_ON");
                    m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    // CRITICAL: Standard shader's alpha-test branch samples
                    // tex2D(_MainTex, uv).a × _Color.a. _MainTex is NOT set on
                    // this material → tex2D returns alpha=0 → every fragment
                    // fails the Cutoff=0.5 test → 100% invisible.
                    // Now that renderQueue is Geometry+1 (opaque pass) we don't
                    // need AlphaTest — disable the keyword so fragments aren't
                    // discarded for lacking a _MainTex they don't need.
                    m.DisableKeyword("_ALPHATEST_ON");
                    m.SetFloat("_Cutoff", 0.0f);
                    // Opaque-Geometry + 1 (queue 2001) instead of AlphaTest (2450)
                    // so voxel meshes render in the OPAQUE pass right after terrain
                    // (queue 2000). At AlphaTest queue we were rendering AFTER all
                    // transparent passes — terrain wasn't covering us but the depth
                    // buffer post-pass interactions made meshes invisible at this
                    // camera altitude. Geometry+1 = same opaque pass, just sorted
                    // after terrain so we don't z-fight ties with biome cubes.
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 1;
                }
                catch { /* shader doesn't have these props — fine */ }
                // Belt+suspenders: always set _MainTex to a 1x1 white texture and
                // _Color to white so ANY shader that samples _MainTex.a in its
                // alpha-test / opaque path gets alpha=1 (passes any cutoff). Without
                // this, Standard (and possibly URP Lit) shaders default _MainTex to
                // null → tex2D returns alpha=0 → entire mesh discarded.
                try
                {
                    m.SetTexture("_MainTex", UnityEngine.Texture2D.whiteTexture);
                    m.SetColor("_Color", UnityEngine.Color.white);
                    m.SetTexture("_BaseMap", UnityEngine.Texture2D.whiteTexture);
                    m.SetColor("_BaseColor", UnityEngine.Color.white);
                    // FORCE EMISSION = white. Standard shader is LIT — without
                    // ambient/directional light hitting these meshes, they render
                    // BLACK regardless of _Color. Emission bypasses lighting:
                    // pixels emit the _EmissionColor value directly into the
                    // framebuffer. Combined with the _MainTex=white above,
                    // every voxel renders as pure white = visible against any
                    // background.
                    // RE-ENABLE EMISSION at 0.5 brightness. User screenshot at
                    // alpha.8 close-zoom showed actors rendering BLACK (Standard
                    // shader unlit because WorldBox scene has no directional/ambient
                    // light reaching the voxel layer). Without emission they're
                    // invisible-against-grass-tile-dark. With emission=white they
                    // override per-actor color. 0.5 grey emission is the compromise:
                    // self-emit enough light to see against grass, but leave headroom
                    // for per-instance _Color tints via MaterialPropertyBlock to
                    // actually shift the visible color.
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", new UnityEngine.Color(0.6f, 0.6f, 0.6f, 1f));
                    m.globalIlluminationFlags = UnityEngine.MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                catch { }
                ConfigureVoxelMaterial(m, name);
                McPackLoader.ApplyToMaterial(m);
                LogVoxelMaterialPassDetails(m, name);
                _material = m;
                Debug.Log($"[WSM3D] Voxel material resolved via '{name}'.");
                return true;
            }
            Debug.LogWarning("[WSM3D] No usable shader found; voxel renderer disabled.");
            return false;
        }


        // Attempt to construct an inline opaque vertex-color shader at runtime.
        // Returns null if Unity refuses to compile it (older Unity versions).
        static Material? TryCompileInlineVoxelShader()
        {
            try
            {
                // First check if our custom name already exists (compiled once previously).
                Shader? existing = Shader.Find("WSM3D/OpaqueVertexColor");
                if (existing != null)
                {
                    Material inlineMaterial = new Material(existing) { name = "WSM3D.Voxel.OpaqueVertexColor" };
                    ConfigureVoxelMaterial(inlineMaterial, "WSM3D/OpaqueVertexColor");
                    McPackLoader.ApplyToMaterial(inlineMaterial);
                    return inlineMaterial;
                }
                // Unity 2022 doesn't have a public runtime ShaderLab compile API,
                // so the inline-shader path is a no-op unless a baked shader of
                // our name is shipped in an AssetBundle (Phase 5 TODO).
                return null;
            }
            catch { return null; }
        }

        static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int _smoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly int _metallicId = Shader.PropertyToID("_Metallic");
        static readonly int _cubemapId = Shader.PropertyToID("_Cubemap");
        static readonly int _cullId = Shader.PropertyToID("_Cull");

        /// <summary>
        /// Configure whichever URP path we selected for the current run:
        ///  - URP Simple Lit: supports per-vertex color tint path better than full Lit.
        ///  - URP Lit: keep this as fallback for stronger BRDF, but also set tint/roughness
        ///    and probe inputs where supported.
        ///  - URP Unlit/Particles: keep a pure unlit pipeline and only set base tint so
        ///    per-instance color multiplies as expected.
        /// </summary>
        static void ConfigureVoxelMaterial(Material material, string shaderName)
        {
            material.SetInt(_cullId, 0);

            bool isLit = shaderName == "Universal Render Pipeline/Lit" || shaderName == "Universal Render Pipeline/Simple Lit";
            bool isUnlit = shaderName == "Universal Render Pipeline/Unlit" ||
                           shaderName == "Universal Render Pipeline/Particles/Unlit";

            if (isLit)
            {
                material.SetColor(_baseColorId, Color.white);
                material.SetFloat(_smoothnessId, 0.2f);
                material.SetFloat(_metallicId, 0.0f);

                // Cubemap probe hookup is best-effort: set only when the active
                // scene skybox provides a true Cubemap texture directly.
                if (RenderSettings.skybox != null && RenderSettings.skybox.mainTexture is Cubemap skyCubemap)
                {
                    material.SetTexture(_cubemapId, skyCubemap);
                    Debug.Log("[WSM3D] Voxel material configured with skybox cubemap reflection probe.");
                }
                else
                {
                    Debug.Log("[WSM3D] Voxel material resolved without cubemap probe; using fallback ambient diffuse.");
                }
                return;
            }

            if (isUnlit)
            {
                // Unlit variants do not use metallic/smoothness in this phase; keep
                // base color at white so <see cref=\"_InstanceColor\"/> remains the
                // effective tint multiplier.
                material.SetColor(_baseColorId, Color.white);
                return;
            }

            // Keep non-URP fallbacks clean and deterministic: don't assume URP-lit property names.
            material.color = Color.white;
        }

        static void LogVoxelMaterialPassDetails(Material material, string shaderName)
        {
            if (_materialDebugLogged) return;
            _materialDebugLogged = true;

            if (material == null)
            {
                Debug.LogWarning("[WSM3D] Voxel material diagnostics skipped: material is null.");
                return;
            }

            string shaderNameSafe = material.shader != null ? material.shader.name : "<null shader>";
            string keywords = material.shaderKeywords != null && material.shaderKeywords.Length > 0
                ? string.Join(", ", material.shaderKeywords)
                : "<none>";

            string renderType = material.GetTag("RenderType", true, "<none>");
            string queueTag = material.GetTag("Queue", false, "<none>");
            Debug.Log($"[WSM3D][MATERIAL] VOXEL sourceCandidate='{shaderName}' resolvedShader='{shaderNameSafe}' passCount={material.passCount} renderQueue={material.renderQueue} renderType={renderType} queueOverride={queueTag}");
            Debug.Log($"[WSM3D][MATERIAL] VOXEL shaderKeywords=[{keywords}]");
            if (renderType == "<none>" || string.IsNullOrEmpty(renderType))
            {
                material.SetOverrideTag("RenderType", "Opaque");
                Debug.LogWarning($"[WSM3D][MATERIAL] VOXEL sourceCandidate='{shaderName}' missing RenderType; forced override to 'Opaque'. renderQueue={material.renderQueue} renderType={material.GetTag("RenderType", true, "<none>")}");
            }

            for (int pass = 0; pass < material.passCount; pass++)
            {
                string passName = material.GetPassName(pass);
                Debug.Log($"[WSM3D][MATERIAL] VOXEL pass[{pass}] name='{passName}'");
            }
        }

        /// <summary>Per-frame submission. Matrix should already include scale.</summary>
        public static bool Submit(Mesh mesh, Matrix4x4 trs, Color tint)
        {
            // Removed: if (InstancingBroken) return false. Once instancing throws,
            // MeshInstanceBatcher.Flush has a working Graphics.DrawMesh fallback path.
            // Pre-empting Submit here used to permanently disable voxel rendering after
            // the first instancing exception. Now we always submit; Flush picks the right path.
            if (_material == null && !EnsureMaterial()) return false;
            MeshInstanceBatcher.Submit(mesh, _material!, trs, tint);
            return true;
        }

        public static Material? GetResolvedMaterial()
        {
            return EnsureMaterial() ? _material : null;
        }

        /// <summary>Issue all batched draw calls. Call once per frame after submissions.</summary>
        public static void Flush()
        {
            if (_material == null) return;
            Camera flushCamera = ResolveFlushCamera();
            LogActorVoxelSubmitDiagnostics(flushCamera);

            if (!Core.savedSettings.ProfilerDump)
            {
                MeshInstanceBatcher.Flush();
                VoxelMeshCache.Tick();
                return;
            }

            var totalSw = Stopwatch.StartNew();
            var batchSw = Stopwatch.StartNew();
            MeshInstanceBatcher.Flush();
            batchSw.Stop();
            Debug.Log($"[WSM3D][PERF] VoxelRender.Flush.MeshInstanceBatcher={batchSw.Elapsed.TotalMilliseconds:F3}ms");

            var cacheSw = Stopwatch.StartNew();
            VoxelMeshCache.Tick();
            cacheSw.Stop();
            Debug.Log($"[WSM3D][PERF] VoxelRender.Flush.VoxelMeshCache.Tick={cacheSw.Elapsed.TotalMilliseconds:F3}ms");

            totalSw.Stop();
            Debug.Log($"[WSM3D][PERF] VoxelRender.Flush total={totalSw.Elapsed.TotalMilliseconds:F3}ms");
        }

        static void LogActorVoxelSubmitDiagnostics(Camera? camera)
        {
            if (_actorVoxelSubmitTranslations.Count == 0) return;

            Debug.Log($"[WSM3D][DIAG] Actor-voxel TRS.GetColumn(3) first {_actorVoxelSubmitTranslations.Count} submissions:");
            for (int i = 0; i < _actorVoxelSubmitTranslations.Count; i++)
            {
                Debug.Log($"[WSM3D][DIAG]  sample[{i}] trsPos={_actorVoxelSubmitTranslations[i]}");
            }

            LogCameraFrustumBounds(camera);
            _actorVoxelSubmitTranslations.Clear();
        }

        static void LogCameraFrustumBounds(Camera? cam)
        {
            if (cam == null) return;

            Vector3 nearBL = cam.ViewportToWorldPoint(new Vector3(0f, 0f, cam.nearClipPlane));
            Vector3 nearBR = cam.ViewportToWorldPoint(new Vector3(1f, 0f, cam.nearClipPlane));
            Vector3 nearTL = cam.ViewportToWorldPoint(new Vector3(0f, 1f, cam.nearClipPlane));
            Vector3 nearTR = cam.ViewportToWorldPoint(new Vector3(1f, 1f, cam.nearClipPlane));
            Vector3 farBL = cam.ViewportToWorldPoint(new Vector3(0f, 0f, cam.farClipPlane));
            Vector3 farBR = cam.ViewportToWorldPoint(new Vector3(1f, 0f, cam.farClipPlane));
            Vector3 farTL = cam.ViewportToWorldPoint(new Vector3(0f, 1f, cam.farClipPlane));
            Vector3 farTR = cam.ViewportToWorldPoint(new Vector3(1f, 1f, cam.farClipPlane));

            Vector3 min = Vector3.Min(Vector3.Min(nearBL, nearBR), Vector3.Min(nearTL, nearTR));
            min = Vector3.Min(min, Vector3.Min(farBL, farBR));
            min = Vector3.Min(min, Vector3.Min(farTL, farTR));
            Vector3 max = Vector3.Max(Vector3.Max(nearBL, nearBR), Vector3.Max(nearTL, nearTR));
            max = Vector3.Max(max, Vector3.Max(farBL, farBR));
            max = Vector3.Max(max, Vector3.Max(farTL, farTR));

            Debug.Log($"[WSM3D][DIAG] Camera frustum {cam.name}: pos={cam.transform.position} near={cam.nearClipPlane:F2} far={cam.farClipPlane:F2} fov={cam.fieldOfView:F2} aspect={cam.aspect:F4} ortho={cam.orthographic} orthoSize={cam.orthographicSize:F3} boundsMin={min} boundsMax={max}");
        }

        static Camera? ResolveFlushCamera()
        {
            if (CameraManager.MainCamera != null && CameraManager.MainCamera.enabled) return CameraManager.MainCamera;
            return Camera.main;
        }

        // ---------------------------------------------------------------------
        // Harmony hooks. Registered automatically via Patcher.PatchAll on the
        // existing Core.Patch() pass because [HarmonyPatch] is declared here.

        [Phase(nameof(SavedSettings.VoxelEntities))]
        [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.precalculateRenderDataParallel))]
        public static class ActorVoxelEmit
        {
            [HarmonyPostfix]
            public static void EmitVoxels(ActorManager __instance)
            {
                Tools.ClearTileHeightSmoothCache();
                if (!Core.IsWorld3D || !Core.savedSettings.VoxelEntities) return;
                if (!EnsureMaterial()) return;

                var rd = __instance.render_data;
                var arr = __instance.visible_units.array;
                int n = __instance.visible_units.count;
                for (int i = 0; i < n; i++)
                {
                    Actor a = arr[i];
                    if (a == null || a.asset == null) continue;
                    // Per-asset opt-out: the existing v1 API hands designers a way to
                    // mark assets as "perp" (ground-aligned billboard). Those keep
                    // sprite rendering for now — they tend to be flat decals (arrows,
                    // ground markers) where voxelization adds nothing.
                    if (Constants.PerpActors.ContainsKey(a.asset.id)) continue;
                    // GATE REMOVED (codex plate-78 diff): upstream may set has_normal_render=false
                    // for actors that should still get voxelized (e.g. all actors after the first
                    // created settlement per user observation). Buildings have no such gate;
                    // matching that here. If we DO need to skip, fix it post-voxelize.
                    // if (!rd.has_normal_render[i]) continue;

                    Vector3 cullPos = rd.positions[i];
                    if (cullPos.z < Constants.ZDisplacement * 0.5f)
                    {
                        cullPos = cullPos.To3DTileHeight(false);
                    }
                    float radius = 2f;
                    if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(cullPos, radius))
                    {
                        continue;
                    }
                    WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(cullPos, a.GetHashCode());

                    if (Core.savedSettings.SkeletalAnimation && tier != WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        WorldSphereMod.Rig.RigType rigType = ResolveRigType(a.asset.id);
                        if (rigType != WorldSphereMod.Rig.RigType.None)
                        {
                            Vector3 skPos = rd.positions[i];
                            Vector3 skPosBeforeLift = skPos;
                            Vector3 skRot = rd.rotations[i];
                            Vector3 skScl = rd.scales[i];
                            if (rd.flip_x_states[i]) skScl.x = -skScl.x;
                            if (skPos.z < Constants.ZDisplacement * 0.5f)
                            {
                                skPos = skPos.To3DTileHeight(false);
                            }
                            // Match the ActorVoxelEmit Y-lift so skinned actors aren't
                            // embedded inside the terrain/water voxel. SubmitSkinnedActor
                            // uses skPos as the rig root position; raise it by half the
                            // expected actor height (use scl.y * VoxelScaleMultiplier as
                            // rough actor height estimate; / 2 for center→bottom shift).
                            float skHalfHeight = Mathf.Abs(skScl.y) * Core.savedSettings.VoxelScaleMultiplier * 0.5f;
                            skPos.y += skHalfHeight;
                            LogActorSubmitDiagnostic("skeletal", ref _actorSkeletalDiagnosticLogged, a, rd.main_sprites[i], skPosBeforeLift, skPos, rd.colors[i]);
                            if (WorldSphereMod.Rig.RigDriver.SubmitSkinnedActor(
                                    a, skPos, Quaternion.Euler(0f, skRot.y, 0f), skScl, rd.colors[i], rigType))
                            {
                                rd.has_normal_render[i] = false;
                            }
                            continue;
                        }
                    }

                    Sprite sp = rd.main_sprites[i];
                    if (sp == null) continue;

                    if (tier == WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        bool submitted = false;
                        Mesh? im = WorldSphereMod.LOD.ImpostorBillboard.GetOrCreate(sp);
                        Material? imMat = WorldSphereMod.LOD.ImpostorBillboard.GetMaterial();
                        if (im == null || im.vertexCount == 0 || imMat == null) continue;
                        Vector3 imPos = rd.positions[i];
                        Vector3 imPosBeforeLift = imPos;
                        Vector3 imScl = rd.scales[i];
                        if (rd.flip_x_states[i]) imScl.x = -imScl.x;
                        if (imPos.z < Constants.ZDisplacement * 0.5f)
                        {
                            imPos = imPos.To3DTileHeight(false);
                        }
                        LogActorSubmitDiagnostic("impostor", ref _actorImpostorDiagnosticLogged, a, sp, imPosBeforeLift, imPos, rd.colors[i]);
                        Quaternion br = WorldSphereMod.LOD.ImpostorBillboard.GetFacingRotation(imPos);
                        Matrix4x4 imTrs = Matrix4x4.TRS(imPos, br, imScl);
                        MeshInstanceBatcher.Submit(im, imMat, imTrs, Color.white);
                        submitted = true;
                        if (submitted)
                        {
                            rd.has_normal_render[i] = false;
                        }
                        continue;
                    }

                    Mesh m = VoxelMeshCache.Get(sp, -1, true);
                    if (m == null || m.vertexCount == 0) continue;

                    Vector3 pos = rd.positions[i];
                    Vector3 posBeforeLift = pos;
                    if (pos.z < Constants.ZDisplacement * 0.5f)
                    {
                        pos = pos.To3DTileHeight(false);
                    }
                    LogActorSubmitDiagnostic("voxel", ref _actorVoxelDiagnosticLogged, a, sp, posBeforeLift, pos, rd.colors[i]);
                    SanityTestCube.CaptureFirstActorPos(pos);
                    Vector3 rot = rd.rotations[i];
                    Vector3 scl = rd.scales[i];
                    if (rd.flip_x_states[i]) scl.x = -scl.x;
                    scl.z = scl.x;
                    scl *= Core.savedSettings.VoxelScaleMultiplier;
                    // Lift the mesh CENTER up by half the world-space mesh height so the
                    // mesh BOTTOM sits ON the terrain surface instead of being embedded
                    // inside the terrain/water voxel cube (which sits at y~2-3, exactly
                    // where Tools.To3DTileHeight(false) puts the actor center). Without
                    // this, half the actor mesh is hidden inside the cube and at
                    // strategy-zoom altitudes it reads as 100% invisible.
                    float halfHeight = m.bounds.size.y * 0.5f * scl.y;
                    pos.y += halfHeight;
                    LogFirstActorPos(posBeforeLift, pos, scl);
                    // Z/X axes encode sprite-billboard lean; on a 3D mesh they topple the body. Yaw only here; lean returns in Phase 6 as a spine-bone tilt.
                    Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, rot.y, 0f), scl);
                    RecordActorVoxelTrs(trs);
                    // Hide the sprite quad for this actor — we drew the 3D mesh instead.
                    if (Submit(m, trs, Color.white))
                    {
                        rd.has_normal_render[i] = false;
                        TraceActorColorSample("voxel", i, rd.colors[i], a, sp, posBeforeLift, pos, rot, scl);
                    }
                }
            }

            static WorldSphereMod.Rig.RigType ResolveRigType(string assetId)
            {
                return Constants.ResolveActorRig(assetId);
            }

            static void LogActorSubmitDiagnostic(string path, ref bool logged, Actor actor, Sprite? sprite, Vector3 beforeLift, Vector3 afterLift, Color tint)
            {
                if (logged) return;
                logged = true;
                string assetId = actor != null && actor.asset != null ? actor.asset.id : "<null>";
                string spriteName = sprite != null ? sprite.name : "<null>";
                Debug.Log($"[WSM3D] Actor {path} submit sample asset={assetId} sprite={spriteName} posBeforeLift={beforeLift} posAfterLift={afterLift} color={tint} alpha={tint.a}");
            }

            static void TraceActorColorSample(
                string path,
                int index,
                Color tint,
                Actor actor,
                Sprite? sprite,
                Vector3 rawPos,
                Vector3 liftedPos,
                Vector3 rotation,
                Vector3 scale)
            {
                if (_actorVoxelColorSampleCount >= 3) return;
                if (path != "voxel") return;

                _actorVoxelColorSampleCount++;
                string actorId = actor != null && actor.asset != null ? actor.asset.id : "<null>";
                string spriteName = sprite != null ? sprite.name : "<null>";
                Debug.Log($"[WSM3D][DIAG] Actor voxel color sample {_actorVoxelColorSampleCount}/3 asset={actorId} sprite={spriteName} index={index} rawPos={rawPos} liftedPos={liftedPos} rotY={rotation.y:F3} scale={scale} color={tint}");
            }

            static void LogFirstActorPos(Vector3 rawPos, Vector3 liftedPos, Vector3 scl)
            {
                if (_firstActorPosLogged) return;
                _firstActorPosLogged = true;
                Debug.Log($"[WSM3D] First-actor pos: raw={rawPos}, lifted={liftedPos}, scl={scl}");
            }

            static void RecordActorVoxelTrs(Matrix4x4 trs)
            {
                if (_actorVoxelSubmitTranslations.Count >= 5) return;
                Vector4 pos = trs.GetColumn(3);
                _actorVoxelSubmitTranslations.Add(new Vector3(pos.x, pos.y, pos.z));
            }
        }

        // Phase 1 fallback for buildings. Phase 2's procgen building meshes override
        // this when SavedSettings.ProceduralBuildings flips on; until then, when the
        // player turns Voxel Entities on, voxelizing the building sprite is the best
        // we can do for 3D buildings without procgen.
        [Phase(nameof(SavedSettings.VoxelEntities))]
        [HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.precalculateRenderDataParallel))]
        public static class BuildingVoxelEmit
        {
            static bool _buildingVoxelEmitSubmitLogged;

            [HarmonyPostfix]
            public static void EmitVoxels(BuildingManager __instance)
            {
                if (!Core.IsWorld3D || !Core.savedSettings.VoxelEntities) return;
                if (Core.savedSettings.ProceduralBuildings) return;
                if (!EnsureMaterial()) return;

                var rd = __instance.render_data;
                var arr = __instance._array_visible_buildings;
                int n = __instance._visible_buildings_count;
                for (int i = 0; i < n; i++)
                {
                    Building b = arr[i];
                    if (b == null || b.asset == null) continue;
                    if (Constants.PerpBuildings.ContainsKey(b.asset.id)) continue;

                    Vector3 cullPos = rd.positions[i];
                    if (cullPos.z < Constants.ZDisplacement * 0.5f)
                    {
                        cullPos = cullPos.To3DTileHeight(false);
                    }
                    float radius = 3f;
                    if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(cullPos, radius))
                    {
                        continue;
                    }
                    WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(cullPos, b.GetHashCode());

                    Sprite sp = rd.main_sprites[i];
                    if (sp == null) continue;

                    if (tier == WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        bool submitted = false;
                        Mesh? im = WorldSphereMod.LOD.ImpostorBillboard.GetOrCreate(sp);
                        Material? imMat = WorldSphereMod.LOD.ImpostorBillboard.GetMaterial();
                        // Impostor mesh build failed: fall through to vanilla
                        // sprite (don't zero scales — that's the "hide the
                        // sprite because we drew our own mesh" path, which
                        // we didn't actually do here).
                        if (im == null || im.vertexCount == 0 || imMat == null) continue;
                        Vector3 imPos = rd.positions[i];
                        Vector3 imScl = rd.scales[i];
                        if (rd.flip_x_states[i]) imScl.x = -imScl.x;
                        if (imPos.z < Constants.ZDisplacement * 0.5f)
                        {
                            imPos = imPos.To3DTileHeight(false);
                        }
                        Quaternion br = WorldSphereMod.LOD.ImpostorBillboard.GetFacingRotation(imPos);
                        Matrix4x4 imTrs = Matrix4x4.TRS(imPos, br, imScl);
                        MeshInstanceBatcher.Submit(im, imMat, imTrs, Color.white);
                        submitted = true;
                        if (submitted)
                        {
                            rd.scales[i] = Vector3.zero;
                        }
                        continue;
                    }

                    Mesh m = VoxelMeshCache.Get(sp);
                    if (m == null || m.vertexCount == 0) continue;

                    Vector3 pos = rd.positions[i];
                    if (pos.z < Constants.ZDisplacement * 0.5f)
                    {
                        pos = pos.To3DTileHeight(false);
                    }
                    Vector3 rot = rd.rotations[i];
                    Vector3 scl = rd.scales[i];
                    if (rd.flip_x_states[i]) scl.x = -scl.x;
                    scl.z = scl.x;
                    // Lift mesh center up by half world-space height (same fix as
                    // ActorVoxelEmit): without it, mesh center sits at To3DTileHeight,
                    // which embeds half the mesh inside the terrain/foundation voxel.
                    scl *= Core.savedSettings.VoxelScaleMultiplier;
                    // Clamp building voxel sprite height to prevent excessive vertical scale
                    // (e.g. 5-10 px * 16 = 80-160 uu).
                    scl.x = Mathf.Sign(scl.x) * Mathf.Min(Mathf.Abs(scl.x), BuildingMaxScale);
                    scl.y = Mathf.Min(scl.y, BuildingMaxScale);
                    scl.z = Mathf.Min(scl.z, BuildingMaxScale);
                    float bldHalfHeight = m.bounds.size.y * 0.5f * scl.y;
                    pos.y += bldHalfHeight;
                    Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, rot.y, 0f), scl);
                    if (!_buildingVoxelEmitSubmitLogged)
                    {
                        _buildingVoxelEmitSubmitLogged = true;
                        Debug.Log($"[WSM3D] BuildingVoxelEmit first submit mesh.bounds.size={m.bounds.size}, scaledBoundsSize={Vector3.Scale(m.bounds.size, scl)}");
                    }
                    // BuildingRenderData has no has_normal_render. Suppressing via scales[i]=0
                    // hides the sprite quad without nulling main_sprites (downstream
                    // calculateColoredSprite() chokes on null). Shadow sprite still draws as a
                    // ground decal under the 3D mesh — fine until Phase 5 ships real shadows.
                    if (Submit(m, trs, Color.white))
                    {
                        rd.scales[i] = Vector3.zero;
                    }
                }
            }
        }
    }

    /// <summary>
    /// MonoBehaviour driver that calls <see cref="VoxelRender.Flush"/> once per frame
    /// in <c>LateUpdate</c>, after every render-data calculation has run. Attached to
    /// the mod's root GameObject in <c>Core.Init</c>.
    /// </summary>
    public sealed class VoxelFrameDriver : MonoBehaviour
    {
        static bool _lastSkeletalState = false;
        const float kCameraLookupInterval = 0.05f;
        const int kPerfSampleWindowFrames = 60;
        float _nextCameraLookup = 0f;
        static int _perfFrameCounter;
        static float _perfDeltaTimeSum;

        void OnEnable()
        {
            // Survive scene transitions — re-parent to a dedicated root GameObject
            // owned by WSM3D + apply DontDestroyOnLoad. If we just call DDoL on
            // the current parent, Unity silently no-ops for non-root nodes.
            try
            {
                if (transform.parent != null) transform.SetParent(null, worldPositionStays: false);
                UnityEngine.Object.DontDestroyOnLoad(gameObject);
            } catch { }
            MeshInstanceBatcher.SetMainThread();
            // Force per-instance fallback only when explicitly requested. The
            // Standard material path now keeps INSTANCING_ON in sync before DrawMeshInstanced.
            MeshInstanceBatcher.SetFallbackPath(Core.savedSettings != null && Core.savedSettings.ForceFallbackDrawPath);
            WorldSphereMod.Lighting.SunDriver.BindMainCamera(CameraManager.MainCamera);
        }

        static float _telemetryLastTime;
        static int _instancingTelemetryFrame;
        void LateUpdate()
        {
            if (!Core.IsWorld3D) return;

            float deltaTime = Time.deltaTime;
            _perfFrameCounter++;
            _perfDeltaTimeSum += deltaTime;
            if (_perfFrameCounter >= kPerfSampleWindowFrames)
            {
                float avgFrameTime = _perfDeltaTimeSum / kPerfSampleWindowFrames;
                float avgFps = avgFrameTime > 0f ? 1f / avgFrameTime : 0f;
                Debug.Log($"[WSM3D][Perf] frameDeltaMs={deltaTime * 1000f:F2} avg60FrameDeltaMs={avgFrameTime * 1000f:F2} avg60Fps={avgFps:F1}");
                _perfFrameCounter = 0;
                _perfDeltaTimeSum = 0f;
            }

            _instancingTelemetryFrame++;
            if (_instancingTelemetryFrame >= 60)
            {
                _instancingTelemetryFrame = 0;
                Debug.Log($"[WSM3D][Telemetry] InstancingEfficiency={MeshInstanceBatcher.InstancingEfficiency:F4} FrameBucketCount={MeshInstanceBatcher.FrameBucketCount} FrameInstances={MeshInstanceBatcher.FrameInstances}");
            }

            // Log-based telemetry every 10s — bypasses bridge for steady-state observability
            // even when bridge is in scene-transition known-issue state.
            float now = Time.realtimeSinceStartup;
            if (now - _telemetryLastTime > 10f)
            {
                _telemetryLastTime = now;
                Debug.Log($"[WSM3D][Telemetry] frameMs={Time.unscaledDeltaTime*1000:F2} drawCalls={MeshInstanceBatcher.FrameDrawCalls} instances={MeshInstanceBatcher.FrameInstances} cacheSize={VoxelMeshCache.Count} cacheHits={VoxelMeshCache.HitCount} cacheMisses={VoxelMeshCache.MissCount} gcMB={(System.GC.GetTotalMemory(false) / 1048576f):F1}");
            }

            WorldSphereMod.Voxel.VoxelMeshCache.BeginFrame();
            WorldSphereMod.LOD.ImpostorBillboard.Tick();

            bool hasRenderWork = Core.savedSettings.VoxelEntities || Core.savedSettings.ProceduralBuildings || Core.savedSettings.CrossedQuadFoliage;
            if (hasRenderWork)
            {
                WorldSphereMod.LOD.FrustumCuller.UpdatePlanes();
            }

            WorldSphereMod.Rig.RigCache.Tick();
            WorldSphereMod.Rig.RigCache.DrainPendingDestroy();
            if (Core.savedSettings.SkeletalAnimation)
            {
                if (_lastSkeletalState == false)
                {
                    _lastSkeletalState = true;
                }
                WorldSphereMod.Rig.RigDriver.Update();
            }
            else if (_lastSkeletalState)
            {
                // Edge transition true->false. Dispose stale SkinnedMeshRenderer
                // instances ONCE so they don't animate with garbage bone matrices
                // (dragonfly-legs bug). Per-frame Clear would freeze the load.
                WorldSphereMod.Rig.RigDriver.Clear();
                _lastSkeletalState = false;
            }

            if (MeshInstanceBatcher.HasPendingSubmissions)
            {
                VoxelRender.Flush();
                VoxelMeshCache.DrainPendingDestroy();
            }

            WorldSphereMod.Voxel.VoxelMeshCache.PumpQueuedBuilds(1);
            WorldSphereMod.Voxel.VoxelMeshCache.DrainCompletedBuilds(8);

            if (Core.savedSettings.DebugSanityCube)
            {
                SanityTestCube.Draw();
            }

            if (Core.savedSettings.ProceduralBuildings)
            {
                WorldSphereMod.ProcGen.ProcGenCache.DrainPendingDestroy();
            }

            if (Core.savedSettings.CrossedQuadFoliage)
            {
                WorldSphereMod.Foliage.CrossedQuadMeshCache.DrainPendingDestroy();
            }

            if (Core.savedSettings.MeshWater)
            {
                WorldSphereMod.Water.WaterRender.UpdateLifecycle();
            }

            WorldSphereMod.Terrain.MountainSlopeSurface.EnsureActive();

            if (Time.time >= _nextCameraLookup)
            {
                WorldSphereMod.Lighting.SunDriver.BindMainCamera(CameraManager.MainCamera);
                _nextCameraLookup = Time.time + kCameraLookupInterval;
            }

            WorldSphereMod.Lighting.SunDriver.Update();

            WorldSphereMod.Fx.DecalPool.Tick();

            WorldSphereMod.Fx.Environmental.Tick();

            WorldSphereMod.Fx.PostFxController.ApplySetting(Core.savedSettings.PostFX);
        }
    }
}
" to contain ""Sprites/Default"" because VoxelRender.EnsureMaterial must exclude Sprites/Default — it's a dummy fallback.
  Stack Trace:
     at FluentAssertions.Primitives.StringAssertions`1.NotContain(String unexpected, String because, Object[] becauseArgs)
   at SourceContentInvariantsTests.VoxelRender_cs_EnsureMaterial_excludes_default_sprites_and_hidden_colored() in C:\Users\koosh\Dev\WorldSphereMod\tests\WorldSphereMod.Tests.E2E\SourceContentInvariantsTests.cs:line 73
   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     5, Passed:    18, Skipped:     0, Total:    23, Duration: 449 ms - WorldSphereMod.Tests.E2E.dll (net8.0)
```

