#nullable enable

using System;
using Fodinae.Core;
using MinesServer.Networking.Server.Packets.GUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    public class ModalWindowHandler
    {
        private readonly UIDocument _doc;
        private VisualElement? _overlay;
        private VisualElement? _icon;
        private Label? _title;
        private Label? _desc;
        private Button? _okButton;

        public ModalWindowHandler(UIDocument doc)
        {
            _doc = doc;
        }

        public void Show(ModalWindowPacket packet)
        {
            EnsureCreated();

            // Контент биндится, а не строится: по пакету меняются только
            // текст и видимость иконки.
            UIState.SetHidden(_icon, string.IsNullOrEmpty(packet.IconURI));
            _title!.text = packet.Title;
            _desc!.text = packet.Description;
            _okButton!.text = packet.ButtonText;

            UIState.Show(_overlay!);
            var overlay = _overlay!;
            overlay.SetEnabled(true);
            overlay.pickingMode = PickingMode.Position;
        }

        public bool IsShowing => _overlay != null && !UIState.IsHidden(_overlay);

        public void Hide()
        {
            if (_overlay != null)
            {
                UIState.Hide(_overlay);
                _overlay.SetEnabled(false);
                _overlay.pickingMode = PickingMode.Ignore;
            }
        }

        private void EnsureCreated()
        {
            if (_overlay != null)
            {
                return;
            }

            // Статическая структура (оверлей, панель, иконка, заголовок,
            // описание, кнопка OK) живёт в ModalWindow.uxml; здесь только клон
            // и биндинги. Начальная скрытость — в разметке (style="display: none").
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>(
                ProjectRuntimeContracts.ResourcePaths.ModalWindowUxml) ??
                throw new InvalidOperationException(
                    "[ModalWindowHandler] Resources/UI/ModalWindow.uxml is required.");
            TemplateContainer tree = template.Instantiate();
            _overlay = tree;

            _icon = tree.Q<VisualElement>("ModalIcon") ??
                throw new InvalidOperationException(
                    "[ModalWindowHandler] ModalIcon is missing from ModalWindow.uxml.");
            _title = tree.Q<Label>("ModalTitle") ??
                throw new InvalidOperationException(
                    "[ModalWindowHandler] ModalTitle is missing from ModalWindow.uxml.");
            _desc = tree.Q<Label>("ModalDesc") ??
                throw new InvalidOperationException(
                    "[ModalWindowHandler] ModalDesc is missing from ModalWindow.uxml.");
            _okButton = tree.Q<Button>("ModalOkButton") ??
                throw new InvalidOperationException(
                    "[ModalWindowHandler] ModalOkButton is missing from ModalWindow.uxml.");
            _okButton.clicked += Hide;

            _overlay.SetEnabled(false);
            _doc.rootVisualElement.Add(_overlay);
        }
    }
}
