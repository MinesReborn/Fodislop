using System;
using UnityEngine;

namespace Fodinae.Scripts.Audio.Core
{
    /// <summary>
    /// Хендл активного проигрывания — возвращается методом AudioSystem.Play().
    ///
    /// Оборачивает FMOD.Studio.EventInstance напрямую без C#-делегатов и лишней GC-нагрузки.
    /// Позволяет:
    /// <list type="bullet">
    ///   <item>Остановить звук досрочно: <c>handle.Stop(fadeOut: 0.3f)</c></item>
    ///   <item>Подвинуть позицию: <c>handle.SetPosition(target.position)</c></item>
    ///   <item>Узнать играет ли ещё: <c>handle.IsPlaying</c></item>
    ///   <item>Менять громкость на лету: <c>handle.SetVolume(0.5f)</c></item>
    ///   <item>Менять FMOD параметры: <c>handle.SetParameter("Speed", 1.5f)</c></item>
    /// </list>
    /// </summary>
    public sealed class AudioPlaybackHandle
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

        /// <summary>Остановить с плавным затуханием за fadeOut секунд (если на строен FMOD fadeout).</summary>
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

        /// <summary>Установить позицию в мире (для пространственных звуков).</summary>
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

        /// <summary>Изменить громкость этого конкретного голоса (0..+∞, линейно).</summary>
        public void SetVolume(float linearVolume)
        {
            if (!EventInstance.isValid())
            {
                return;
            }

            EventInstance.setVolume(Mathf.Max(0f, linearVolume));
        }

        /// <summary>Изменить питч этого конкретного голоса.</summary>
        public void SetPitch(float pitch)
        {
            if (!EventInstance.isValid())
            {
                return;
            }

            EventInstance.setPitch(Mathf.Clamp(pitch, 0.01f, 4f));
        }

        /// <summary>Установить значение параметра FMOD события на лету.</summary>
        public void SetParameter(string parameterName, float value)
        {
            if (!EventInstance.isValid() || string.IsNullOrEmpty(parameterName))
            {
                return;
            }

            EventInstance.setParameterByName(parameterName, value);
        }
    }
}
