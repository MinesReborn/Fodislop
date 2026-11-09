#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.AssetPipeline.Loading;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.UI.Controls;
using Kern.UI.Motion;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.UI.Screens;

namespace Kern.UI.HUD;

public sealed class PlayerHUDMissionPanel
{
    private const int MissionResetHour = 18;
    private const string TimerMacro = "%T%";
    private const int MissionCells = 10;
    // Картинка миссии — слева от названия и задания; размер от сервера, не больше строки на две (макет: .mission-img)
    private const float ImageMax = 40f;

    private readonly ILocalizationService _loc;
    private readonly IAssetLoader _assetLoader;
    private readonly IWebAssetLoader _webAssetLoader;
    private readonly IAsyncOperationSupervisor _operations;

    private VisualElement? _missionPanel;
    private Label? _missionTitleLabel;
    private VisualElement? _missionImage;
    private Label? _missionDescLabel;
    private VisualElement? _missionCells;
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
        _missionCells = root.Q<VisualElement>("MissionCells");
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

        // Ход задания — 10 ячеек (макет: .mission-cells); счёт «3 / 10», при Max = 1 его нет
        float pct = stats.MissionMaxProgress > 0 ? (float)stats.MissionProgress / stats.MissionMaxProgress : 0f;
        if (_missionCells != null)
        {
            KernQuant.Set(_missionCells, MissionCells, Mathf.FloorToInt(Mathf.Clamp01(pct) * MissionCells));
        }

        if (_missionProgressLabel != null)
        {
            bool counted = stats.MissionMaxProgress > 1;
            _missionProgressLabel.text = counted
                ? KernNumbers.Group(stats.MissionProgress) + " / " + KernNumbers.Group(stats.MissionMaxProgress)
                : string.Empty;
            _missionProgressLabel.EnableInClassList("is-hidden", !counted);
        }
    }

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
            _missionPanel?.RemoveFromClassList("has-img");
            return;
        }

        float w = stats.MissionImageWidth, h = stats.MissionImageHeight;
        float k = Mathf.Min(1f, ImageMax / Mathf.Max(1f, Mathf.Max(w, h)));
        _missionImage.style.width = w * k;
        _missionImage.style.height = h * k;
        UIState.Show(_missionImage);
        _missionPanel?.AddToClassList("has-img");

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
            texture = await ServerImageSource.LoadAsync(_assetLoader, _webAssetLoader, uri, token);
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

        _missionDescLabel.text = _timerDescription.Replace(
            TimerMacro,
            FormatTimeUntilReset(),
            StringComparison.Ordinal);
    }

    private string FormatTimeUntilReset()
    {
        DateTime localNow = DateTime.Now;
        DateTime nextReset = localNow.Date.AddHours(MissionResetHour);
        if (localNow >= nextReset)
        {
            nextReset = nextReset.AddDays(1);
        }

        TimeSpan remaining = nextReset - localNow;
        return _loc.Get("hud.mission.reset_in", remaining.Hours, remaining.Minutes);
    }

    private void StopTimer()
    {
        _timerSchedule?.Pause();
        _timerSchedule = null;
        _timerDescription = null;
    }
}
