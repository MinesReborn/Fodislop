#nullable enable

using NUnit.Framework;
using Kern.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kern.Tests.Core;

public sealed class DisplayOutputProfileTests
{
    [Test]
    public void DisplayCameraPermitsHDRWithoutCachingCurrentDisplayMode()
    {
        var owner = new GameObject("HDR camera test");
        try
        {
            Camera camera = owner.AddComponent<Camera>();
            UniversalAdditionalCameraData data = owner.AddComponent<UniversalAdditionalCameraData>();
            camera.allowHDR = false;
            data.allowHDROutput = false;
            HDROutput.ConfigureCamera(camera);
            Assert.That(camera.allowHDR, Is.True);
            Assert.That(data.allowHDROutput, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void OffscreenCameraRetainsItsAuthoredOutputContract()
    {
        var owner = new GameObject("Offscreen camera test");
        var target = new RenderTexture(16, 16, 0);
        try
        {
            Camera camera = owner.AddComponent<Camera>();
            camera.targetTexture = target;
            camera.allowHDR = false;
            UniversalAdditionalCameraData data = owner.AddComponent<UniversalAdditionalCameraData>();
            data.allowHDROutput = false;
            data.renderPostProcessing = false;
            HDROutput.ConfigureCamera(camera);
            Assert.That(camera.allowHDR, Is.False);
            Assert.That(data.allowHDROutput, Is.False);
            Assert.That(data.renderPostProcessing, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(target);
        }
    }

    [TestCase("Assets/Settings/DefaultVolumeProfile.asset")]
    [TestCase("Assets/Settings/PostProcessVolumeProfile.asset")]
    public void AuthoredProfilesDoNotIntroduceASecondNativePostProcess(string path)
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        Assert.That(profile, Is.Not.Null, path);
        foreach (VolumeComponent component in profile.components)
        {
            bool nativePostEffect = component != null &&
                component.GetType().Namespace == typeof(Tonemapping).Namespace &&
                component is IPostProcessComponent;
            Assert.That(nativePostEffect, Is.False,
                $"{path}: {component?.GetType().Name}; artistic effects belong to Kern, output tonemapping to Bootstrap.");
        }
    }
}
