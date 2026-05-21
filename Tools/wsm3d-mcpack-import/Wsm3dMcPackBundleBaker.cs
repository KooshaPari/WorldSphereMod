using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class Wsm3dMcPackBundleBaker
{
    private const string BuildAssetPath = "Assets/MCPackBake";
    private const string BundleAssetName = "atlas";

    public static void BakeFromCommandLine()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            string atlasPath = GetArg(args, "--atlas");
            string outputBundle = GetArg(args, "--output-bundle");
            string buildTargetName = GetArg(args, "--build-target", "StandaloneWindows64");
            string bundleName = GetArg(args, "--bundle-name", BundleAssetName);

            if (string.IsNullOrWhiteSpace(atlasPath) || !File.Exists(atlasPath))
            {
                throw new Exception("Missing or invalid --atlas path.");
            }

            string sourceProjectAssets = Path.Combine(Application.dataPath, "MCPackBake");
            if (!Directory.Exists(sourceProjectAssets))
            {
                Directory.CreateDirectory(sourceProjectAssets);
            }

            string stagingAtlas = Path.Combine(sourceProjectAssets, $"{bundleName}.png");
            File.Copy(atlasPath, stagingAtlas, overwrite: true);

            string assetPath = $"{BuildAssetPath}/{bundleName}.png";
            string normalizedAssetPath = assetPath.Replace("\\", "/");

            AssetDatabase.Refresh();
            var importer = AssetImporter.GetAtPath(normalizedAssetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Point;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            var loaded = AssetImporter.GetAtPath(normalizedAssetPath);
            if (loaded == null)
            {
                throw new Exception("Failed to import atlas into Unity staging assets.");
            }

            loaded.assetBundleName = bundleName;
            loaded.SaveAndReimport();
            AssetDatabase.Refresh();

            string outputDirectory = Path.GetDirectoryName(outputBundle);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new Exception("Invalid --output-bundle directory.");
            }
            Directory.CreateDirectory(outputDirectory);

            BuildTarget target = (BuildTarget)Enum.Parse(typeof(BuildTarget), buildTargetName);
            BuildPipeline.BuildAssetBundles(
                outputDirectory,
                BuildAssetBundleOptions.UncompressedAssetBundle | BuildAssetBundleOptions.ChunkBasedCompression,
                target
            );

            AssetDatabase.RemoveUnusedAssetBundleNames();
            AssetDatabase.Refresh();

            string expectedBundle = Path.Combine(outputDirectory, bundleName);
            if (!File.Exists(expectedBundle))
            {
                throw new Exception($"Expected AssetBundle was not created at '{expectedBundle}'.");
            }

            if (!string.Equals(outputBundle, expectedBundle, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(outputBundle))
                {
                    File.Delete(outputBundle);
                }
                File.Copy(expectedBundle, outputBundle, overwrite: true);
                File.Delete(expectedBundle);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MCPackBundleBaker] {ex}");
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(0);
    }

    private static string GetArg(string[] args, string key, string defaultValue = "")
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return defaultValue;
    }
}
