using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading.Tasks;
using UnityEngine;
using WorldSphereMod.NewCamera;
using static WorldSphereMod.QuantumSprites.Manager;
namespace WorldSphereMod.QuantumSprites
{
    public static class Manager
    {
        //warning, if rotation not set beforehand, the sprite will go woosh woosh
        public static void RotateToCamera(Transform transform)
        {
            if (Core.savedSettings.RotateStuffToCamera)
            {
                Vector3 pos = transform.position;
                transform.rotation *= Tools.RotateToCamera(ref pos);
            }
        }
        public static void SetScaleAndResetRot(this QuantumSprite sprite, float pScale)
        {
            sprite.setScale(pScale);
            sprite.ForceRotation(ref Constants.Zero);
        }
        public static void RotateToCameraAtPos(this Transform transform, Vector3 pos)
        {
            if (Core.savedSettings.RotateStuffToCamera && Core.IsWorld3D)
            {
                transform.rotation *= Tools.RotateToCameraAtTile(pos.AsIntClamped());
            }
        }
        public static void ForceRotation(this GroupSpriteObject obj, ref Vector3 Rot)
        {
            obj.transform.eulerAngles = Rot;
            obj._last_angles_v3 = Rot;
        }
        public static void set(this GroupSpriteObject Object, ref Vector3 pPosition, float pScale, QuantumSpriteAsset pAsset)
        {
            if (Object._last_pos_v3.x != pPosition.x || Object._last_pos_v3.y != pPosition.y || Object._last_pos_v3.z != pPosition.z)
            {
                Object._last_pos_v2 = pPosition;
                
                Object._last_pos_v3 = pPosition;
                if (!pAsset.IsQuantumUpright())
                {
                    Object.m_transform.ToSpecialNonUpright(Object._last_pos_v3);
                }
                else
                {
                    Object.m_transform.ToSpecialUpright(Object._last_pos_v3);
                }
            }
            if (Object._last_scale_v2.x != pScale)
            {
                Object.setScale(pScale);
            }
        }
        public static void drawSocialize3D(QuantumSpriteAsset pAsset)
        {
            if (!PlayerConfig.optionBoolEnabled("talk_bubbles"))
            {
                return;
            }
            float tMax = 1f;
            double tCurTime = World.world.getCurSessionTime();
            Actor[] tArr = World.world.units.visible_units_socialize.array;
            int tLen = World.world.units.visible_units_socialize.count;
            tLen = Math.Min(tLen, 1000);
            for (int i = 0; i < tLen; i++)
            {
                Actor tActor = tArr[i];
                if (!tActor.hasTrait("mute"))
                {
                    CommunicationAsset normal = CommunicationLibrary.normal;
                    float tDiff = (float)(tCurTime - tActor.timestamp_tween_session_social);
                    if (tDiff > tMax)
                    {
                        tDiff = 1f;
                    }
                    Vector3 headOffsetPositionForFunRendering = tActor.getHeadOffsetPositionForFunRendering();
                    float tTween = iTween.easeOutCubic(0f, 1f, tDiff);
                    float tOffsetX = Randy.randomFloat(-0.03f, 0.03f);
                    float tOffsetY = Randy.randomFloat(-0.03f, 0.03f);
                    Vector2 tScale = tActor.current_scale;
                    float tX = headOffsetPositionForFunRendering.x + tOffsetX * tScale.x;
                    float tY = headOffsetPositionForFunRendering.y + tOffsetY * tScale.y;
                    Vector2 tPos = new Vector2(tX, tY);
                    tScale.y *= tTween;
                    QuantumSprite tQBubble = pAsset.group_system.getNext();
                    tQBubble.set(ref tPos, tScale.y);
                    Sprite tSpeechSprite = normal.getSpriteBubble();
                    tQBubble.setSprite(tSpeechSprite);
                    if (normal.show_topic)
                    {
                        Vector3 tPosTopic = tQBubble.m_transform.TransformPoint(0, 10f, 0.1f);
                        QuantumSprite next = pAsset.group_system.getNext();
                        next.set(ref tPosTopic, tScale.y * 0.35f);
                        next.transform.rotation = tQBubble.m_transform.rotation;
                        Sprite tTopicSprite = tActor.getSocializeTopic();
                        next.setSprite(tTopicSprite);
                    }
                }
            }
        }
        public static void setnotupright(this GroupSpriteObject Object, ref Vector2 pPosition, float pScale)
        {
            if (Object._last_pos_v3.x != pPosition.x || Object._last_pos_v3.y != pPosition.y)
            {
                Object._last_pos_v2 = pPosition;
                Object._last_pos_v3 = pPosition;
                Object.m_transform.ToSpecialNonUprightWithHeight(Object._last_pos_v3);
            }
            if (Object._last_scale_v2.x != pScale)
            {
                Object.setScale(pScale);
            }
        }
        public static bool IsQuantumUpright(this QuantumSpriteAsset pAsset)
        {
            return pAsset.id == "selected_units" || pAsset.id == "draw_building_stockpiles";
        }
        public static void DrawResourceInStockPile3D(QuantumSpriteAsset pAsset, Vector3 pMainPosition, Sprite pSprite, int pIndex, int pRow, int pColumn, ref Color pColor)
        {
            Vector3 tPos = pMainPosition;
            tPos.z -= 1;
            tPos.y += (0.58f * pRow)-5;
            tPos.x += (0.5f * pColumn)+2;
            if (pColumn % 2 != 0)
            {
                tPos.y += 0.29f;
            }
            tPos.z += 0.5f * pIndex;
            QuantumSprite quantumSprite = QuantumSpriteLibrary.drawQuantumSprite(pAsset, tPos, null, null, null, null, 1f, false, -1f);
            quantumSprite.setSprite(pSprite);
            quantumSprite.setColor(ref pColor);
        }

        public static void SetPos(this QuantumSprite sprite, ref Vector3 pPosition)
        {
            if (!Core.IsWorld3D)
            {
                sprite.setPosOnly(ref pPosition);
                return;
            }
            if (sprite._last_pos_v3.x != pPosition.x || sprite._last_pos_v3.y != pPosition.y || sprite._last_pos_v3.z != pPosition.z)
            {
                sprite._last_pos_v2 = pPosition;
                sprite._last_pos_v3 = pPosition;
                sprite.m_transform.localPosition = Tools.To3DTileHeight(pPosition, 0.1f);
            }
        }
        public static void SetProjectile(this GroupSpriteObject Obj, ref Vector3 pPosition, Projectile Projectile)
        {
            if (Obj._last_pos_v3.x != pPosition.x || Obj._last_pos_v3.y != pPosition.y || Obj._last_pos_v3.z != pPosition.z)
            {
                Obj._last_pos_v2 = pPosition;
                Obj._last_pos_v3 = pPosition;
                if (Core.IsWorld3D)
                {
                    Obj.m_transform.localPosition = Obj._last_pos_v3.To3DTileHeight(true);
                    if (!Constants.PerpProjectiles.ContainsKey(Projectile.asset.id))
                    {
                        Obj.m_transform.rotation = Tools.GetUprightRotation(pPosition.AsIntClamped());
                        RotateToCamera(Obj.m_transform);
                    }
                    else
                    {
                        Obj.m_transform.rotation = Tools.GetRotation(pPosition.AsIntClamped());
                    }
                }
                else
                {
                    Obj.m_transform.localPosition = Obj._last_pos_v3;
                }
            }

            if (Obj._last_scale_v2.x != Projectile.getCurrentScale())
            {
                Obj.setScale(Projectile.getCurrentScale());
            }
        }
    }
    public class QuantumSpritePatches
    {
        [HarmonyPatch(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawSelectedUnits))]
        [HarmonyPrefix]
        public static bool drawselectedunits()
        {
            return !(Core.IsWorld3D && ControllableUnit._unit_main != null);
        }
        [HarmonyPatch(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawResourceIconOnStockpile))]
        [HarmonyPrefix]
        private static bool drawresourceiconpatch(QuantumSpriteAsset pAsset, Vector3 pMainPosition, Sprite pSprite, int pIndex, int pRow, int pColumn, ref Color pColor)
        {
            if (Core.IsWorld3D)
            {
                DrawResourceInStockPile3D(pAsset, pMainPosition, pSprite, pIndex, pRow, pColumn, ref pColor);
                return false;
            }
            return true;
        }
        [HarmonyPatch(typeof(QuantumSpriteManager), nameof(QuantumSpriteManager.hideAll))]
        [HarmonyPostfix]
        static void ResetSprites()
        {
            foreach (QuantumSpriteAsset quantumSpriteAsset in AssetManager.quantum_sprites.list)
            {
                QuantumSpriteGroupSystem group_system = quantumSpriteAsset.group_system;
                if (group_system != null)
                {
                    foreach(QuantumSprite sprite in group_system._sprites.Where((QuantumSprite sprite) => sprite != null))
                    {
                        sprite.ForceRotation(ref Constants.Zero);
                    }
                }
            }
        }
        [HarmonyPatch(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.showLightAt))]
        [HarmonyPrefix]
        private static bool showLightAt(Vector2 pPos, Color pColor, float pScale = 1f)
        {
            QuantumSprite next = QuantumSpriteLibrary.light_areas.group_system.getNext();
            next.setnotupright(ref pPos, pScale);
            pColor.a /= 2;
            next.setColor(ref pColor);
            return false;
        }
        [HarmonyPatch(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawSocialize))]
        [HarmonyPrefix]
        private static bool drawresourceiconpatch(QuantumSpriteAsset pAsset)
        {
            if (Core.IsWorld3D)
            {
                drawSocialize3D(pAsset);
                return false;
            }
            return true;
        }
        [HarmonyPatch(typeof(GroupSpriteObject), nameof(GroupSpriteObject.setScale), new Type[] {typeof(float)})]
        [HarmonyPrefix]
        static bool setScale(GroupSpriteObject __instance, float pScale)
        {
            if (__instance._last_scale_v3.y != pScale)
            {
                __instance._last_scale_v2 = new Vector2(pScale, pScale);
                __instance._last_scale_v3 = new Vector3(pScale, pScale, 0.1f);
                __instance.m_transform.localScale = __instance._last_scale_v3;
            }
            return false;
        }
        [HarmonyPatch(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawArrowQuantumSprite))]
        [HarmonyPrefix]
        public static bool arrows(QuantumSpriteAsset pAsset, Vector3 pStart, Vector3 pEnd, ref Color pColor, City pCity, ref QuantumSpriteArrows __result)
        {
            if (!Core.IsWorld3D)
            {
                return true;
            }
            __result = patch(pAsset, pStart, pEnd, ref pColor, pCity);
            return false;
            static QuantumSpriteArrows patch(QuantumSpriteAsset pAsset, Vector3 pStart, Vector3 pEnd, ref Color pColor, City pCity)
            {
                if (pStart.x == pEnd.x && pStart.y == pEnd.y)
                {
                    return null;
                }
                float tDist = Toolbox.Dist(pStart.x, pStart.y, pEnd.x, pEnd.y);
                float tScale = pAsset.base_scale * QuantumSpriteLibrary.getCameraScaleZoomMultiplier(pAsset);
                if (pCity != null)
                {
                    tScale *= pCity.mark_scale_effect;
                }
                tDist /= tScale;
                if (tDist < (float)pAsset.line_width)
                {
                    return null;
                }
                float tAnimatedPos = QuantumSpriteManager.arrow_middle_current;
                if (!pAsset.arrow_animation)
                {
                    tAnimatedPos = 0f;
                }
                QuantumSpriteArrows tQSprite = (QuantumSpriteArrows)pAsset.group_system.getNext();
                tQSprite.spriteArrowEnd.enabled = pAsset.render_arrow_end;
                tQSprite.spriteArrowStart.enabled = pAsset.render_arrow_start;
                if (tDist < (float)(pAsset.line_width + 2))
                {
                    tQSprite.spriteArrowEnd.enabled = false;
                }
                if (tQSprite.spriteArrowEnd.enabled)
                {
                    tQSprite.spriteArrowEnd.color = pColor;
                    tQSprite.spriteArrowEnd.transform.localPosition = new Vector3(tDist, 0f, 0f);
                }
                if (tQSprite.spriteArrowStart.enabled)
                {
                    tQSprite.spriteArrowStart.color = pColor;
                }
                tQSprite.spriteArrowMiddle.color = pColor;
                Vector3 tPos = pStart;
                tPos.z = (float)pAsset.group_system.countActive() * 0.001f;
                tQSprite.transform.ToSpecialNonUprightWithHeight(tPos);
                Vector2 direct = Tools.Direction3D(pStart, pEnd);
                float tAngle = Tools.MathStuff.Angle(direct.y, direct.x) + 90;
                tQSprite.transform.rotation *= Quaternion.Euler(new Vector3(0, 0f, tAngle));
                float tSizeMiddle = tDist - tAnimatedPos;
                if (tQSprite.spriteArrowEnd.enabled)
                {
                    tSizeMiddle -= 5f;
                }
                tQSprite.spriteArrowMiddle.size = new Vector2(tSizeMiddle, (float)pAsset.line_height);
                tQSprite.spriteArrowMiddle.transform.localPosition = new Vector3(tAnimatedPos, 0f, 0f);
                tQSprite.transform.localScale = new Vector3(tScale, tScale, 1f);
                return tQSprite;
            }
        }
        [HarmonyPatch(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawSquareSelection))]
        [HarmonyPrefix]
        static bool DrawSquare(QuantumSpriteAsset pAsset)
        {
            void drawSquareSelection(QuantumSpriteAsset pAsset)
            {
                if (!World.world.player_control.square_selection_started)
                {
                    return;
                }
                float tCameraScaleZoomMultiplier = QuantumSpriteLibrary.getCameraScaleZoomMultiplier(pAsset);
                Color tColorSelection = World.world.getArchitectColor();
                Vector2 tStart = World.world.player_control.square_selection_position_current;
                Vector2 tEnd = World.world.getMousePos();
                float tWidth = tStart.x - tEnd.x;
                float tHeight = tEnd.y - tStart.y;
                float tLineSize = 0.1f * tCameraScaleZoomMultiplier;
                Color tColorMain = tColorSelection;
                tColorMain.a = 0.3f;
                QuantumSprite quantumSprite = QuantumSpriteLibrary.drawQuantumSprite(pAsset, tStart, null, null, null, null, 1f, false, -1f);
                quantumSprite.setSprite(QuantumSpriteLibrary._sprite_pixel);
                quantumSprite.transform.localScale = new Vector3(tHeight, -tWidth);
                quantumSprite.setColor(ref tColorMain);
                QuantumSprite quantumSprite2 = QuantumSpriteLibrary.drawQuantumSprite(pAsset, tStart, null, null, null, null, 1f, false, -1f);
                quantumSprite2.setSprite(QuantumSpriteLibrary._sprite_pixel);
                quantumSprite2.transform.localScale = new Vector3(tHeight, tLineSize);
                quantumSprite2.setColor(ref tColorSelection);
                QuantumSprite quantumSprite3 = QuantumSpriteLibrary.drawQuantumSprite(pAsset, tStart, null, null, null, null, 1f, false, -1f);
                quantumSprite3.setSprite(QuantumSpriteLibrary._sprite_pixel);
                quantumSprite3.transform.localScale = new Vector3(tLineSize, -tWidth);
                quantumSprite3.setColor(ref tColorSelection);
                QuantumSprite quantumSprite4 = QuantumSpriteLibrary.drawQuantumSprite(pAsset, tEnd, null, null, null, null, 1f, false, -1f);
                quantumSprite4.setSprite(QuantumSpriteLibrary._sprite_pixel);
                quantumSprite4.transform.localScale = new Vector3(-tHeight, tLineSize);
                quantumSprite4.setColor(ref tColorSelection);
                QuantumSprite quantumSprite5 = QuantumSpriteLibrary.drawQuantumSprite(pAsset, tEnd, null, null, null, null, 1f, false, -1f);
                quantumSprite5.setSprite(QuantumSpriteLibrary._sprite_pixel);
                quantumSprite5.transform.localScale = new Vector3(tLineSize, tWidth);
                quantumSprite5.setColor(ref tColorSelection);
            }
            if (Core.IsWorld3D)
            {
                drawSquareSelection(pAsset);
                return false;
            }
            return true;
        }
        [HarmonyPatch(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawProjectiles))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            CodeMatcher Matcher = new CodeMatcher(instructions, generator);
            Matcher.MatchForward(false, new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Projectile), nameof(Projectile.getCurrentScale))));
            if (Matcher.Pos < 0 || Matcher.IsInvalid)
            {
                global::UnityEngine.Debug.LogWarning("[WSM3D] QuantumSpritePatches.drawProjectiles transpiler: Projectile.getCurrentScale not found in vanilla IL — skipping");
                return instructions;
            }
            Matcher.RemoveInstruction();
            Matcher.RemoveInstruction();
            Matcher.Insert(new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(Manager), nameof(Manager.SetProjectile))));
            Matcher.MatchForward(false, new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(Transform), "set_rotation")));
            if (Matcher.Pos < 0 || Matcher.IsInvalid)
            {
                global::UnityEngine.Debug.LogWarning("[WSM3D] QuantumSpritePatches.drawProjectiles transpiler: Transform.set_rotation not found in vanilla IL — skipping second rewrite");
                return Matcher.Instructions();
            }
            Matcher.RemoveInstruction();
            Matcher.Insert(new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(Tools), nameof(Tools.AddRotation))));
            return Matcher.Instructions();
        }
        
        [HarmonyPatch(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawStatusEffectFor))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> effects(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            CodeMatcher Matcher = new CodeMatcher(instructions, generator);
            Matcher.MatchForward(false, new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(GroupSpriteObject), nameof(GroupSpriteObject.setScale), new Type[] { typeof(float) })));
            if (Matcher.Pos < 0 || Matcher.IsInvalid)
            {
                global::UnityEngine.Debug.LogWarning("[WSM3D] QuantumSpritePatches.effects transpiler: setScale not found — skipping");
                return instructions;
            }
            Matcher.RemoveInstruction();
            Matcher.Insert(new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(Manager), nameof(Manager.SetScaleAndResetRot))));
            Matcher.MatchForward(false, new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(GroupSpriteObject), nameof(GroupSpriteObject.setSharedMat))));
            if (Matcher.Pos < 0 || Matcher.IsInvalid)
            {
                global::UnityEngine.Debug.LogWarning("[WSM3D] QuantumSpritePatches.effects transpiler: setSharedMat not found — skipping second insert");
                return Matcher.Instructions();
            }
            Matcher.Insert(new CodeInstruction(OpCodes.Ldloc_S, (byte)4), new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(Component), "get_transform")), new CodeInstruction(OpCodes.Ldloc_3), new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Manager), nameof(Manager.RotateToCameraAtPos))));

            return Matcher.Instructions();
        }
        [HarmonyPatch(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawBuildings))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> buildings(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            CodeMatcher Matcher = new CodeMatcher(instructions, generator);
            Matcher.MatchForward(false, new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(GroupSpriteObject), nameof(GroupSpriteObject.setPosOnly), new Type[] {typeof(Vector3).MakeByRefType()})));
            if (Matcher.Pos < 0 || Matcher.IsInvalid)
            {
                global::UnityEngine.Debug.LogWarning("[WSM3D] QuantumSpritePatches.buildings transpiler: setPosOnly not found — skipping");
                return instructions;
            }
            Matcher.RemoveInstruction();
            Matcher.Insert(new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(Manager), nameof(Manager.SetPos))));
            return Matcher.Instructions();
        }
    }
    [HarmonyPatch(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawQuantumSprite), new Type[] { typeof(QuantumSpriteAsset), typeof(Vector3), typeof(WorldTile), typeof(Kingdom), typeof(City), typeof(BattleContainer), typeof(float), typeof(bool), typeof(float) })]
    public class MainQuantumSpritePatch
    {
        static void Prefix(QuantumSpriteAsset pAsset, ref Vector3 pPos)
        {
            if (!Core.IsWorld3D)
            {
                return;
            }
            if (pAsset.id == "highlight_cursor_zones" || pAsset.id == "square_selection")
            {
                pPos.z = 4 * Core.Sphere.HeightMult;
            }
            else
            {
                pPos.z += 1 + Tools.GetTileHeightSmooth(pPos);
            }
        }
        static void Postfix(QuantumSpriteAsset pAsset, ref QuantumSprite __result)
        {
            if (!Core.IsWorld3D)
            {
                return;
            }
            if (pAsset.id == "highlight_cursor_zones")
            {
                Vector3 size = Constants.HighlightedZoneSize;
                __result.setScale(ref size);
            }
        }
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            CodeMatcher Matcher = new CodeMatcher(instructions, generator);
            Matcher.MatchForward(false, new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(GroupSpriteObject), nameof(GroupSpriteObject.set), new Type[] { typeof(Vector3).MakeByRefType(), typeof(float) })));
            // Pattern can fail to match after vanilla IL updates — guard before
            // RemoveInstruction so we don't throw ArgumentOutOfRangeException
            // (which Harmony wraps in IL Compile Error and aborts the whole
            // Patcher.PatchAll, taking the entire WSM3D mod down with it).
            if (Matcher.Pos < 0 || Matcher.IsInvalid)
            {
                global::UnityEngine.Debug.LogWarning("[WSM3D] MainQuantumSpritePatch transpiler: GroupSpriteObject.set pattern not found in vanilla IL — skipping transpile (sprite-render Manager.set rewire disabled)");
                return instructions;
            }
            Matcher.RemoveInstruction();
            Matcher.Insert(new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(Manager), nameof(Manager.set))));
            return Matcher.Instructions();
        }
    }
    //better to patch data from the source if rotations are handled differently, or weird shit happens
    public static class SourcePatches
    {
        [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.precalculateRenderDataParallel))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.High)]
        public static void calculateactordata3D(ActorManager __instance)
        {
            if (!Core.IsWorld3D)
            {
                return;
            }
            int tTotalVisibleObjects = __instance.visible_units.count;
            if (tTotalVisibleObjects == 0) return;
            Actor[] tArray = __instance.visible_units.array;
            int tDynamicBatchSize = 256;
            int tTotalBatches = ParallelHelper.calcTotalBatches(tTotalVisibleObjects, tDynamicBatchSize);
            Parallel.For(0, tTotalBatches, World.world.parallel_options, delegate (int pBatchIndex)
            {
                int num = ParallelHelper.calculateBatchBeg(pBatchIndex, tDynamicBatchSize);
                int tIndexEnd = ParallelHelper.calculateBatchEnd(num, tDynamicBatchSize, tTotalVisibleObjects);
                for (int tIndex = num; tIndex < tIndexEnd; tIndex++)
                {
                    Actor tActor = tArray[tIndex];
                    Vector3 v = tActor.updatePos();
                    Vector3 tCurrentActorPos = Tools.To3DTileHeight(v, v.z + 0.1f);
                    Vector3 tActorRotation = tActor.Get3DRot();
                    __instance.render_data.positions[tIndex] = tCurrentActorPos;
                    __instance.render_data.rotations[tIndex] = tActorRotation;
                }
            });
        }
        //i only need to change 2 LINES OF CODE. I WOULD USE A TRANSPILER, BUT THIS FUCKASS FUNCTION USES A DELEGATE, WHICH I CANNOT FUCKING TRANSPILE
        [HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.precalculateRenderDataParallel))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.High)]
        public static void calculatebuildindata3D(BuildingManager __instance)
        {
            if (!Core.IsWorld3D)
            {
                return;
            }
            int tTotalVisibleObjects = __instance._visible_buildings_count;
            if (tTotalVisibleObjects == 0) return;
            Building[] tArrayVisibleBuildings = __instance._array_visible_buildings;
            Vector3[] tRenderPositions = __instance.render_data.positions;
            Vector3[] tRenderRotations = __instance.render_data.rotations;
            int tDynamicBatchSize = 256;
            int tTotalBatches = ParallelHelper.calcTotalBatches(tTotalVisibleObjects, tDynamicBatchSize);
            Parallel.For(0, tTotalBatches, World.world.parallel_options, delegate (int pBatchIndex)
            {
                int num = ParallelHelper.calculateBatchBeg(pBatchIndex, tDynamicBatchSize);
                int tIndexEnd = ParallelHelper.calculateBatchEnd(num, tDynamicBatchSize, tTotalVisibleObjects);
                for (int tIndex = num; tIndex < tIndexEnd; tIndex++)
                {
                    Building tBuilding = tArrayVisibleBuildings[tIndex];
                    tRenderPositions[tIndex] = tBuilding.cur_transform_position;
                    tRenderRotations[tIndex] = tBuilding.Get3DRot();
                    __instance.render_data.scales[tIndex] = tBuilding.getCurrentScale() * Core.savedSettings.BuildingSize;
                }
            });
        }
    }
}
