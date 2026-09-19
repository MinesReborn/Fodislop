#nullable enable

using System;
using Kern.Core;
using Kern.Core.Lifecycle;
using Kern.Core.Interfaces;
using UnityEngine;

namespace Kern.Game;

public sealed class RobotNameplate
{
    private IWorldLabel? _nicknameText;
    private Vector3 _lastLabelsPosition;
    private bool _hasUpdatedLabels;

    public void Initialize(
        Transform robotTransform,
        uint botID,
        string nickname,
        bool isLocalPlayer,
        ISceneObjectFactory sceneObjects,
        IWorldLabels labels)
    {
        Transform? existingNickname = robotTransform.Find("Nickname");
        if (isLocalPlayer)
        {
            _nicknameText?.Dispose();
            _nicknameText = null;
            if (existingNickname != null)
            {
                existingNickname.gameObject.SetActive(false);
            }

            return;
        }

        if (existingNickname != null)
        {
            existingNickname.gameObject.SetActive(false);
        }

        _nicknameText ??= labels.Create(chatBubble: false);
        InvalidatePosition();
        _nicknameText.SetVisible(true);
        _nicknameText.SetText(nickname ?? string.Empty);
    }

    public void SetText(string text, bool isLocalPlayer)
    {
        if (_nicknameText != null)
        {
            _nicknameText.SetText(isLocalPlayer ? string.Empty : text);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (_nicknameText != null)
        {
            _nicknameText.SetVisible(enabled);
        }
    }

    public void UpdatePosition(Vector3 robotPosition, Sprite? skinSprite, Transform robotTransform, Transform? clanTransform)
    {
        if (_hasUpdatedLabels &&
            (robotPosition - _lastLabelsPosition).sqrMagnitude <= 1e-8f)
        {
            return;
        }

        if (_nicknameText != null)
        {
            Vector3 topRight = new(robotPosition.x + 0.5f, robotPosition.y + 0.5f, robotPosition.z);
            _nicknameText.SetPosition(topRight);
        }

        if (clanTransform != null)
        {
            clanTransform.SetPositionAndRotation(robotPosition + new Vector3(0.6f, -0.5f, 0f), Quaternion.identity);
        }

        _lastLabelsPosition = robotPosition;
        _hasUpdatedLabels = true;
    }

    public void InvalidatePosition()
    {
        _hasUpdatedLabels = false;
    }

    public void Destroy()
    {
        if (_nicknameText != null)
        {
            _nicknameText.Dispose();
            _nicknameText = null;
        }
    }
}
