#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Networking;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using VContainer;
using Stopwatch = System.Diagnostics.Stopwatch;
using Kern.Networking.Gateways;
using Kern.Networking.Status;
using Kern.UI.Localization;
using Kern.UI.Screens;

namespace Kern.UI.Menu;

internal sealed class MenuRepairPresenter
{
    private readonly ILocalizationService _loc;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly IMenuFileDiagnosticsService _fileDiagnostics;
    private bool _teardownStarted;

    internal MenuRepairPresenter(ILocalizationService localization,
        IAsyncOperationSupervisor operations, IMenuFileDiagnosticsService fileDiagnostics)
    {
        _loc = localization;
        _operations = operations;
        _fileDiagnostics = fileDiagnostics;
    }

    internal void Bind(VisualElement tree)
    {
        _repairProgressBlock = tree.Q<VisualElement>("RepairProgressBlock");
        _repairProgressFill = tree.Q<VisualElement>("RepairProgressFill");
        _repairProgressPercent = tree.Q<Label>("RepairProgressPercent");
        _repairLogLines = tree.Q<VisualElement>("RepairLogLines");
        _runFullRepairButton = tree.Q<Button>("RunFullRepairButton");
    }

    internal void Subscribe()
    {
        if (_runFullRepairButton != null)
        {
            _runFullRepairButton.clicked -= OnRunFullDiagnosticsClicked;
            _runFullRepairButton.clicked += OnRunFullDiagnosticsClicked;
        }
    }

    internal void Dispose()
    {
        _teardownStarted = true;
        if (_runFullRepairButton != null)
        {
            _runFullRepairButton.clicked -= OnRunFullDiagnosticsClicked;
        }
    }


        // Живая проверка файлов в окне «Диагностика и целостность файлов»: при
        // открытии запускает IMenuFileDiagnosticsService, рисует статус каждой
        // проверки и общий прогресс; «Перепроверить кэш» прогоняет заново.
        internal VisualElement? _repairProgressBlock;
        internal VisualElement? _repairProgressFill;
        internal Label? _repairProgressPercent;
        internal VisualElement? _repairLogLines;
        internal Label? _repairSummaryLine;
        internal Button? _runFullRepairButton;
        internal MenuFileDiagnosticsReport? _lastDiagnosticsReport;
        internal bool _diagnosticsInFlight;
        internal bool _repairProgressShown;




        // ── Окно «Диагностика и целостность файлов» ──

        internal const string RepairStatusOkKey = "mainmenu.repair_status_ok";
        internal const string RepairStatusWarningKey = "mainmenu.repair_status_warning";
        internal const string RepairStatusFailedKey = "mainmenu.repair_status_failed";
        internal const string RepairStatusRunningKey = "mainmenu.repair_status_running";
        internal const string RepairStatusPendingKey = "mainmenu.repair_status_pending";

        internal void OnRepairModalOpened()
        {
            if (_diagnosticsInFlight)
            {
                return;
            }

            ApplyRepairTexts();
            _operations.Run("menu_file_diagnostics", token => RunDiagnosticsAsync(false, token));
        }

        internal void OnRunFullDiagnosticsClicked()
        {
            if (_diagnosticsInFlight)
            {
                return;
            }

            _operations.Run("menu_file_diagnostics_full", token => RunDiagnosticsAsync(true, token));
        }

        internal async UniTask RunDiagnosticsAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            if (_diagnosticsInFlight || _loc == null || _repairLogLines == null)
            {
                return;
            }

            _diagnosticsInFlight = true;
            _repairProgressShown = false;
            _runFullRepairButton?.SetEnabled(false);
            ResetRepairLines();
            try
            {
                MenuFileDiagnosticsReport report = await _fileDiagnostics.RunAllAsync(
                    activeCheck: MarkRepairLineRunning,
                    fraction: UpdateRepairProgress,
                    forceRefresh,
                    cancellationToken);

                if (_teardownStarted)
                {
                    return;
                }

                _lastDiagnosticsReport = report;
                RenderRepairReport(report);
            }
            catch (OperationCanceledException)
            {
                // Меню закрывается — тихо выходим.
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MainMenu] File diagnostics failed: {exception}");
                if (!_teardownStarted && _repairSummaryLine != null)
                {
                    SetRepairLine(_repairSummaryLine, _loc.Get("mainmenu.repair_run_failed"), "mm-log-line--error");
                }
            }
            finally
            {
                _diagnosticsInFlight = false;
                _repairProgressShown = false;
                _runFullRepairButton?.SetEnabled(true);
                UIState.Hide(_repairProgressBlock);
            }
        }

        /// <summary>Строки проверок: создать по числу ключей сервиса плюс строка итога.</summary>
        internal void EnsureRepairLines()
        {
            if (_repairLogLines == null || _fileDiagnostics == null)
            {
                return;
            }

            if (_repairLogLines.childCount == 0)
            {
                for (int index = 0; index < _fileDiagnostics.CheckLabelKeys.Count; index++)
                {
                    var line = new Label { name = $"RepairCheckLine{index}" };
                    line.AddToClassList("mm-log-line");
                    _repairLogLines.Add(line);
                }

                _repairSummaryLine = new Label { name = "RepairSummaryLine" };
                _repairSummaryLine.AddToClassList("mm-log-line");
                _repairSummaryLine.AddToClassList("mm-log-line--pending");
                _repairLogLines.Add(_repairSummaryLine);
            }
            else if (_repairSummaryLine == null)
            {
                _repairSummaryLine = (Label)_repairLogLines[_repairLogLines.childCount - 1];
            }
        }

        /// <summary>Все строки — в состояние ожидания: проверки ещё не дошли до них.</summary>
        internal void ResetRepairLines()
        {
            if (_repairLogLines == null || _loc == null || _fileDiagnostics == null)
            {
                return;
            }

            EnsureRepairLines();
            int checkCount = Mathf.Min(_fileDiagnostics.CheckLabelKeys.Count, _repairLogLines.childCount - 1);
            for (int index = 0; index < checkCount; index++)
            {
                SetRepairLine(
                    (Label)_repairLogLines[index],
                    $"{_loc.Get(_fileDiagnostics.CheckLabelKeys[index])} — {_loc.Get(RepairStatusPendingKey)}",
                    "mm-log-line--pending");
            }

            if (_repairSummaryLine != null)
            {
                SetRepairLine(_repairSummaryLine, string.Empty, "mm-log-line--pending");
            }
        }

        internal void MarkRepairLineRunning(int index)
        {
            ShowRepairProgressOnce();
            if (_repairLogLines == null || _loc == null ||
                index < 0 || index >= _repairLogLines.childCount - 1)
            {
                return;
            }

            SetRepairLine(
                (Label)_repairLogLines[index],
                $"{_loc.Get(_fileDiagnostics.CheckLabelKeys[index])} — {_loc.Get(RepairStatusRunningKey)}",
                "mm-log-line--running");
        }

        internal void UpdateRepairProgress(float fraction)
        {
            ShowRepairProgressOnce();
            float clamped = Mathf.Clamp01(fraction);
            if (_repairProgressFill != null)
            {
                _repairProgressFill.style.width = new Length(clamped * 100f, LengthUnit.Percent);
            }

            if (_repairProgressPercent != null)
            {
                _repairProgressPercent.text = $"{Mathf.RoundToInt(clamped * 100f)}%";
            }
        }

        internal void ShowRepairProgressOnce()
        {
            if (_repairProgressShown || _repairProgressBlock == null)
            {
                return;
            }

            _repairProgressShown = true;
            UIState.Show(_repairProgressBlock);
        }

        internal void RenderRepairReport(MenuFileDiagnosticsReport report)
        {
            if (_repairLogLines == null || _loc == null || _fileDiagnostics == null)
            {
                return;
            }

            EnsureRepairLines();
            int checkCount = Mathf.Min(report.Checks.Count, _repairLogLines.childCount - 1);
            for (int index = 0; index < checkCount; index++)
            {
                MenuFileCheckResult check = report.Checks[index];
                SetRepairLine(
                    (Label)_repairLogLines[index],
                    BuildRepairLineText(check.LabelKey, check.Status, check.Detail),
                    RepairStatusClass(check.Status));
            }

            if (_repairSummaryLine == null)
            {
                return;
            }

            int failures = 0;
            int warnings = 0;
            foreach (MenuFileCheckResult check in report.Checks)
            {
                switch (check.Status)
                {
                    case MenuFileCheckStatus.Failed:
                        failures++;
                        break;
                    case MenuFileCheckStatus.Warning:
                        warnings++;
                        break;
                }
            }

            if (report.AllOk)
            {
                SetRepairLine(_repairSummaryLine, _loc.Get("mainmenu.repair_all_ok"), "mm-log-line--success");
            }
            else if (failures > 0)
            {
                SetRepairLine(
                    _repairSummaryLine,
                    _loc.Get("mainmenu.repair_summary_problems", failures),
                    "mm-log-line--error");
            }
            else
            {
                SetRepairLine(
                    _repairSummaryLine,
                    _loc.Get("mainmenu.repair_summary_warnings", warnings),
                    "mm-log-line--warning");
            }
        }

        /// <summary>Тексты окна после ApplyLocalizedText и при переоткрытии: отчёт или ожидание.</summary>
        internal void ApplyRepairTexts()
        {
            if (_repairLogLines == null || _loc == null || _fileDiagnostics == null)
            {
                return;
            }

            if (_lastDiagnosticsReport is { } report)
            {
                RenderRepairReport(report);
            }
            else if (!_diagnosticsInFlight)
            {
                ResetRepairLines();
            }
        }

        internal string BuildRepairLineText(string labelKey, MenuFileCheckStatus status, string detail)
        {
            if (_loc == null)
            {
                throw new InvalidOperationException("Menu localization must be injected before rendering diagnostics.");
            }

            string statusKey = status switch
            {
                MenuFileCheckStatus.Ok => RepairStatusOkKey,
                MenuFileCheckStatus.Warning => RepairStatusWarningKey,
                _ => RepairStatusFailedKey,
            };

            string text = $"{_loc.Get(labelKey)} — {_loc.Get(statusKey)}";
            return string.IsNullOrWhiteSpace(detail) ? text : $"{text} ({detail})";
        }

        internal static string RepairStatusClass(MenuFileCheckStatus status) => status switch
        {
            MenuFileCheckStatus.Ok => "mm-log-line--success",
            MenuFileCheckStatus.Warning => "mm-log-line--warning",
            _ => "mm-log-line--error",
        };

        internal void SetRepairLine(Label line, string text, string modifierClass)
        {
            line.text = text;
            line.RemoveFromClassList("mm-log-line--success");
            line.RemoveFromClassList("mm-log-line--error");
            line.RemoveFromClassList("mm-log-line--warning");
            line.RemoveFromClassList("mm-log-line--running");
            line.RemoveFromClassList("mm-log-line--pending");
            line.AddToClassList(modifierClass);
        }

}
