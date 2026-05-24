using System;
using NeoModLoader.api;
using UnityEngine;

namespace WorldSphereTester
{
    public class Tester : IMod
    {
        private static ModDeclare _declare = null!;
        private static GameObject _gameObject = null!;

        public ModDeclare GetDeclaration() => _declare;
        public GameObject GetGameObject() => _gameObject;
        public string GetUrl() => "https://github.com/MelvinShwuaner/WorldSphereMod";

        public void OnLoad(ModDeclare pModDecl, GameObject pGameObject)
        {
            _declare = pModDecl;
            _gameObject = pGameObject;
            Log("OnLoad");
        }

        public void Init()
        {
            Log("Init");
        }

        public void PostInit()
        {
            Log("PostInit — running full WorldSphereAPI smoke test");

            WorldSphereAPI? api = null;
            try
            {
                bool connected = WorldSphereAPI.Connect(out api);
                Log($"Connect -> {connected}, api null? {api == null}");
                if (!connected || api == null)
                {
                    Log("WorldSphereMod host not detected — aborting tester.");
                    return;
                }
            }
            catch (Exception ex)
            {
                LogError("Connect threw", ex);
                return;
            }

            Run("IsWorld3D",            () => Log($"IsWorld3D = {api!.IsWorld3D}"));
            Run("IsModel3D",            () => Log($"IsModel3D = {api!.IsModel3D}"));
            Run("GetSetting<bool>",     () => Log($"GetSetting<bool>(VoxelEntities) = {api!.GetSetting<bool>("VoxelEntities")}"));
            Run("GetSetting<float>",    () => Log($"GetSetting<float>(BuildingSize) = {api!.GetSetting<float>("BuildingSize")}"));
            Run("MakeActorNonUpright",  () => { api!.MakeActorNonUpright("test_actor"); Log("MakeActorNonUpright ok"); });
            Run("MakeBuildingNonUpright", () => { api!.MakeBuildingNonUpright("test_building"); Log("MakeBuildingNonUpright ok"); });
            Run("MakeProjectileNonUpright", () => { api!.MakeProjectileNonUpright("test_projectile"); Log("MakeProjectileNonUpright ok"); });
            Run("EditEffect",           () => { api!.EditEffect("test_fx", true, false, 0, true); Log("EditEffect ok"); });
            Run("RegisterCustomMesh",   () => { api!.RegisterCustomMesh("test_unit", null!, null!); Log("RegisterCustomMesh ok (v2-only — no-op on v1)"); });
            Run("RegisterBuildingRules", () => { api!.RegisterBuildingRules("test_house", null!); Log("RegisterBuildingRules ok (v2-only — no-op on v1)"); });

            Log("All API smoke-test calls dispatched.");
        }

        private static void Run(string label, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogError(label, ex);
            }
        }

        private static void Log(string msg) => Debug.Log($"[WorldSphereTester] {msg}");

        private static void LogError(string label, Exception ex) =>
            Debug.Log($"[WorldSphereTester] EXCEPTION in {label}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    }
}
