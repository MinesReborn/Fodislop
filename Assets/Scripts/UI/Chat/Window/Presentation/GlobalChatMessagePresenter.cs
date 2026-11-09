#nullable enable

using System;
using Kern.UI.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Chat;

internal sealed class GlobalChatMessagePresenter
{
    // Порог «игрок у дна»: ~две строки сообщений, прощает дробные раскладки
    // и мелкий недокрут после предыдущей автопрокрутки.
    private const float BottomStickThreshold = 32f;

    // Свёрнутый чат показывает три последние строки канала
    private const int PeekLines = 3;

    private readonly ChatMessageHistory _history = new();
    private readonly System.Collections.Generic.List<string> _peek = new();
    private VisualElement? _peekLines;
    private ScrollView? _scrollView;
    private Action<long>? _messageClicked;

    // Строку сообщения можно кликнуть: клик по чужой строке
    // открывает серверное окно отправителя (кнопка «Личное сообщение»).
    // По ревью PR передаётся MessageId — отправитель резолвится сервером
    // по идентификатору сообщения, а не по PlayerId.
    public void Attach(
        ScrollView? scrollView,
        Action<long>? messageClicked = null)
    {
        _scrollView = scrollView;
        _messageClicked = messageClicked;
        Render();
    }

    private void RenderPeek()
    {
        if (_peekLines == null)
        {
            return;
        }

        _peekLines.Clear();
        foreach (string line in _peek)
        {
            var label = new Label(line) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("chat-peek-line");
            _peekLines.Add(label);
        }
    }

    public void Detach()
    {
        _peekLines = null;
        _scrollView = null;
        _messageClicked = null;
    }

    public void AttachPeek(VisualElement? peekLines)
    {
        _peekLines = peekLines;
        RenderPeek();
    }

    // peek — строка для свёрнутого чата («ник: текст»); системные сообщения туда не попадают.
    // time — «18:42» перед ником, у системной строки пусто
    public void Add(string formattedMessage, long messageId = 0, string? peek = null, string time = "")
    {
        _history.Add(formattedMessage, messageId, time);
        AppendVisible(formattedMessage, messageId, time);
        if (peek != null)
        {
            _peek.Add(peek);
            if (_peek.Count > PeekLines)
            {
                _peek.RemoveAt(0);
            }

            RenderPeek();
        }
    }

    public void Clear()
    {
        _history.Clear();
        _peek.Clear();
        RenderPeek();
        Render();
    }

    public void Render()
    {
        if (_scrollView == null)
        {
            return;
        }

        _scrollView.Clear();
        foreach ((string formatted, long messageId, string time) in _history.GetMessages())
        {
            AppendVisible(formatted, messageId, time);
        }
    }

    private void AppendVisible(string formattedMessage, long messageId, string time)
    {
        if (_scrollView == null)
        {
            return;
        }

        // Липкое дно, как в старом клиенте (fixScroll): автопрокрутка только
        // если игрок и так был внизу. Позиция читается ДО добавления, по
        // последней завершённой раскладке; прокрутившего историю вверх игрока
        // к дну не дёргаем.
        bool stickToBottom = IsAtBottom();

        // Строка — контейнер: текст и время рядом. Время нельзя класть внутрь Label: у текстового элемента с детьми
        // UI Toolkit не меряет текст, строка получала нулевую высоту и строки ленты налезали друг на друга
        var line = new VisualElement();
        line.AddToClassList("chat-line-row");
        if (messageId > 0 && _messageClicked != null)
        {
            Action<long> clicked = _messageClicked;
            line.AddToClassList("chat-line--clickable");
            line.RegisterCallback<ClickEvent>(_ => clicked(messageId));
        }

        _scrollView.Add(line);

        // Ряд: время моно (колонка 45 px с зазором, макет .chat-log li time) и текст строки с шагом 1.45 кегля
        if (time.Length > 0)
        {
            var timeLabel = new Label(time) { pickingMode = PickingMode.Ignore };
            timeLabel.AddToClassList("chat-line__time");
            line.Add(timeLabel);
        }

        var label = new Label(formattedMessage);
        label.AddToClassList("chat-line");
        label.AddToClassList("lh-145");
        UILocalizer.ApplyLineHeight(label);
        line.Add(label);

        while (_scrollView.childCount > ChatMessageHistory.MaxMessages)
        {
            _scrollView.RemoveAt(0);
        }

        // ScrollTo при незавершённой раскладке сам откладывает прокрутку тик
        // за тиком до устояния layout (прямой scrollOffset клампится слайдером
        // по ещё не обновлённым highLimit и залипает) и отменяется, если
        // игрок начал скроллить сам.
        if (stickToBottom)
        {
            _scrollView.ScrollTo(line);
        }
    }

    private bool IsAtBottom()
    {
        if (_scrollView?.contentViewport == null)
        {
            return true;
        }

        float maxScroll = Mathf.Max(
            0f,
            _scrollView.contentContainer.layout.height - _scrollView.contentViewport.layout.height);
        return _scrollView.scrollOffset.y >= maxScroll - BottomStickThreshold;
    }

    // Принудительный доскролл к последнему сообщению: при открытии окна и
    // после загрузки истории. Сообщения, пришедшие в скрытое окно (при заходе
    // на сервер чат закрыт), прокрутить нельзя - layout для display: none не
    // считается, поэтому окно доскролливает при показе. Deferred-механизм
    // ScrollTo сам дождётся, когда раскладка устоится.
    public void ScrollToBottom()
    {
        if (_scrollView == null)
        {
            return;
        }

        VisualElement content = _scrollView.contentContainer;
        if (content.childCount == 0)
        {
            return;
        }

        _scrollView.ScrollTo(content[content.childCount - 1]);
    }
}
