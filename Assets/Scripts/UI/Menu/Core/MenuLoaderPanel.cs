#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI
{
    /// <summary>
    /// Owns the loading panel: element lookup (with placeholder synthesis
    /// when MainMenu.uxml is missing nodes), show/hide, and progress updates.
    /// </summary>
    internal sealed class MenuLoaderPanel
    {
        private VisualElement? _container;
        private VisualElement? _content;
        private MenuLoaderProgress? _progress;
        private bool _hiddenAtDone;

        public void Bind(VisualElement tree, VisualElement searchRoot, ILocalizationService loc)
        {
            _container = tree.Q<VisualElement>("LoaderContainer") ?? searchRoot.Q<VisualElement>("LoaderContainer");
            _content = tree.Q<VisualElement>("LoaderContent") ?? searchRoot.Q<VisualElement>("LoaderContent");
            VisualElement? progressFill = tree.Q<VisualElement>("LoaderProgressFill") ?? searchRoot.Q<VisualElement>("LoaderProgressFill");
            Label? phaseLabel = tree.Q<Label>("LoaderPhaseLabel") ?? searchRoot.Q<Label>("LoaderPhaseLabel");
            Label? phaseCount = tree.Q<Label>("LoaderPhaseCount") ?? searchRoot.Q<Label>("LoaderPhaseCount");
            VisualElement? phaseList = tree.Q<VisualElement>("LoaderPhaseList") ?? searchRoot.Q<VisualElement>("LoaderPhaseList");

            if (_container == null || _content == null ||
                progressFill == null || phaseLabel == null ||
                phaseCount == null || phaseList == null)
            {
                Debug.LogWarning("[MainMenu] Some loader elements missing from MainMenu.uxml, synthesizing placeholders to prevent startup crash.");
                _container ??= new VisualElement { name = "LoaderContainer" };
                _content ??= new VisualElement { name = "LoaderContent" };
                progressFill ??= new VisualElement { name = "LoaderProgressFill" };
                phaseLabel ??= new Label { name = "LoaderPhaseLabel" };
                phaseCount ??= new Label { name = "LoaderPhaseCount" };
                phaseList ??= new VisualElement { name = "LoaderPhaseList" };

                _container.Add(_content);
                _content.Add(progressFill);
                _content.Add(phaseLabel);
                _content.Add(phaseCount);
                _content.Add(phaseList);
                if (searchRoot != null && !searchRoot.Contains(_container))
                {
                    searchRoot.Add(_container);
                }
            }

            _progress = new MenuLoaderProgress(
                progressFill,
                phaseLabel,
                phaseCount,
                phaseList,
                loc);

            if (_container != null)
            {
                _container.pickingMode = PickingMode.Ignore;
            }

            UIState.Hide(_container);
            UIState.Hide(_content);
        }

        public void Show()
        {
            UIState.Show(_container);
            UIState.Show(_content);
        }

        public void Hide()
        {
            UIState.Hide(_container);
        }

        public void Reset()
        {
            _hiddenAtDone = false;
        }

        public void UpdateProgress(WorldLoadPhase phase, Action onDone)
        {
            _progress?.UpdateProgress(phase);

            if (phase == WorldLoadPhase.Done && !_hiddenAtDone)
            {
                _hiddenAtDone = true;
                onDone();
            }
        }

        public void RefreshLocalization()
        {
            _progress?.RefreshLocalization();
        }
    }
}
