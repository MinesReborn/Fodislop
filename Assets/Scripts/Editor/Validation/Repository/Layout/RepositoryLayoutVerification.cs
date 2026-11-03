#nullable enable

using System;
using System.IO;
using System.Reflection;
using Kern.AssetPipeline.Loading;
using Kern.Core;
using UnityEditor;
using UnityEngine;

namespace Kern.Editor;

public static class RepositoryLayoutVerification
{
    public static void Run()
    {
        string root = Path.GetDirectoryName(Application.dataPath)!;
        if (!File.Exists(Path.Combine(root, ".kern-taxonomy-verification")))
        {
            throw new InvalidOperationException("Run taxonomy verification in a marked disposable project copy.");
        }

        PlayerSettings.companyName = "KernVerification";
        PlayerSettings.productName = "FodislopTaxonomyVerification";
        AssetDatabase.SaveAssets();

        int textures = 0;
        foreach (var entry in TextureDeliveryCatalog.Read(Path.Combine(root, TextureDeliveryCatalog.AssetPath)).All)
        {
            string expected = Path.GetFullPath(Path.Combine(Application.dataPath, "Textures", entry.Key));
            if (!File.Exists(expected))
            {
                throw new InvalidDataException($"Published texture '{entry.Value}' has no authored source at '{expected}'.");
            }

            textures++;
        }

        int resources = 0;
        foreach (FieldInfo field in typeof(ResourcePaths).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(string))
            {
                continue;
            }

            string path = (string)field.GetRawConstantValue()!;
            if (Resources.Load(path) == null)
            {
                throw new FileNotFoundException($"Required runtime resource {field.Name} is missing: {path}");
            }

            resources++;
        }

        ProductionSceneContractValidator.ValidateForCommandLine();
        Debug.Log($"[RepositoryLayout] Verified {textures} published texture paths, {resources} runtime resources and all production scene bindings.");
    }
}
