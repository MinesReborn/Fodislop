#nullable enable

using System;
using System.Threading;
using Kern.AssetPipeline.Loading;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.UI.Controls;
using MinesServer.Networking.Server.Packets.GUI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

using Kern.UI.Input;
using Kern.UI.Screens;

namespace Kern.UI.Windows;

public class ModalWindowHandler : IDisposable
{
    private readonly UIDocument _doc;
    private readonly UIInputManager _uiInput;
    private readonly IAssetLoader _assets;
    private readonly IWebAssetLoader _web;
    private readonly IAsyncOperationSupervisor _operations;
    private CancellationTokenSource? _iconLoad;
    private VisualElement? _overlay;
    private VisualElement? _icon;
    private Label? _title;
    private Label? _desc;
    private Button? _okButton;
    private IVisualElementScheduledItem? _keyWatcher;

    public ModalWindowHandler(
        UIDocument doc,
        UIInputManager uiInput,
        IAssetLoader assets,
        IWebAssetLoader web,
        IAsyncOperationSupervisor operations)
    {
        _doc = doc;
        _uiInput = uiInput;
        _assets = assets;
        _web = web;
        _operations = operations;
    }

    /// <summary>
    /// Закрытие окна с клавиатуры.
    ///
    /// Клавиши читаются из планировщика панели, а не из KeyDownEvent: пока у
    /// панели нет клавиатурного фокуса, UI Toolkit событие не доставляет, и
    /// Escape уходил только в PauseMenu. Нажатие открывало паузу поверх
    /// заблокированного ввода, а окно оставалось: IInputBlocker.IsModalShowing
    /// держал IsInputBlocked в true, и ни движение, ни локальный чат не
    /// отвечали. Проверено тестом на игре.
    /// </summary>
    private void PollKeys()
    {
        if (!IsShowing || Keyboard.current == null)
        {
            return;
        }

        bool escape = Keyboard.current.escapeKey.wasPressedThisFrame;
        bool enter = Keyboard.current.enterKey.wasPressedThisFrame ||
                     Keyboard.current.numpadEnterKey.wasPressedThisFrame;
        if (!escape && !enter)
        {
            return;
        }

        // Escape сейчас принадлежит чату: перехватывать его у сфокусированного
        // поля нельзя.
        if (escape && _uiInput.IsChatFocused)
        {
            return;
        }

        // Метка нужна, чтобы меню паузы не открылось тем же нажатием: порядок
        // Update между окном и паузой не задан.
        if (escape)
        {
            _uiInput.ConsumeEscape();
        }

        Hide();
    }

    public void Show(ModalWindowPacket packet)
    {
        EnsureCreated();
        VisualElement overlay = _overlay ??
            throw new InvalidOperationException("[ModalWindowHandler] Modal overlay was not created.");

        // Контент биндится, а не строится: по пакету меняются только
        // текст и видимость иконки.
        ShowIcon(packet.IconURI);
        _title!.text = packet.Title;
        _desc!.text = packet.Description;
        _okButton!.text = packet.ButtonText;

        UIState.Show(overlay);
        overlay.SetEnabled(true);
        overlay.pickingMode = PickingMode.Position;
        WatchKeys(overlay);
    }

    // Картинка сообщения (IconURI пакета): до загрузки места нет; пустой адрес — картинки нет
    private void ShowIcon(string? uri)
    {
        _iconLoad?.Cancel();
        _iconLoad?.Dispose();
        _iconLoad = null;
        VisualElement icon = _icon!;
        icon.style.backgroundImage = StyleKeyword.Null;
        UIState.Hide(icon);
        if (string.IsNullOrEmpty(uri))
        {
            return;
        }

        var load = new CancellationTokenSource();
        _iconLoad = load;
        _operations.Run("modal_window_icon", async supervisorToken =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(load.Token, supervisorToken);
            Texture2D? texture = await ServerImageSource.LoadAsync(_assets, _web, uri, linked.Token);
            if (!linked.Token.IsCancellationRequested && texture != null)
            {
                icon.style.backgroundImage = new StyleBackground(texture);
                UIState.Show(icon);
            }
        });
    }

    public bool IsShowing => _overlay != null && !UIState.IsHidden(_overlay);

    public void Hide()
    {
        _keyWatcher?.Pause();
        if (_overlay != null)
        {
            UIState.Hide(_overlay);
            _overlay.SetEnabled(false);
            _overlay.pickingMode = PickingMode.Ignore;
        }
    }

    // Планировщик панели работает каждый кадр и не зависит от клавиатурного
    // фокуса. На паузе оверлея он бы не крутился, поэтому item и создаётся
    // вместе с оверлеем, а не заранее.
    private void WatchKeys(VisualElement overlay)
    {
        if (_doc.rootVisualElement.panel == null)
        {
            // The document may still be detached during scene teardown; the
            // next show attempt will schedule the watcher after attachment.
            return;
        }

        _keyWatcher = _keyWatcher ??
            overlay.schedule.Execute(PollKeys).Every(16);
        _keyWatcher.Resume();
    }

    /// <summary>
    /// Оверлей убирается из дерева. Панель документа переживает сцену, поэтому
    /// оставшийся элемент жил бы до конца приложения, и следующий обработчик
    /// создал бы второй оверлей поверх него.
    /// </summary>
    public void Dispose()
    {
        _keyWatcher?.Pause();
        _keyWatcher = null;
        _iconLoad?.Cancel();
        _iconLoad?.Dispose();
        _iconLoad = null;
        _overlay?.RemoveFromHierarchy();
        _overlay = null;
        _icon = null;
        _title = null;
        _desc = null;
        _okButton = null;
    }

    private void EnsureCreated()
    {
        if (_overlay != null)
        {
            if (_doc.rootVisualElement != null && !_doc.rootVisualElement.Contains(_overlay))
            {
                _doc.rootVisualElement.Add(_overlay);
            }

            return;
        }

        // Статическая структура (оверлей, панель, иконка, заголовок,
        // описание, кнопка OK) живёт в ModalWindow.uxml. Оверлей — это
        // вложенный VisualElement, а не TemplateContainer: класс is-hidden и
        // все правила висят именно на нём. Если переключать скрытие на
        // контейнере, окно остаётся с display:none навсегда, а IsShowing
        // возвращает true с момента создания — то есть невидимое окно
        // блокирует весь ввод, и закрыть его нечем.
        VisualTreeAsset template = Resources.Load<VisualTreeAsset>(
            ResourcePaths.ModalWindowUxml) ??
            throw new InvalidOperationException(
                "[ModalWindowHandler] Resources/UI/Windows/Modal/Templates/ModalWindow.uxml is required.");
        TemplateContainer tree = template.Instantiate();
        tree.AddToClassList("ui-fullscreen");
        tree.pickingMode = PickingMode.Ignore;

        _overlay = tree.Q<VisualElement>("ModalOverlay") ??
            throw new InvalidOperationException(
                "[ModalWindowHandler] ModalOverlay is missing from ModalWindow.uxml.");
        var overlay = _overlay;

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

        overlay.SetEnabled(false);

        // Оверлей цепляется к корню панели напрямую, а не через TemplateContainer:
        // Instantiate заворачивает UXML в обёртку без размеров, единственный её
        // ребёнок — абсолютный (вне потока), обёртка схлопывается в ноль, и
        // «top/bottom: 0» центруют окно по нулевой линии у верха экрана
        // (репорт: MessageBox уезжает вверх за пределы экрана). Прямой ребёнок
        // корня растягивается на весь экран, и окно встаёт по центру.
        overlay.RemoveFromHierarchy();
        _doc.rootVisualElement.Add(overlay);
    }
}
