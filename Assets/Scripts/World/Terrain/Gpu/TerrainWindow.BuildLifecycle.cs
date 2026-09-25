#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.Diagnostics;
using Kern.Core.Lifecycle;
using Kern.World.Streaming;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain
{
    /// <summary>
    /// Async build lifecycle for TerrainWindow: scheduling, completion acceptance,
    /// generation/size validation, and same-frame publication state transitions.
    /// </summary>
    internal sealed class TerrainWindowBuildLifecycle : IDisposable
    {
        private readonly TerrainBuildDriver _driver = new();
        private readonly TerrainWindowChangeJournal _changes = new();
        private readonly TerrainWindowPublishedView _publishedView = new();
        private readonly TerrainWindowBuildRequestScheduler _requestScheduler;

        private bool _wasCpuMeshRebuildBypassed;
        private long _worldGeneration;
        private long _buildOldestChangeTimestamp;
        private TerrainBuildCompletion<TerrainCpuBuildRequest, TerrainCpuBuildResult>? _heldCompletion;
        private float _preparationLatencySeconds = StreamingPolicy.DefaultPreparationLatencySeconds;

        public TerrainWindowBuildLifecycle()
        {
            _requestScheduler = new(_driver, _changes, _publishedView);
        }

        public TerrainBuildDriver Driver => _driver;

        public TerrainDirtyTracker Dirty => _changes.Dirty;

        public HashSet<CellType> PendingTextureCellTypes => _changes.PendingTextureCellTypes;

        public Mesh? CellIDMesh => _publishedView.CellIDMesh;

        public Vector2Int Origin => _publishedView.Origin;

        public int Width => _publishedView.Width;

        public int Height => _publishedView.Height;

        public bool IsInitialized => _publishedView.IsInitialized;

        public bool CellsCommitted => _publishedView.CellsCommitted;

        public ulong PublishedContentRevision => _publishedView.PublishedContentRevision;

        public bool NeedsRefresh
        {
            get => _changes.NeedsRefresh;
            set => _changes.NeedsRefresh = value;
        }

        public bool HasOrigin => _publishedView.HasOrigin;

        public bool HasCpuBuildInFlight => _requestScheduler.IsBusy;

        public bool HasUnpublishedTextureRefresh => _requestScheduler.HasUnpublishedTextureRefresh;

        public TerrainBuildState BuildState { get; private set; } = TerrainBuildState.WaitingForData;

        public float EstimatedPreparationSeconds => _preparationLatencySeconds;

        private Vector2Int BuildOrigin => _requestScheduler.ActiveRequest?.Origin ?? HeldOrigin ?? Origin;

        public bool HoldPublication { get; set; }

        public Vector2Int? HeldOrigin => _heldCompletion?.Request.Origin;

        public Vector2Int ProspectiveOrigin => HeldOrigin ?? Origin;

        public void Attach(
            Transform transform,
            ISceneObjectFactory sceneObjects,
            string sortingLayerName,
            int doorOverlaySortingOrder,
            float cellSize)
        {
            _publishedView.Attach(
                transform,
                sceneObjects,
                sortingLayerName,
                doorOverlaySortingOrder,
                cellSize,
                _driver);
        }

        public void ApplyDimensions(Vector2Int size, bool dimensionsChanged)
        {
            if (!dimensionsChanged && IsInitialized)
            {
                return;
            }

            if (_requestScheduler.IsBusy)
            {
                _requestScheduler.Cancel();
                return;
            }

            _publishedView.WithdrawPublication(_driver);
            _heldCompletion = null;
            _publishedView.ApplyDimensions(size, _driver);
            _changes.ClearDirty();
            NeedsRefresh = true;
        }

        public void CoalesceDirtyRects()
        {
            _changes.RebuildAllCells = _changes.ShouldCoalesceDirtyRects(BuildOrigin, Width, Height);
        }

        public bool RecordWorldChange(
            int serverX,
            int serverY,
            int width,
            int height,
            int worldHeight)
        {
            TerrainWindowWorldChangeResult result = _changes.RecordWorldChange(
                serverX,
                serverY,
                width,
                height,
                worldHeight,
                HasOrigin,
                _requestScheduler.IsBusy,
                BuildOrigin,
                Width,
                Height);
            return result != TerrainWindowWorldChangeResult.None;
        }

        public bool IsNearBuildWindow(RectInt unityRect, int marginCells)
        {
            return _changes.IsNearBuildWindow(
                unityRect,
                marginCells,
                IsInitialized,
                HasOrigin,
                _requestScheduler.IsBusy,
                BuildOrigin,
                Width,
                Height);
        }

        public void InvalidateWorld()
        {
            _worldGeneration++;
            FrameEventLog.Record("террейн: смена мира");
            _requestScheduler.Cancel();
            _heldCompletion = null;
            NeedsRefresh = true;
            _changes.ClearWorldChanges();
            _changes.ClearChangedRegions();
            WithdrawPublication();
        }

        public void RequestDistortion(bool enabled)
        {
            _changes.RequestDistortion(enabled);
        }

        public void RequestDistortionStyle(TerrainDistortionStyle style)
        {
            _changes.RequestDistortionStyle(style);
        }

        public void TakePublishedChangedRegions(List<RectInt> destination)
        {
            _changes.TakePublishedChangedRegions(destination);
        }

        public float Commit() => _publishedView.Commit(_driver);

        public void Dispose()
        {
            _requestScheduler.Dispose();
            _publishedView.Dispose();
            _driver.Dispose();
        }

        /// <summary>
        /// Забрать готовый шаг, если он есть. Зовётся в начале кадра, до
        /// планирования: тогда планировщик видит уже новое начало окна, и
        /// следующий шаг ставится в этом же кадре, а не кадром позже. Возвращает
        /// false при отказе сборки.
        /// </summary>
        ///
        /// Сразу после публикации вызывающий обязан выгрузить тексели (Commit) в
        /// том же кадре: начало окна и двери уже новые.
        public bool TryPublishCompleted(in TerrainBuildServices services, out Exception? failure)
        {
            failure = null;

            // Удержанный шаг публикуется, только пока рабочий поток свободен:
            // идущий шаг уже пишет в те же массивы конвейера. Тогда удержанный
            // заменит этот шаг — он тоже собран целиком.
            if (!HoldPublication && !_requestScheduler.IsBusy && _heldCompletion is { } held)
            {
                _heldCompletion = null;
                if (!Complete(services, held, out failure))
                {
                    return false;
                }
            }

            if (!_requestScheduler.IsBusy ||
                !_requestScheduler.TryTakeCompleted(
                    out TerrainBuildCompletion<TerrainCpuBuildRequest, TerrainCpuBuildResult> completion))
            {
                return true;
            }

            services.Telemetry.TerrainBuildInFlight = 0;
            if (HoldPublication && completion.Result != null &&
                completion.Request.WorldGeneration == _worldGeneration)
            {
                // Удерживается только последний готовый шаг: каждый шаг перехода
                // собирает окно целиком, и новый заменяет прежний без остатка.
                _heldCompletion = completion;
                _requestScheduler.MarkCompletionHasNoTextureRefresh();
                BuildState = TerrainBuildState.WaitingForPublication;
                return true;
            }

            _heldCompletion = null;
            return Complete(services, completion, out failure);
        }

        /// <summary>
        /// Поставить следующий шаг к запрошенному началу, если окно свободно и
        /// есть что делать. Возвращает false при отказе сборки.
        /// </summary>
        public bool Process(
            in TerrainBuildServices services,
            IClientConfigManager clientConfigManager,
            Vector2Int requestedOrigin,
            bool dimensionsChanged,
            bool bypassCpuMeshRebuild,
            MeshRenderer? meshRenderer,
            ulong contentRevision,
            out Exception? failure)
        {
            failure = null;
            services.Telemetry.TerrainBuildInFlight = _requestScheduler.IsBusy ? 1 : 0;
            if (_requestScheduler.IsBusy)
            {
                BuildState = TerrainBuildState.CpuPreparing;
                return true;
            }

            if (bypassCpuMeshRebuild)
            {
                _wasCpuMeshRebuildBypassed = true;
                BuildState = TerrainBuildState.Canceled;
                return true;
            }

            if (_wasCpuMeshRebuildBypassed)
            {
                _wasCpuMeshRebuildBypassed = false;
                NeedsRefresh = true;
            }

            bool rebuild = requestedOrigin != ProspectiveOrigin || NeedsRefresh || dimensionsChanged ||
                !Dirty.IsEmpty || PendingTextureCellTypes.Count > 0;
            if (!rebuild)
            {
                BuildState = _heldCompletion != null ? TerrainBuildState.WaitingForPublication
                    : CellsCommitted ? TerrainBuildState.Published
                    : TerrainBuildState.WaitingForData;
                return true;
            }

            var intent = new TerrainBuildSchedulingIntent(
                requestedOrigin,
                dimensionsChanged,
                meshRenderer,
                contentRevision,
                _worldGeneration,
                HoldPublication,
                _heldCompletion != null,
                NeedsRefresh,
                HasOrigin,
                Origin,
                new Vector2Int(Width, Height));
            TerrainBuildSchedulingResult result = _requestScheduler.Schedule(
                services,
                clientConfigManager,
                intent,
                out failure,
                out long oldestChangeTimestamp);
            if (result == TerrainBuildSchedulingResult.WaitingForData)
            {
                BuildState = TerrainBuildState.WaitingForData;
                return false;
            }

            if (result == TerrainBuildSchedulingResult.Failed)
            {
                BuildState = TerrainBuildState.Error;
                return false;
            }

            _buildOldestChangeTimestamp = oldestChangeTimestamp;
            BuildState = TerrainBuildState.CpuPreparing;
            services.Telemetry.TerrainBuildInFlight = 1;
            return true;
        }

        private bool Complete(
            in TerrainBuildServices services,
            in TerrainBuildCompletion<TerrainCpuBuildRequest, TerrainCpuBuildResult> completion,
            out Exception? failure)
        {
            failure = null;
            TerrainCpuBuildRequest request = completion.Request;
            _requestScheduler.MarkCompletionHasNoTextureRefresh();
            List<RectInt> changedRegions = _changes.BuildChangedRegions;

            if (completion.Result is not { } result)
            {
                // Шаг прерван между стадиями: кэш, предрасчёт, заливка и тексели
                // больше не согласованы, и следующий шаг обязан собрать окно
                // целиком. Изменения шага возвращаются в очередь освещения.
                NeedsRefresh = true;
                _changes.ChangedRegions.AddRange(changedRegions);
                changedRegions.Clear();
                _changes.RestoreOldestChangeTimestamp(_buildOldestChangeTimestamp);

                _buildOldestChangeTimestamp = 0;
                if (completion.WasCanceled)
                {
                    services.Telemetry.TerrainBuildCancelCount++;
                    BuildState = TerrainBuildState.Canceled;
                    return true;
                }

                BuildState = TerrainBuildState.Error;
                failure = completion.Failure;
                return false;
            }

            if (request.WorldGeneration != _worldGeneration ||
                request.Size != new Vector2Int(Width, Height))
            {
                // Шаг досчитан по миру или размеру, которых больше нет.
                NeedsRefresh = true;
                BuildState = TerrainBuildState.Canceled;
                return true;
            }

            if (!_driver.TryContinueBuild(services, out TerrainBuildContext context))
            {
                // Атласы пропали между постановкой и публикацией (сброс набора
                // текстур): тексели ссылаются на них, публиковать нельзя.
                NeedsRefresh = true;
                WithdrawPublication();
                BuildState = TerrainBuildState.WaitingForData;
                return true;
            }

            float latencySeconds = _requestScheduler.BuildStartElapsedSeconds;
            try
            {
                _driver.Publish(context, request, result, latencySeconds * 1000f);
            }
            catch (Exception exception)
            {
                BuildState = TerrainBuildState.Error;
                failure = exception;
                return false;
            }

            RectInt lightingBounds = _publishedView.Publish(request);
            for (int index = 0; index < changedRegions.Count; index++)
            {
                _changes.AddPublishedChangedRegion(changedRegions[index], lightingBounds);
            }

            changedRegions.Clear();
            BuildState = TerrainBuildState.Published;

            ObservePreparationLatency(latencySeconds);
            services.Telemetry.TerrainWorkerBuildMs = result.ElapsedMs;
            services.Telemetry.TerrainBuildLatencyMs = latencySeconds * 1000f;
            if (_buildOldestChangeTimestamp != 0)
            {
                services.Telemetry.TerrainEditDisplayLatencyMs = (float)(
                    (System.Diagnostics.Stopwatch.GetTimestamp() - _buildOldestChangeTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                _buildOldestChangeTimestamp = 0;
            }

            return true;
        }

        private void WithdrawPublication()
        {
            _publishedView.WithdrawPublication(_driver);
        }

        private void ObservePreparationLatency(float observedSeconds)
        {
            if (float.IsNaN(observedSeconds) || float.IsInfinity(observedSeconds) || observedSeconds < 0f)
            {
                return;
            }

            // Верхняя граница — чтобы одна холодная сборка при входе в мир не
            // раздувала опережение на всю дальнейшую ходьбу.
            float clamped = Mathf.Clamp(observedSeconds, 0.005f, 2f);
            _preparationLatencySeconds = Mathf.Lerp(_preparationLatencySeconds, clamped, 0.25f);
        }
    }
}
