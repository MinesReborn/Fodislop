#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Kern.Tools.Imgui.Profiling;

public sealed class UiRenderStatsCollector
{
    private static readonly HashSet<string> _ClipClasses = new(StringComparer.Ordinal)
    {
        "ui-panel", "ui-scroll-viewport", "hud-minimap-container", "sci-fi-bar-track",
        "world-labels", "chat-message", "gchat-message", "fit-clip", "fit-clamp", "fit-shrink",
        "mm-root", "mm-loader-progress-track", "mm-ticker-text", "mm-settings-layout",
        "unity-scroll-view__content-viewport",
    };

    private readonly List<(VisualElement Element, string Subtree)> _statsStack = [];
    private readonly List<GameObject> _sceneRoots = [];
    private readonly List<UIDocument> _documents = [];
    private readonly List<UIDocument> _documentScratch = [];

    public UiLayoutTracker.RenderStats Stats { get; private set; } = new();

    public List<UIDocument> CollectDocuments()
    {
        _documents.Clear();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
            {
                continue;
            }

            scene.GetRootGameObjects(_sceneRoots);
            foreach (GameObject root in _sceneRoots)
            {
                root.GetComponentsInChildren(includeInactive: false, _documentScratch);
                foreach (UIDocument document in _documentScratch)
                {
                    if (document.isActiveAndEnabled)
                    {
                        _documents.Add(document);
                    }
                }
            }
        }

        return _documents;
    }

    public void CollectRenderStats(Func<VisualElement, string> shortNameProvider)
    {
        var stats = new UiLayoutTracker.RenderStats();
        foreach (UIDocument document in CollectDocuments())
        {
            if (document.panelSettings != null)
            {
                stats.AtlasLimit = Math.Max(stats.AtlasLimit, document.panelSettings.dynamicAtlasSettings.maxSubTextureSize);
            }

            VisualElement? root = document.rootVisualElement;
            if (root == null)
            {
                continue;
            }

            _statsStack.Clear();
            _statsStack.Add((root, "(корень)"));
            while (_statsStack.Count > 0)
            {
                (VisualElement element, string subtree) = _statsStack[^1];
                _statsStack.RemoveAt(_statsStack.Count - 1);

                IResolvedStyle style = element.resolvedStyle;
                if (style.display == DisplayStyle.None ||
                    style.visibility == Visibility.Hidden ||
                    style.opacity <= 0f)
                {
                    continue;
                }

                stats.Visible++;
                stats.VisibleBySubtree.TryGetValue(subtree, out int inSubtree);
                stats.VisibleBySubtree[subtree] = inSubtree + 1;

                if (style.opacity < 1f)
                {
                    stats.Translucent++;
                }

                if (style.translate.x != 0f || style.translate.y != 0f)
                {
                    stats.Translated++;
                }

                if (element is TextElement text && !string.IsNullOrEmpty(text.text))
                {
                    stats.Texts++;
                    stats.TextCharacters += text.text.Length;
                    if (style.unityTextOutlineWidth > 0f)
                    {
                        stats.OutlinedTexts++;
                    }

                    if (style.textShadow.color.a > 0f)
                    {
                        stats.ShadowedTexts++;
                    }
                }

                Texture? texture = style.backgroundImage.texture != null
                    ? style.backgroundImage.texture
                    : style.backgroundImage.sprite != null
                        ? style.backgroundImage.sprite.texture
                        : style.backgroundImage.renderTexture;
                if (element is Image image && image.image != null)
                {
                    texture = image.image;
                }

                if (texture != null)
                {
                    stats.Images++;
                    stats.Textures.TryGetValue(texture, out int uses);
                    stats.Textures[texture] = uses + 1;
                }

                foreach (string className in element.GetClasses())
                {
                    if (!_ClipClasses.Contains(className))
                    {
                        continue;
                    }

                    stats.Clips++;
                    if (style.borderTopLeftRadius > 0f || style.borderTopRightRadius > 0f ||
                        style.borderBottomLeftRadius > 0f || style.borderBottomRightRadius > 0f)
                    {
                        stats.RoundedClips++;
                    }

                    break;
                }

                for (int i = 0; i < element.hierarchy.childCount; i++)
                {
                    VisualElement child = element.hierarchy[i];
                    bool wrapper = element == root ||
                        subtree == "(корень)" ||
                        subtree.StartsWith("TemplateContainer", StringComparison.Ordinal) ||
                        subtree == "#UIDocument-container";
                    string childSubtree = wrapper ? shortNameProvider(child) : subtree;
                    _statsStack.Add((child, childSubtree));
                }
            }
        }

        Stats = stats;
    }
}
