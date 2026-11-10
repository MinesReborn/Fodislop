#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.UI.Localization;
using UnityEngine.UIElements;

using Kern.UI.Screens;

namespace Kern.UI.Menu
{
    /// <summary>
    /// Owns the loading panel: element lookup, show/hide, and progress updates.
    /// </summary>
    internal sealed class MenuLoaderPanel
    {
        private VisualElement? _container;
        private VisualElement? _content;
        private MenuLoaderProgress? _progress;
        private Button? _retry;
        private Label? _tip;
        private ILoadingTip? _tipSource;
        private bool _hiddenAtDone;

        public bool HasFailure { get; private set; }

        public void Bind(VisualElement tree, VisualElement searchRoot, ILocalizationService loc, ILoadingTip tip)
        {
            _tipSource = tip;
            _container = tree.Q<VisualElement>("LoaderContainer") ?? searchRoot.Q<VisualElement>("LoaderContainer");
            _content = tree.Q<VisualElement>("LoaderContent") ?? searchRoot.Q<VisualElement>("LoaderContent");
            VisualElement? progressFill = tree.Q<VisualElement>("LoaderProgressFill") ?? searchRoot.Q<VisualElement>("LoaderProgressFill");
            Label? phaseLabel = tree.Q<Label>("LoaderPhaseLabel") ?? searchRoot.Q<Label>("LoaderPhaseLabel");
            Label? phaseCount = tree.Q<Label>("LoaderPhaseCount") ?? searchRoot.Q<Label>("LoaderPhaseCount");
            VisualElement? phaseList = tree.Q<VisualElement>("LoaderPhaseList") ?? searchRoot.Q<VisualElement>("LoaderPhaseList");
            Label? explain = tree.Q<Label>("LoaderExplain") ?? searchRoot.Q<Label>("LoaderExplain");
            _retry = tree.Q<Button>("RetryDescentButton") ?? searchRoot.Q<Button>("RetryDescentButton");
            _tip = tree.Q<Label>("LoaderTip") ?? searchRoot.Q<Label>("LoaderTip");

            // Подстановка пустышек вместо недостающих элементов запрещена.
            // Она молча превращала «в разметке нет того, чего ждёт код» в
            // «меню построено», после чего кадр умирал тремя секундами позже
            // на таймауте готовности, и в логе не было ни одного имени, за
            // которое можно зацепиться. Отказ называет всё недостающее сразу.
            var missing = new List<string>();
            if (_container == null) { missing.Add("LoaderContainer"); }
            if (_content == null) { missing.Add("LoaderContent"); }
            if (progressFill == null) { missing.Add("LoaderProgressFill"); }
            if (phaseLabel == null) { missing.Add("LoaderPhaseLabel"); }
            if (phaseCount == null) { missing.Add("LoaderPhaseCount"); }
            if (phaseList == null) { missing.Add("LoaderPhaseList"); }
            if (explain == null) { missing.Add("LoaderExplain"); }
            if (_retry == null) { missing.Add("RetryDescentButton"); }
            if (_tip == null) { missing.Add("LoaderTip"); }
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "[MainMenu] В Resources/UI/Menu/Templates/MainMenu.uxml нет элементов, которых ждёт загрузчик: " +
                    string.Join(", ", missing) +
                    ". Имя или тип элемента в разметке разошлись с кодом.");
            }

            _progress = new MenuLoaderProgress(
                progressFill,
                phaseLabel,
                phaseCount,
                phaseList,
                explain!,
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
            StartTip();
        }

        public void Hide()
        {
            UIState.Hide(_container);
            StopTip();
        }

        public void Dispose() => StopTip();

        // Совет — текст сервера (LoadingTipPacket): показывается последний присланный, смену решает сервер
        private void StartTip()
        {
            if (_tipSource == null)
            {
                return;
            }

            _tipSource.Changed -= ShowTip;
            _tipSource.Changed += ShowTip;
            ShowTip();
        }

        private void StopTip()
        {
            if (_tipSource != null)
            {
                _tipSource.Changed -= ShowTip;
            }
        }

        private void ShowTip()
        {
            if (_tipSource == null || _tip == null)
            {
                return;
            }

            string text = _tipSource.Tip;
            UIState.SetHidden(_tip, text.Length == 0);
            _tip.text = text;
            UILocalizer.ApplyLineHeight(_tip);
        }

        public void Reset()
        {
            _hiddenAtDone = false;
            HasFailure = false;
            _progress?.ResetFailure();
            _retry?.AddToClassList("is-hidden");
        }

        // Игроку — отказ словами макета и «Повторить»; причина — в лог
        public void ShowFailure(Exception exception)
        {
            HasFailure = true;
            UnityEngine.Debug.LogException(exception);
            _progress?.ShowFailure();
            _retry?.RemoveFromClassList("is-hidden");
        }

        public void UpdateProgress(WorldLoadPhase phase, AssetSyncProgress assetSync, Action onDone)
        {
            _progress?.UpdateProgress(phase, assetSync);

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
