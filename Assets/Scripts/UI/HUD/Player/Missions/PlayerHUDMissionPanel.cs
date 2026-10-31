#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.AssetPipeline.Loading;
using Kern.UI.Screens;

namespace Kern.UI.HUD;

public sealed class PlayerHUDMissionPanel
{
    // Дневной сброс заданий Рабочего Центра — 18:00 МСК (игровое правило,
    // продублированное на сервере в GJobs/ServerTime). Дедлайна в пакете нет,
    // поэтому клиент считает остаток сам и тикает локально.
    private static readonly TimeZoneInfo MoscowTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
    private const int MissionResetHour = 18;
    private const string TimerMacro = "%T%";

    private readonly ILocalizationService _loc;
    private readonly IAssetLoader _assetLoader;
    private readonly IWebAssetLoader _webAssetLoader;
    private readonly IAsyncOperationSupervisor _operations;

    private VisualElement? _missionPanel;
    private Label? _missionTitleLabel;
    private VisualElement? _missionImage;
    private Label? _missionDescLabel;
    private VisualElement? _missionProgressFill;
    private Label? _missionProgressLabel;

    private IVisualElementScheduledItem? _timerSchedule;
    private string? _timerDescription;
    private string? _loadedImageUri;
    private CancellationTokenSource? _imageCts;

    public PlayerHUDMissionPanel(
        ILocalizationService loc,
        IAssetLoader assetLoader,
        IWebAssetLoader webAssetLoader,
        IAsyncOperationSupervisor operations)
    {
        _loc = loc;
        _assetLoader = assetLoader;
        _webAssetLoader = webAssetLoader;
        _operations = operations;
    }

    public void Initialize(VisualElement root)
    {
        _missionPanel = root.Q<VisualElement>("MissionPanel");
        _missionTitleLabel = root.Q<Label>("MissionTitleLabel");
        _missionImage = root.Q<VisualElement>("MissionImage");
        _missionDescLabel = root.Q<Label>("MissionDescLabel");
        _missionProgressFill = root.Q<VisualElement>("MissionProgressFill");
        _missionProgressLabel = root.Q<Label>("MissionProgressLabel");
    }

    public void Update(IPlayerStats? stats)
    {
        if (_missionPanel == null || stats == null)
        {
            return;
        }

        if (!stats.IsMissionActive)
        {
            UIState.Hide(_missionPanel);
            StopTimer();
            return;
        }

        UIState.Show(_missionPanel);

        if (_missionTitleLabel != null)
        {
            _missionTitleLabel.text = stats.MissionTitle ?? _loc.Get("hud.mission");
        }

        UpdateImage(stats);
        UpdateDescription(stats);

        float pct = stats.MissionMaxProgress > 0 ? (float)stats.MissionProgress / stats.MissionMaxProgress : 0f;
        if (_missionProgressFill != null)
        {
            _missionProgressFill.style.width = new Length(Mathf.Clamp01(pct) * 100, LengthUnit.Percent);
        }

        if (_missionProgressLabel != null)
        {
            _missionProgressLabel.text = $"{stats.MissionProgress:N0}/{stats.MissionMaxProgress:N0}";
        }
    }

    // Картинка миссии — по договорённости ImagePacketBuilder: пустой URI значит
    // «картинки нет» (загрузку не планируем), absolute http(s) идёт в веб-канал,
    // относительный — в серверный asset-канал (M3G). URI кэшируется, чтобы
    // повторные Update не перезагружали ту же картинку.
    private void UpdateImage(IPlayerStats stats)
    {
        if (_missionImage == null)
        {
            return;
        }

        string uri = stats.MissionImageUri ?? string.Empty;
        if (string.Equals(_loadedImageUri, uri, StringComparison.Ordinal))
        {
            return;
        }

        _loadedImageUri = uri;
        _imageCts?.Cancel();
        _imageCts?.Dispose();
        _imageCts = new CancellationTokenSource();

        if (string.IsNullOrWhiteSpace(uri))
        {
            _missionImage.style.backgroundImage = new StyleBackground(StyleKeyword.None);
            _missionImage.style.width = 0;
            _missionImage.style.height = 0;
            UIState.Hide(_missionImage);
            return;
        }

        _missionImage.style.width = stats.MissionImageWidth;
        _missionImage.style.height = stats.MissionImageHeight;
        UIState.Show(_missionImage);

        CancellationToken token = _imageCts.Token;
        VisualElement imageElement = _missionImage;
        _operations.Run(
            $"load_mission_image_{uri}",
            supervisorToken => LoadImageAsync(imageElement, uri, token, supervisorToken));
    }

    private async UniTask LoadImageAsync(
        VisualElement imageElement,
        string uri,
        CancellationToken elementToken,
        CancellationToken supervisorToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            elementToken,
            supervisorToken);
        CancellationToken token = linkedCancellation.Token;
        Texture2D? texture;
        try
        {
            texture = await GetTextureAsync(uri, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MissionPanel] Optional mission image '{uri}' failed to load: {exception.Message}");
            return;
        }

        if (token.IsCancellationRequested || texture == null)
        {
            return;
        }

        imageElement.style.backgroundImage = new StyleBackground(texture);
    }

    private async UniTask<Texture2D?> GetTextureAsync(string uri, CancellationToken token)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? absolute) ||
            (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps))
        {
            return await _assetLoader.GetTextureAsync(uri, token);
        }

        return await _webAssetLoader.GetTextureAsync(uri, token);
    }

    private void UpdateDescription(IPlayerStats stats)
    {
        if (_missionDescLabel == null)
        {
            return;
        }

        string description = stats.MissionDescription ?? string.Empty;
        if (!description.Contains(TimerMacro, StringComparison.Ordinal))
        {
            StopTimer();
            _missionDescLabel.text = description;
            return;
        }

        _timerDescription = description;
        ApplyTimerText();
        _timerSchedule ??= _missionDescLabel.schedule.Execute(ApplyTimerText).Every(1000);
    }

    private void ApplyTimerText()
    {
        if (_missionDescLabel == null || _timerDescription == null)
        {
            return;
        }

        _missionDescLabel.text = _timerDescription.Replace(TimerMacro, FormatTimeUntilReset(), StringComparison.Ordinal);
    }

    private static string FormatTimeUntilReset()
    {
        DateTime mskNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MoscowTimeZone);
        DateTime nextReset = mskNow.Hour < MissionResetHour
            ? new DateTime(mskNow.Year, mskNow.Month, mskNow.Day, MissionResetHour, 0, 0)
            : new DateTime(mskNow.Year, mskNow.Month, mskNow.Day, MissionResetHour, 0, 0).AddDays(1);
        TimeSpan remaining = nextReset - mskNow;
        return $"{remaining.Hours} ч. {remaining.Minutes} мин.";
    }

    private void StopTimer()
    {
        _timerSchedule?.Pause();
        _timerSchedule = null;
        _timerDescription = null;
    }
}
