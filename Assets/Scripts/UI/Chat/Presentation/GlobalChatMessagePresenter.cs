#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

internal sealed class GlobalChatMessagePresenter
{
    private readonly ChatMessageHistory _history = new();
    private ScrollView? _scrollView;

    public void Attach(ScrollView? scrollView)
    {
        _scrollView = scrollView;
        Render();
    }

    public void Detach()
    {
        _scrollView = null;
    }

    public void Add(string formattedMessage)
    {
        _history.Add(formattedMessage);
        AppendVisible(formattedMessage);
    }

    public void Render()
    {
        if (_scrollView == null)
        {
            return;
        }

        _scrollView.Clear();
        foreach (string message in _history.GetMessages())
        {
            AppendVisible(message);
        }
    }

    private void AppendVisible(string formattedMessage)
    {
        if (_scrollView == null)
        {
            return;
        }

        var label = new Label(formattedMessage);
        label.AddToClassList("gchat-message");
        _scrollView.Add(label);
        while (_scrollView.childCount > ChatMessageHistory.MaxMessages)
        {
            _scrollView.RemoveAt(0);
        }

        _scrollView.scrollOffset = new Vector2(0, float.MaxValue);
    }
}
