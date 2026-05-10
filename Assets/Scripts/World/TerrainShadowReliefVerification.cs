using System;
using System.Collections.Generic;
using UnityEngine;
using Fodinae.Assets.Scripts.World;
using Fodinae.Assets.Scripts.Game.Managers;

namespace Fodinae.Assets.Scripts.World
{
    public class TerrainShadowReliefVerification : MonoBehaviour
    {
        void Start()
        {
            Verify();
        }

        public void Verify()
        {
            Debug.Log("=== SHADOW AND RELIEF VERIFICATION ===");

            var renderer = FindObjectOfType<SingleMeshTerrainRenderer>();
            if (renderer == null)
            {
                Debug.LogError("❌ SingleMeshTerrainRenderer not found!");
                return;
            }

            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("❌ Mesh not found on renderer!");
                return;
            }

            var mesh = meshFilter.sharedMesh;
            Debug.Log($"✅ Mesh found with {mesh.vertexCount} vertices.");

            List<Vector2> shadowRelief = new();
            mesh.GetUVs(5, shadowRelief);

            List<Vector2> localUVs = new();
            mesh.GetUVs(6, localUVs);

            if (shadowRelief.Count == 0)
            {
                Debug.LogError("❌ UV5 (ShadowRelief) is empty!");
            }
            else
            {
                Debug.Log($"✅ UV5 (ShadowRelief) has {shadowRelief.Count} entries.");

                // Sample some data
                int reliefCount = 0;
                int shadowCount = 0;
                for (int i = 0; i < shadowRelief.Count; i++)
                {
                    if (shadowRelief[i].x > 0.5f) reliefCount++;
                    else if (shadowRelief[i].y > 0.01f) shadowCount++;
                }
                Debug.Log($"   Sample data: {reliefCount} vertices with relief, {shadowCount} vertices with shadow.");
            }

            if (localUVs.Count == 0)
            {
                Debug.LogError("❌ UV6 (LocalUVs) is empty!");
            }
            else
            {
                Debug.Log($"✅ UV6 (LocalUVs) has {localUVs.Count} entries.");
                bool correctRange = true;
                for (int i = 0; i < localUVs.Count; i++)
                {
                    if (Mathf.Abs(Mathf.Abs(localUVs[i].x) - 0.70710678f) > 0.0001f ||
                        Mathf.Abs(Mathf.Abs(localUVs[i].y) - 0.70710678f) > 0.0001f)
                    {
                        correctRange = false;
                        break;
                    }
                }
                if (correctRange) Debug.Log("✅ UV6 range is correct ([-0.707, 0.707]).");
                else Debug.LogError("❌ UV6 range is INCORRECT!");
            }

            var meshRenderer = renderer.GetComponent<MeshRenderer>();
            if (meshRenderer != null && meshRenderer.sharedMaterials != null)
            {
                foreach (var mat in meshRenderer.sharedMaterials)
                {
                    if (mat != null)
                    {
                        Debug.Log($"✅ Material using shader: {mat.shader.name}");
                    }
                }
            }

            Debug.Log("=== VERIFICATION COMPLETE ===");
        }
    }
}
