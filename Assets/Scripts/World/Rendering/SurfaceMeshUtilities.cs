#nullable enable

using System;
using Fodinae.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World;

internal static class SurfaceMeshUtilities
{
    public static Mesh CreateDynamic(string meshName)
    {
        var mesh = new Mesh
        {
            name = meshName,
            hideFlags = HideFlags.DontSave,
        };
        mesh.MarkDynamic();
        return mesh;
    }

    public static T GetOrAddComponent<T>(GameObject gameObject)
        where T : Component
    {
        bool found = gameObject.TryGetComponent(out T? component);
        if (!found || component == null)
        {
            component = gameObject.AddComponent<T>();
        }

        if (component == null)
        {
            throw new MissingComponentException(
                $"Failed to attach required component {typeof(T).Name} to " +
                $"surface object '{gameObject.name}'.");
        }

        return component;
    }

    public static void DrawLightingField(
        CommandBuffer commandBuffer,
        Mesh mesh,
        Material material)
    {
        if (mesh.vertexCount == 0)
        {
            return;
        }

        int pass = material.FindPass(
            ProjectRuntimeContracts.ShaderPassNames.LightingMaterialField);
        if (pass < 0)
        {
            throw new InvalidOperationException(
                $"Surface material '{material.name}' is missing LightingMaterialField pass.");
        }

        commandBuffer.DrawMesh(
            mesh,
            Matrix4x4.identity,
            material,
            submeshIndex: 0,
            shaderPass: pass);
    }

    public static void DestroyOwned(UnityEngine.Object? ownedObject)
    {
        if (ownedObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(ownedObject);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(ownedObject);
        }
    }
}
