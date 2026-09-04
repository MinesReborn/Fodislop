#nullable enable

using System;
using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Core;

/// <summary>
/// Откладывает запись конфига, пока игрок тянет ползунок.
/// </summary>
/// <remarks>
/// ЗАЧЕМ. <c>ClientConfigManager.Save</c> валидирует конфиг целиком и пишет
/// файл с fsync и подменой через временный. На каждое движение ползунка это
/// десятки таких записей в секунду.
///
/// ПОЧЕМУ ОТДЕЛЬНЫМ ТИПОМ. Раньше эти четыре строки жили внутри
/// <c>LightingConfigHolder</c> и обслуживали только свет: любая другая
/// вкладка настроек била в диск напрямую. Дебаунс — это своя
/// ответственность, а не подробность освещения, поэтому он вынесен настоящим
/// типом, а не спрятан в частичный класс.
/// </remarks>
public sealed class ConfigSaveScheduler(IClientConfigManager clientConfig)
{
    /// <summary>
    /// Окно ожидания. Четверть секунды — заметно длиннее интервала между
    /// кадрами перетаскивания и заметно короче паузы, после которой игрок
    /// вправе закрыть игру и ожидать, что настройка сохранена.
    /// </summary>
    public const float DebounceSeconds = 0.25f;

    private readonly IClientConfigManager _clientConfig = clientConfig ??
        throw new ArgumentNullException(nameof(clientConfig));

    private bool _pending;
    private float _dueTime;

    public void Queue()
    {
        _pending = true;
        _dueTime = Time.unscaledTime + DebounceSeconds;
    }

    /// <summary>Пишет, если окно ожидания истекло. Возвращает факт записи.</summary>
    public bool TryFlush(float currentTime)
    {
        if (!_pending || currentTime < _dueTime)
        {
            return false;
        }

        _pending = false;
        Debug.Log("[ConfigSaveScheduler] Debounce expired; flushing config to disk.");
        _clientConfig.Save();
        return true;
    }

    /// <summary>
    /// Пишет немедленно, не дожидаясь окна.
    /// </summary>
    /// <remarks>
    /// Нужен на выходе из игры: правка, сделанная в последнюю четверть
    /// секунды, иначе не доехала бы до диска никогда.
    /// </remarks>
    public void Flush()
    {
        if (!_pending)
        {
            return;
        }

        _pending = false;
        Debug.Log("[ConfigSaveScheduler] Immediate flush requested; saving config to disk.");
        _clientConfig.Save();
    }
}
