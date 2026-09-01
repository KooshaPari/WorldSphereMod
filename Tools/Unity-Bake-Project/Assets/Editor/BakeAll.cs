// Full bake: legacy assets + shaders + both bundles (worldsphere + wsm3d-shaders)
// Run via: Unity -batchmode -nographics -projectPath Tools/Unity-Bake-Project -executeMethod WSM3D.BakeAll.Run -logFile - -quit
//
// Re-creates LegacyAssets/ (Mesh + Material + Skybox) so the worldsphere bundle
// can be serialized with valid references. Rebuilds wsm3d-shaders too.

using UnityEngine;
using UnityEditor;
using System.IO;

namespace WSM3D
{
    public static class BakeAll
    {
        private const string BakeProjectRoot = "Assets/WSM3D";
        private const string LegacyDir = "Assets/WSM3D/LegacyAssets";

        [MenuItem("WSM3D/Bake All")]
        public static void Run()
        {
            Debug.Log("[WSM3D] BakeAll: starting full bundle build");

            EnsureLegacyAssetsExist();
            BuildAssetBundles();

            Debug.Log("[WSM3D] BakeAll: done");
        }

        private static void EnsureLegacyAssetsExist()
        {
            Directory.CreateDirectory(LegacyDir);

            // CompoundSphereMesh: 12-vertex icosphere (procedural fallback matches code)
            var meshPath = LegacyDir + "/CompoundSphereMesh.asset";
            if (!File.Exists(meshPath))
            {
                var mesh = new Mesh { name = "CompoundSphereMesh" };
                mesh.vertices = IcosphereVertices();
                mesh.triangles = IcosphereTriangles();
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, meshPath);
                Debug.Log($"[WSM3D] Created {meshPath} ({mesh.vertexCount} verts)");
            }

            // CompoundSphereMaterial: uses Standard shader with metallic=0, roughness=0.5
            var matPath = LegacyDir + "/CompoundSphereMaterial.mat";
            if (!File.Exists(matPath))
            {
                var shader = Shader.Find("Standard");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                var mat = new Material(shader) { name = "CompoundSphereMaterial" };
                AssetDatabase.CreateAsset(mat, matPath);
                Debug.Log($"[WSM3D] Created {matPath} (shader={shader.name})");
            }

            // Skybox material: simple blue gradient
            var skyPath = LegacyDir + "/Skybox.mat";
            if (!File.Exists(skyPath))
            {
                var shader = Shader.Find("Skybox/Procedural");
                if (shader == null)
                    shader = Shader.Find("Standard");
                var mat = new Material(shader) { name = "Skybox" };
                AssetDatabase.CreateAsset(mat, skyPath);
                Debug.Log($"[WSM3D] Created {skyPath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static Vector3[] IcosphereVertices()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            return new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
        }

        private static int[] IcosphereTriangles()
        {
            // 20 triangle faces of an icosahedron
            return new[]
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
        }

        private static void BuildAssetBundles()
        {
            // Resolve output relative to this script file, not Application.dataPath (which is Assets/)
            var scriptDir = Path.GetDirectoryName(new System.Diagnostics.StackTrace(true).GetFrame(0).GetFileName());
            var projectRoot = Path.GetFullPath(Path.Combine(scriptDir, "..", "..", ".."));
            var bundleOutput = Path.Combine(projectRoot, "WorldSphereMod", "AssetBundles");
            Directory.CreateDirectory(bundleOutput);

            // Build the package-relative paths the AssetBundleBuild expects
            const string BakeRoot = "Assets/WSM3D";
            string LegacyDir = BakeRoot + "/LegacyAssets";
            string ShadersDir = BakeRoot + "/Shaders";

            // Locate actual shader file names (BakeShaders may have renamed them)
            string FindShader(string baseName) {
                foreach (var f in Directory.EnumerateFiles(Path.Combine(Application.dataPath, "WSM3D", "Shaders"))) {
                    if (Path.GetFileName(f).StartsWith(baseName) && f.EndsWith(".shader"))
                        return "Assets/WSM3D/Shaders/" + Path.GetFileName(f);
                }
                return null;
            }

            var shaderPaths = new System.Collections.Generic.List<string>();
            foreach (var name in new[] { "WSM3DUnlit", "WSM3DPbr", "WSM3DTerrainBlend", "SphereTerrain", "OpaqueVertexColor", "CompoundSphere", "Impostor", "GerstnerWater", "FoliageWind" }) {
                var p = FindShader(name);
                if (p != null)
                {
                    shaderPaths.Add(p);
                    Debug.Log($"[WSM3D] Found shader: {p}");
                }
                else
                {
                    Debug.LogWarning($"[WSM3D] Shader not found: {name}");
                }
            }

            var builds = new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = "worldsphere",
                    assetNames = new[] {
                        LegacyDir + "/CompoundSphereMesh.asset",
                        LegacyDir + "/CompoundSphereMaterial.mat",
                        LegacyDir + "/Skybox.mat"
                    }
                },
                new AssetBundleBuild
                {
                    assetBundleName = "wsm3d-shaders",
                    assetNames = shaderPaths.ToArray()
                }
            };

            var opts = BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode;

            var manifest = BuildPipeline.BuildAssetBundles(
                bundleOutput,
                builds,
                opts,
                BuildTarget.StandaloneWindows64);

            if (manifest == null)
            {
                Debug.LogError("[WSM3D] BakeAll: BuildAssetBundles returned null");
                return;
            }

            Debug.Log("[WSM3D] BakeAll: built bundles to " + bundleOutput);
            foreach (var b in manifest.GetAllAssetBundles())
            {
                Debug.Log($"[WSM3D]   - {b}");
            }
        }
    }
}
