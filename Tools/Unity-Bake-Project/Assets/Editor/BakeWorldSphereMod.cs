using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Rebuilds the legacy "worldsphere" asset bundle from procedurally-created
/// assets.  The original source mesh/material assets lived in the upstream
/// CompoundMeshes repo (MelvinShwuaner) which has been deleted.  This editor
/// script recreates equivalent assets and bundles them for the mod runtime.
///
/// Usage (batch mode):
///   Unity.exe -batchmode -projectPath Tools/Unity-Bake-Project \
///       -executeMethod BakeWorldSphereMod.BakeAll -quit -logFile -
///
/// Output:
///   Tools/Unity-Bake-Project/AssetBundles/win/worldsphere
///   (also linux/osx when run on those platforms)
/// </summary>
public static class BakeWorldSphereMod
{
    const string BundleName = "worldsphere";
    const string OutputDir = "AssetBundles";

    public static void BakeAll()
    {
        Debug.Log("[WSM3D-BakeLegacy] === Starting legacy worldsphere bundle rebuild ===");

        string dataPath = Application.dataPath;
        string projectDir = Path.GetDirectoryName(dataPath);
        string outputDir = Path.Combine(projectDir, OutputDir);

        // Ensure output directory exists
        Directory.CreateDirectory(outputDir);

        // Step 1: Create the legacy mesh asset
        Mesh mesh = CreateCompoundSphereMesh();
        // Path MUST match what Core.cs:2044+ loads: "Assets/WSM3D/LegacyAssets/CompoundSphereMesh.asset"
        string meshPath = "Assets/WSM3D/LegacyAssets/CompoundSphereMesh.asset";
        Directory.CreateDirectory(Path.Combine(dataPath, "WSM3D", "LegacyAssets"));
        AssetDatabase.CreateAsset(mesh, meshPath);
        Debug.Log("[WSM3D-BakeLegacy] Created CompoundSphereMesh asset at " + meshPath);

        // Step 2: Create the legacy material asset
        Material mat = CreateCompoundSphereMaterial();
        string matPath = "Assets/WSM3D/LegacyAssets/CompoundSphereMaterial.mat";
        AssetDatabase.CreateAsset(mat, matPath);
        Debug.Log("[WSM3D-BakeLegacy] Created CompoundSphereMaterial asset at " + matPath);

        // Step 3: Create the skybox material
        Material skybox = CreateSkyboxMaterial();
        string skyboxPath = "Assets/WSM3D/LegacyAssets/Skybox.mat";
        AssetDatabase.CreateAsset(skybox, skyboxPath);
        Debug.Log("[WSM3D-BakeLegacy] Created Skybox asset at " + skyboxPath);

        AssetDatabase.Refresh();

        // Step 4: Tag all assets into the worldsphere bundle
        string[] assetPaths = new[] { meshPath, matPath, skyboxPath };
        foreach (string ap in assetPaths)
        {
            AssetImporter importer = AssetImporter.GetAtPath(ap);
            if (importer != null)
            {
                importer.assetBundleName = BundleName;
                importer.SaveAndReimport();
                Debug.Log("[WSM3D-BakeLegacy] Tagged: " + ap);
            }
        }

        // Step 5: Build the bundle
        Debug.Log("[WSM3D-BakeLegacy] Building bundle to: " + outputDir);
        BuildPipeline.BuildAssetBundles(
            outputDir,
            BuildAssetBundleOptions.None,
            EditorUserBuildSettings.activeBuildTarget);

        Debug.Log("[WSM3D-BakeLegacy] === Legacy worldsphere bundle rebuild complete ===");

        // Report output files
        string bundleFile = Path.Combine(outputDir, BundleName);
        if (File.Exists(bundleFile))
        {
            var fi = new FileInfo(bundleFile);
            Debug.Log($"[WSM3D-BakeLegacy] Output: {fi.FullName} ({fi.Length} bytes)");
        }
    }

    /// <summary>
    /// Creates an icosphere mesh that matches the original CompoundSphereMesh.
    /// The runtime code expects a mesh with vertices, normals, and triangles
    /// suitable for GPU instancing with StructuredBuffer-based coloring.
    /// </summary>
    static Mesh CreateCompoundSphereMesh()
    {
        // Use Unity's built-in icosphere creation via primitives
        // (Quad -> subdivide -> normalize -> done)
        Mesh mesh = new Mesh();
        mesh.name = "CompoundSphereMesh";

        // Create a UV sphere (more reliable than manual icosphere in editor)
        int segments = 16;
        int rings = 12;
        Vector3[] vertices = new Vector3[(segments + 1) * (rings + 1)];
        Vector3[] normals = new Vector3[(segments + 1) * (rings + 1)];
        Vector2[] uvs = new Vector2[(segments + 1) * (rings + 1)];
        int[] triangles = new int[segments * rings * 6];

        float radius = 0.5f;

        for (int ring = 0; ring <= rings; ring++)
        {
            float phi = Mathf.PI * ring / rings;
            float sinPhi = Mathf.Sin(phi);
            float cosPhi = Mathf.Cos(phi);

            for (int seg = 0; seg <= segments; seg++)
            {
                float theta = 2f * Mathf.PI * seg / segments;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                Vector3 normal = new Vector3(sinPhi * cosTheta, cosPhi, sinPhi * sinTheta);
                vertices[ring * (segments + 1) + seg] = normal * radius;
                normals[ring * (segments + 1) + seg] = normal;
                uvs[ring * (segments + 1) + seg] = new Vector2((float)seg / segments, (float)ring / rings);
            }
        }

        int tri = 0;
        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                int current = ring * (segments + 1) + seg;
                int next = current + segments + 1;

                triangles[tri++] = current;
                triangles[tri++] = next;
                triangles[tri++] = current + 1;

                triangles[tri++] = current + 1;
                triangles[tri++] = next;
                triangles[tri++] = next + 1;
            }
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        Debug.Log($"[WSM3D-BakeLegacy] Created mesh: {vertices.Length} vertices, {triangles.Length / 3} triangles");
        return mesh;
    }

    /// <summary>
    /// Creates a Standard shader material with properties matching what the
    /// mod runtime expects for compound sphere rendering.
    /// </summary>
    static Material CreateCompoundSphereMaterial()
    {
        Shader standard = Shader.Find("Standard");
        if (standard == null)
        {
            Debug.LogWarning("[WSM3D-BakeLegacy] Standard shader not found, using Default-Material");
            return new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        Material mat = new Material(standard);
        mat.name = "CompoundSphereMaterial";

        // Set properties that the mod's rendering pipeline expects
        mat.SetFloat("_Metallic", 0.2f);
        mat.SetFloat("_Glossiness", 0.6f);
        mat.SetFloat("_SmoothnessTextureChannel", 0); // metallic workflow
        mat.color = new Color(0.7f, 0.7f, 0.7f, 1.0f);

        // Enable GPU instancing (required for StructuredBuffer-based rendering)
        mat.enableInstancing = true;

        Debug.Log("[WSM3D-BakeLegacy] Created material with Standard shader + instancing enabled");
        return mat;
    }

    /// <summary>
    /// Creates a basic skybox material.
    /// </summary>
    static Material CreateSkyboxMaterial()
    {
        Shader skyboxShader = Shader.Find("Skybox/Procedural");
        if (skyboxShader == null)
        {
            skyboxShader = Shader.Find("Standard");
        }

        Material skybox = new Material(skyboxShader);
        skybox.name = "WSM3D_Skybox";

        // Cubemap-based skybox is created at runtime; provide a placeholder
        Debug.Log("[WSM3D-BakeLegacy] Created skybox material placeholder");
        return skybox;
    }

    [MenuItem("WSM3D/Bake Legacy Worldsphere Bundle")]
    public static void MenuBake() => BakeAll();
}
