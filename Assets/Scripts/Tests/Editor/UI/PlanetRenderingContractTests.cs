#nullable enable

using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fodinae.Tests.UI;

public sealed class PlanetRenderingContractTests
{
    private const string AlbedoPath = "Assets/Textures/UI/planet_albedo.png";
    private const string NormalPath = "Assets/Textures/UI/planet_normal.png";
    private const string PackedPath = "Assets/Textures/UI/planet_packed.png";
    private const string SurfaceMaterialPath = "Assets/Materials/PlanetSurface.mat";
    private const string AtmosphereMaterialPath = "Assets/Materials/PlanetAtmosphere.mat";
    private const string SurfaceMeshPath = "Assets/Meshes/PlanetIcosphere.asset";
    private const string AtmosphereMeshPath = "Assets/Meshes/PlanetShellIcosphere.asset";
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string SurfaceShaderPath = "Assets/Shaders/UI/PlanetSurface.shader";
    private const string AtmosphereShaderPath = "Assets/Shaders/UI/PlanetAtmosphere.shader";

    [Test]
    public void MaterialsUseLightweightPlanetShadersAndBakedMaps()
    {
        Material surface = RequireAsset<Material>(SurfaceMaterialPath);
        Material atmosphere = RequireAsset<Material>(AtmosphereMaterialPath);

        Assert.That(surface.shader.name, Is.EqualTo("Fodinae/UI/PlanetSurface"));
        Assert.That(atmosphere.shader.name, Is.EqualTo("Fodinae/UI/PlanetAtmosphere"));

        Assert.That(surface.GetTexture("_AlbedoMap"), Is.SameAs(RequireAsset<Texture2D>(AlbedoPath)));
        Assert.That(surface.GetTexture("_NormalMap"), Is.SameAs(RequireAsset<Texture2D>(NormalPath)));
        Assert.That(surface.GetTexture("_PackedMap"), Is.SameAs(RequireAsset<Texture2D>(PackedPath)));

        // Поверхность обязана остаться матовой в обоих концах диапазона.
        // Именно съезд шероховатости вниз превращает планету в мокрый шар, и
        // проверяется здесь потому, что на глаз это ловится только у терминатора.
        Assert.That(surface.GetFloat("_RoughnessMin"), Is.InRange(0.65f, 0.9f));
        Assert.That(surface.GetFloat("_RoughnessMax"), Is.InRange(0.65f, 0.9f));

        // Сильная нормаль — вторая половина того же дефекта: рельеф начинает
        // бликовать по всей поверхности и читается как рябь на масле.
        Assert.That(surface.GetFloat("_NormalStrength"), Is.InRange(0f, 0.35f));

        Assert.That(surface.GetFloat("_SunIntensity"), Is.InRange(3f, 6f));
        Assert.That(surface.GetFloat("_Exposure"), Is.InRange(0.5f, 2f));
    }

    [Test]
    public void BakedMapsMatchOrbitalResolutionContract()
    {
        foreach (string path in new[] { AlbedoPath, NormalPath, PackedPath })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);

            // Карты равнопромежуточные: ширина вдвое больше высоты, и потолок
            // импорта должен быть не ниже 4096. Прошлый пайплайн ужимал ту же
            // географию до 512 — на пол-экрана от неё не оставалось ничего.
            Assert.That(importer.maxTextureSize, Is.GreaterThanOrEqualTo(4096), path);
            Assert.That(importer.mipmapEnabled, Is.True, path);

            // Долгота заворачивается, широта — нет: повтор по V склеил бы
            // северный полюс с южным.
            Assert.That(importer.wrapModeU, Is.EqualTo(TextureWrapMode.Repeat), path);
            Assert.That(importer.wrapModeV, Is.EqualTo(TextureWrapMode.Clamp), path);
        }

        var albedoImporter = (TextureImporter)AssetImporter.GetAtPath(AlbedoPath);
        Assert.That(albedoImporter.sRGBTexture, Is.True);

        // Нормаль и упакованная карта — данные, а не картинки: гамма-коррекция
        // сдвинула бы и наклон, и шероховатость.
        foreach (string path in new[] { NormalPath, PackedPath })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.sRGBTexture, Is.False, path);
        }
    }

    [Test]
    public void MeshesStayInsideFixedOrbitalBudgets()
    {
        Mesh surface = RequireAsset<Mesh>(SurfaceMeshPath);
        Mesh atmosphere = RequireAsset<Mesh>(AtmosphereMeshPath);

        Assert.That(surface.triangles.Length / 3, Is.LessThanOrEqualTo(20_480));
        Assert.That(atmosphere.triangles.Length / 3, Is.LessThanOrEqualTo(5_120));
    }

    [Test]
    public void ShadersKeepTheExpectedTextureSampleBudget()
    {
        string surface = File.ReadAllText(SurfaceShaderPath);
        string atmosphere = File.ReadAllText(AtmosphereShaderPath);

        // Ровно три выборки — альбедо, нормаль, упаковка. Рост этого числа
        // означает, что в шейдер вернулись слои, которым место в запекании.
        Assert.That(Count(surface, "SAMPLE_TEXTURE2D_GRAD("), Is.EqualTo(1));
        Assert.That(Count(surface, "SampleEquirectRGB("), Is.EqualTo(4));
        Assert.That(Count(atmosphere, "SAMPLE_TEXTURE2D"), Is.Zero);

        // Процедурная генерация в рантайме — это ровно то, что прошлые заходы
        // приносили обратно каждый раз, когда картинка казалась пустоватой.
        foreach (string forbidden in new[] { "Fbm", "GradientNoise", "raymarch", "tessellation" })
        {
            Assert.That(surface, Does.Not.Contain(forbidden).IgnoreCase, forbidden);
            Assert.That(atmosphere, Does.Not.Contain(forbidden).IgnoreCase, forbidden);
        }
    }

    [Test]
    public void AtmosphereShellIsThinAndUsesExactlyOneRenderer()
    {
        Scene scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Additive);
        try
        {
            Transform surface = RequireSceneTransform(scene, "PlanetSurface");
            Transform atmosphere = RequireSceneTransform(scene, "PlanetAtmosphere");
            float radiusRatio = atmosphere.localScale.x / surface.localScale.x;

            Assert.That(radiusRatio, Is.EqualTo(1.015f).Within(0.0001f));
            Assert.That(atmosphere.GetComponents<Renderer>(), Has.Length.EqualTo(1));
            Assert.That(atmosphere.GetComponent<Renderer>().enabled, Is.True);
        }
        finally
        {
            _ = EditorSceneManager.CloseScene(scene, removeScene: true);
        }
    }

    private static T RequireAsset<T>(string path)
        where T : Object
    {
        T? asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Assert.That(asset, Is.Not.Null, $"Required asset is missing: {path}");
        return asset!;
    }

    private static Transform RequireSceneTransform(Scene scene, string objectName)
    {
        Transform? result = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(includeInactive: true))
            .FirstOrDefault(candidate => candidate.name == objectName);
        Assert.That(result, Is.Not.Null, $"MainMenu is missing '{objectName}'.");
        return result!;
    }

    private static int Count(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
