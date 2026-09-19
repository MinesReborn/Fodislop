#nullable enable

using System;
using Kern.Core.Interfaces;
using UnityEngine;

namespace Kern.Audio.Core;
public sealed class AudioPlaybackHandle : IAudioPlaybackHandle
{
    public AudioBusType BusType { get; }
    public FMOD.Studio.EventInstance EventInstance { get; }

    public bool IsPlaying
    {
        get
        {
            if (!EventInstance.isValid())
            {
                return false;
            }

            EventInstance.getPlaybackState(out var state);
            return state != FMOD.Studio.PLAYBACK_STATE.STOPPED;
        }
    }

    public AudioPlaybackHandle(FMOD.Studio.EventInstance instance, AudioBusType busType)
    {
        EventInstance = instance;
        BusType = busType;
    }

    public void Stop(float fadeOut = 0f)
    {
        if (!EventInstance.isValid())
        {
            return;
        }

        var mode = fadeOut > 0f ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE;
        EventInstance.stop(mode);
        EventInstance.release();
    }

    public void SetPosition(Vector3 worldPosition)
    {
        if (!EventInstance.isValid())
        {
            return;
        }

        EventInstance.set3DAttributes(new FMOD.ATTRIBUTES_3D
        {
            position = new FMOD.VECTOR { x = worldPosition.x, y = worldPosition.y, z = 0f },
            forward = new FMOD.VECTOR { x = 0f, y = 0f, z = 1f },
            up = new FMOD.VECTOR { x = 0f, y = 1f, z = 0f },
        });
    }

    public void SetVolume(float linearVolume)
    {
        if (!EventInstance.isValid())
        {
            return;
        }

        EventInstance.setVolume(Mathf.Max(0f, linearVolume));
    }

    public void SetPitch(float pitch)
    {
        if (!EventInstance.isValid())
        {
            return;
        }

        EventInstance.setPitch(Mathf.Clamp(pitch, 0.01f, 4f));
    }

    public void SetParameter(string parameterName, float value)
    {
        if (!EventInstance.isValid() || string.IsNullOrEmpty(parameterName))
        {
            return;
        }

        EventInstance.setParameterByName(parameterName, value);
    }
}
