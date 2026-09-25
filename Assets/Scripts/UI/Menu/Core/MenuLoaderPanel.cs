#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using UnityEngine.UIElements;

namespace Kern.UI
{
    /// <summary>
    /// Owns the loading panel: element lookup, show/hide, and progress updates.
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

            // Подстановка пустышек вместо недостающих элементов запрещена.
            // Она молча превращала «в разметке нет того, чего ждёт код» в
            // «меню построено», после чего кадр умирал тремя секундами позже
            // на таймауте готовности, и в логе не было ни одного имени, за
            // которое можно зацепиться. Отказ называет всё недостающее сразу.
            string[] missing =
            [
                .. _container == null ? new[] { "LoaderContainer" } : [],
                .. _content == null ? new[] { "LoaderContent" } : [],
                .. progressFill == null ? new[] { "LoaderProgressFill" } : [],
                .. phaseLabel == null ? new[] { "LoaderPhaseLabel" } : [],
                .. phaseCount == null ? new[] { "LoaderPhaseCount" } : [],
                .. phaseList == null ? new[] { "LoaderPhaseList" } : [],
            ];
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    "[MainMenu] В Resources/UI/Menus/MainMenu.uxml нет элементов, которых ждёт загрузчик: " +
                    string.Join(", ", missing) +
                    ". Имя или тип элемента в разметке разошлись с кодом.");
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
