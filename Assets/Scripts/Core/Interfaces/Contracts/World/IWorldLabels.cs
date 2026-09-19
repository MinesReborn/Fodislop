#nullable enable

using System;
using UnityEngine;

namespace Kern.Core.Interfaces;

public interface IWorldLabels
{
    IWorldLabel Create(bool chatBubble);
}

public interface IWorldLabel : IDisposable
{
    void SetText(string text);
    void SetPosition(Vector3 position);
    void SetVisible(bool visible);
    void SetOpacity(float opacity);
}
