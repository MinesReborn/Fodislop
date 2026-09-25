#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kern.Tools.Imgui.Profiling;

/// <summary>
/// Перепись живой иерархии: сколько объектов, в каких сценах и под какими
/// корнями, какие компоненты и сколько их. Файл сцены этого не показывает —
/// роботы, подписи, пулы и DontDestroyOnLoad появляются только во время игры.
///
/// Обход полный и дорогой, поэтому идёт раз в <see cref="IntervalSeconds"/> и
/// только пока вкладка открыта. Его собственное время пишется в
/// <see cref="Snapshot.WalkMilliseconds"/>, чтобы не путать с кадром игры.
/// </summary>
public sealed class SceneCensus
{
    public const float IntervalSeconds = 2f;
    private const int TopRoots = 15;
    private const int TopComponents = 30;

    public sealed class RootStat
    {
        public string Scene = string.Empty;
        public string Name = string.Empty;
        public bool Active;
        public int Objects;
        public int ActiveObjects;
    }

    public sealed class SceneStat
    {
        public string Name = string.Empty;
        public int Roots;
        public int Objects;
        public int ActiveObjects;
    }

    public sealed class Snapshot
    {
        public float WalkMilliseconds;
        public int Objects;
        public int ActiveObjects;
        public int Components;
        public int MaxDepth;
        public string DeepestPath = string.Empty;
        public readonly List<SceneStat> Scenes = new();
        public readonly List<RootStat> Roots = new();
        public readonly List<(string Type, int Total, int Enabled)> ComponentTypes = new();
        public readonly List<string> Cameras = new();
        public int Renderers;
        public int RenderersEnabled;
        public int RenderersVisible;
        public int MissingScripts;
    }

    private readonly List<Component> _components = new();
    private readonly List<Transform> _stack = new();
    private readonly Dictionary<Type, (int Total, int Enabled)> _byType = new();
    private float _nextWalk;

    public Snapshot? Last { get; private set; }

    public void Tick(bool visible)
    {
        if (!visible || Time.unscaledTime < _nextWalk)
        {
            return;
        }

        _nextWalk = Time.unscaledTime + IntervalSeconds;
        Last = Walk();
    }

    private Snapshot Walk()
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        var snapshot = new Snapshot();
        _byType.Clear();

        var roots = new List<GameObject>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded)
            {
                WalkScene(scene, roots, snapshot);
            }
        }

        // DontDestroyOnLoad перечисляется по имени без объектов-зондов:
        // GetSceneByName ничего не создаёт и не переносит между сценами,
        // поэтому запрет KERN-FORBIDDEN-API его не касается. Без этого
        // application-камера Bootstrap и менеджеры были невидимы («камер не
        // найдено» при рендерящейся игре).
        Scene ddolScene = SceneManager.GetSceneByName("DontDestroyOnLoad");
        if (ddolScene.isLoaded)
        {
            WalkScene(ddolScene, roots, snapshot);
        }

        snapshot.Roots.Sort((a, b) => b.Objects.CompareTo(a.Objects));
        if (snapshot.Roots.Count > TopRoots)
        {
            snapshot.Roots.RemoveRange(TopRoots, snapshot.Roots.Count - TopRoots);
        }

        foreach (KeyValuePair<Type, (int Total, int Enabled)> pair in _byType)
        {
            snapshot.ComponentTypes.Add((pair.Key.Name, pair.Value.Total, pair.Value.Enabled));
        }

        snapshot.ComponentTypes.Sort((a, b) => b.Total.CompareTo(a.Total));
        if (snapshot.ComponentTypes.Count > TopComponents)
        {
            snapshot.ComponentTypes.RemoveRange(TopComponents, snapshot.ComponentTypes.Count - TopComponents);
        }

        snapshot.WalkMilliseconds = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 /
            System.Diagnostics.Stopwatch.Frequency);
        return snapshot;
    }

    private void WalkScene(Scene scene, List<GameObject> roots, Snapshot snapshot)
    {
        roots.Clear();
        scene.GetRootGameObjects(roots);
        var sceneStat = new SceneStat { Name = scene.name, Roots = roots.Count };

        foreach (GameObject root in roots)
        {
            var rootStat = new RootStat { Scene = scene.name, Name = root.name, Active = root.activeInHierarchy };
            _stack.Clear();
            _stack.Add(root.transform);

            while (_stack.Count > 0)
            {
                Transform current = _stack[^1];
                _stack.RemoveAt(_stack.Count - 1);
                GameObject gameObject = current.gameObject;

                rootStat.Objects++;
                if (gameObject.activeInHierarchy)
                {
                    rootStat.ActiveObjects++;
                }

                CountComponents(gameObject, snapshot);
                TrackDepth(current, root.transform, snapshot);

                for (int c = current.childCount - 1; c >= 0; c--)
                {
                    _stack.Add(current.GetChild(c));
                }
            }

            sceneStat.Objects += rootStat.Objects;
            sceneStat.ActiveObjects += rootStat.ActiveObjects;
            snapshot.Roots.Add(rootStat);
        }

        snapshot.Objects += sceneStat.Objects;
        snapshot.ActiveObjects += sceneStat.ActiveObjects;
        snapshot.Scenes.Add(sceneStat);
    }

    private void CountComponents(GameObject gameObject, Snapshot snapshot)
    {
        _components.Clear();
        gameObject.GetComponents(_components);
        foreach (Component? component in _components)
        {
            // Пустой элемент — компонент, чей скрипт потерян.
            if (component == null)
            {
                snapshot.MissingScripts++;
                continue;
            }

            snapshot.Components++;
            bool enabled = component switch
            {
                Behaviour behaviour => behaviour.isActiveAndEnabled,
                Renderer renderer => renderer.enabled && gameObject.activeInHierarchy,
                _ => gameObject.activeInHierarchy,
            };

            Type type = component.GetType();
            _byType.TryGetValue(type, out (int Total, int Enabled) counts);
            _byType[type] = (counts.Total + 1, counts.Enabled + (enabled ? 1 : 0));

            if (component is Renderer rendererComponent)
            {
                snapshot.Renderers++;
                if (enabled)
                {
                    snapshot.RenderersEnabled++;
                }

                if (rendererComponent.isVisible)
                {
                    snapshot.RenderersVisible++;
                }
            }
            else if (component is Camera camera)
            {
                snapshot.Cameras.Add(
                    $"{Path(camera.transform)} — {(camera.isActiveAndEnabled ? "включена" : "выключена")}, " +
                    $"глубина {camera.depth}, в текстуру: {(camera.targetTexture != null ? camera.targetTexture.name : "нет")}");
            }
        }
    }

    private static void TrackDepth(Transform current, Transform root, Snapshot snapshot)
    {
        int depth = 0;
        for (Transform? t = current; t != null && t != root; t = t.parent)
        {
            depth++;
        }

        if (depth > snapshot.MaxDepth)
        {
            snapshot.MaxDepth = depth;
            snapshot.DeepestPath = Path(current);
        }
    }

    private static string Path(Transform transform)
    {
        var names = new List<string>();
        for (Transform? t = transform; t != null; t = t.parent)
        {
            names.Add(t.name);
        }

        names.Reverse();
        return string.Join(" / ", names);
    }
}
