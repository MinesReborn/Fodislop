#nullable enable

using System;
using System.Threading;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.UI.Controls;
using MinesServer.Networking.Client.Packets.Chat;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Kern.UI;

/// <summary>
/// Поле ввода локального (proximity) чата: T открывает, Enter отправляет
/// <c>SendLocalChatMessagePacket</c> и закрывает поле, Escape закрывает без
/// отправки. Сервер рассылает такое сообщение всем игрокам в квадрате 5x5
/// чанков вокруг отправителя, а облако над роботом рисует FloatingChatManager.
///
/// Клавиши и панель принадлежат этому типу, но тик вызывает владелец —
/// FloatingChatManager: он уже стоит на сцене, а введение ещё одного
/// MonoBehaviour потребовало бы правки сцены.
/// </summary>
internal sealed class LocalChatInput : IDisposable
{
    private const string Tag = "[LocalChatInput]";

    private readonly UIDocument _doc;
    private readonly INetworkService _networkService;
    private readonly IInputBlocker _inputBlocker;
    private readonly UIInputManager _uiInput;
    private readonly ChatBlinkController _blink;

    private VisualElement? _tree;
    private VisualElement? _panel;
    private TextField? _field;
    private ChatInputBlinker? _blinker;
    private bool _isOpen;
    private bool _ownsChatFocus;

    public LocalChatInput(
        UIDocument doc,
        INetworkService networkService,
        IInputBlocker inputBlocker,
        UIInputManager uiInput,
        IAsyncOperationSupervisor operations,
        CancellationToken destroyToken)
    {
        _doc = doc;
        _networkService = networkService;
        _inputBlocker = inputBlocker;
        _uiInput = uiInput;

        // Контроллер каретки создаётся до дерева: он читает _blinker лениво, но
        // readonly-поле _blink должно быть проинициализировано до первого
        // обращения из колбэков, которые регистрирует CreateUI.
        _blink = new ChatBlinkController(operations, destroyToken, () => _blinker);
        CreateUI();
        if (_panel != null)
        {
            _panel.style.display = DisplayStyle.None;
        }
    }

    public bool IsOpen => _isOpen;

    public void Tick()
    {
        if (Keyboard.current == null || _field == null)
        {
            return;
        }

        // Владение вводом сверяется каждый кадр, а не по событию фокуса.
        // FocusEvent UI Toolkit доставляет на обновлении панели, поэтому при
        // быстром чередовании T и Enter он приходил уже после Submit(): Hide()
        // успевала сбросить IsChatFocused, а пришедший следом FocusEvent ставил
        // его обратно на закрытом поле. BlurEvent к тому моменту уже был, и
        // сбрасывать флаг было больше некому — ввод залипал навсегда, и игра
        // жила, но не брала клавиши.
        if (_ownsChatFocus)
        {
            _uiInput.IsChatFocused = _isOpen;
        }

        if (!_isOpen)
        {
            // T открывает локальный чат только когда игровой ввод не занят:
            // сфокусированное поле глобального чата, окно сервера, пауза и
            // программатор поднимают IsInputBlocked.
            if (Keyboard.current.tKey.wasPressedThisFrame && !_inputBlocker.IsInputBlocked)
            {
                Show();
            }

            return;
        }

        if (Keyboard.current.enterKey.wasPressedThisFrame ||
            Keyboard.current.numpadEnterKey.wasPressedThisFrame)
        {
            Submit();
            return;
        }

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            // Escape за кадр обрабатывает ровно один владелец: без метки меню
            // паузы открылось бы тем же нажатием.
            _uiInput.ConsumeEscape();
            Hide();
        }
    }

    public void Show()
    {
        if (_panel == null || _field == null)
        {
            return;
        }

        _isOpen = true;
        _panel.style.display = DisplayStyle.Flex;
        TakeChatFocus();

        // Фокус откладывается на следующий тик панели: клавиша открытия (T,
        // "Е" на русской раскладке) уже обработана вводом в этом кадре, и
        // немедленный Focus() протаскивал бы её символ в текст поля.
        _field.schedule.Execute(() =>
        {
            if (_isOpen && _field != null)
            {
                _field.Focus();
            }
        }).StartingIn(1);
    }

    public void Hide()
    {
        _isOpen = false;
        ReleaseChatFocus();

        if (_panel == null || _field == null)
        {
            return;
        }

        _panel.style.display = DisplayStyle.None;
        _field.value = string.Empty;

        // Blur() идёт после сброса владения, иначе BlurEvent уводит фокус
        // у глобального чата, который к этому моменту мог его уже забрать.
        _field.Blur();
    }

    // Флаг ставится здесь, а не в колбэке FocusEvent: событие приходит
    // отложенно и не гарантирует ничего в момент Show(). Владеет только тот,
    // кто сам поднял панель, поэтому глобальный чат, забравший фокус, не
    // затирается нашим сбросом.
    private void TakeChatFocus()
    {
        _ownsChatFocus = true;
        _uiInput.IsChatFocused = true;
    }

    private void ReleaseChatFocus()
    {
        if (!_ownsChatFocus)
        {
            return;
        }

        _ownsChatFocus = false;
        _uiInput.IsChatFocused = false;
    }

    public void Dispose()
    {
        Hide();
        _blink.Dispose();
        _tree?.RemoveFromHierarchy();
        _tree = null;
        _panel = null;
        _field = null;
        _blinker = null;
    }

    private void CreateUI()
    {
        var uxml = Resources.Load<VisualTreeAsset>(
            ProjectRuntimeContracts.ResourcePaths.LocalChatUxml);
        if (uxml == null)
        {
            throw new InvalidOperationException(
                $"{Tag} Missing {ProjectRuntimeContracts.ResourcePaths.LocalChatUxml} UXML.");
        }

        VisualElement tree = uxml.CloneTree();
        tree.AddToClassList("ui-fullscreen");
        tree.pickingMode = PickingMode.Ignore;

        _panel = tree.Q<VisualElement>("LocalChatPanel");
        _field = tree.Q<TextField>("LocalChatField");
        if (_panel == null || _field == null)
        {
            throw new InvalidOperationException(
                $"{Tag} LocalChat.uxml must contain LocalChatPanel and LocalChatField.");
        }

        _tree = tree;
        _doc.rootVisualElement.Add(tree);

        _field.selectAllOnFocus = false;
        _field.selectAllOnMouseUp = false;

        // Локальный чат сервер не ограничивает по длине (у него нет такого
        // лимита, в отличие от глобального), поэтому клиентский предел — единственный.
        _field.maxLength = ProjectRuntimeContracts.Chat.MaximumLocalChatLength;

        // TextField прячет свою строку ввода в отдельный дочерний элемент с этим
        // классом: без него поле сохраняет рамку и отступы ввода глобального чата.
        var internalInput = _field.Q<VisualElement>(className: "unity-text-field__input");
        if (internalInput != null)
        {
            internalInput.AddToClassList("lchat-internal-input");
            _blinker = new ChatInputBlinker(_field, internalInput);
        }

        _field.RegisterCallback<FocusEvent>(_ =>
        {
            _blink.StartBlink();
            TakeChatFocus();
        });
        _field.RegisterCallback<BlurEvent>(_ =>
        {
            _blink.StopBlink();

            // Фокус забрали, пока панель открыта: серверное окно или другой
            // элемент. Панель закрывается, иначе она висит, а T перестаёт
            // работать — ветка открытия недостижима при _isOpen == true, и
            // поле глотает Enter и Escape впустую.
            if (_isOpen)
            {
                Hide();
                return;
            }

            ReleaseChatFocus();
        });
        _field.RegisterValueChangedCallback(_ => _blink.NotifyInput());
    }

    private void Submit()
    {
        if (_field == null)
        {
            return;
        }

        string text = _field.value.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            int maxLength = ProjectRuntimeContracts.Chat.MaximumLocalChatLength;
            if (text.Length > maxLength)
            {
                text = text[..maxLength];
            }

            try
            {
                _networkService.Send(new SendLocalChatMessagePacket(text));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{Tag} Не удалось отправить локальное сообщение: {ex}");
            }
        }

        // Поле закрывается и на пустой строке: иначе после каждого пустого
        // Enter пришлось бы жать Escape.
        Hide();
    }
}
