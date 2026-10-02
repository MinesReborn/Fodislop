#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Core;

/// <summary>
/// Прогрев состояний графического конвейера по трассе, записанной движком.
///
/// Состояние конвейера определяется не одним шейдером: в него входят проход,
/// набор ключевых слов, смешивание, формат цели и раскладка вершины. Поэтому
/// прежний прогрев — список шейдеров, который вёлся руками, и треугольник в
/// цель 4×4 — строил не те состояния, что нужны кадру, и настоящие всё равно
/// рождались в первом кадре игры.
///
/// Набор записывает сам движок. <see cref="GraphicsStateCollection"/> ведёт
/// трассу реально созданных состояний, лежит в проекте обычным ассетом и едет
/// в сборку вместе с ним. В трассе лежит описание, а не скомпилированный
/// конвейер, поэтому она переживает и смену машины, и смену драйвера: у
/// игрока конвейеры строятся на месте, просто заранее и по списку.
///
/// Обслуживания руками нет. Правка коэффициента внутри шейдера набор не
/// меняет — это uniform, нового состояния он не порождает. Правка структуры
/// (проход, ключевое слово, смешивание) делает старую запись несовпадающей, и
/// нужное состояние дописывается в трассу в том сеансе, где его впервые
/// вызвали.
///
/// Записывать трассу умеют только редактор и проверочные сборки — в релизе
/// движок этого не поддерживает. Поэтому у игрока цикл замыкает не трасса, а
/// сбор промахов: состояния, которых в привезённой коллекции не хватило,
/// движок складывает в <c>cacheMissCollection</c>.
/// </summary>
public sealed class ShaderWarmupService : IShaderWarmupService, IDisposable
{
    // Сколько состояний строить за один заход. Прогресс считается по реально
    // построенным состояниям, а не по номеру итерации, поэтому полоса
    // загрузки впервые показывает работу.
    private const int WarmupBatchSize = 8;

    // Бюджет кадра на прогрев. Прежний цикл пропускал кадр после каждого
    // элемента: двадцать кадров по ~120 мс на раннем старте, и секундомер
    // мерил это ожидание, а не работу.
    private const double FrameBudgetMilliseconds = 6.0;

    private GraphicsStateCollection? _collection;
    private bool _closed;

#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
    private GraphicsStateCollection? _sessionTrace;
#endif

    public async UniTask WarmupAsync(
        Action<string, float>? progressCallback,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        _collection = Resources.Load<GraphicsStateCollection>(
            ProjectRuntimeContracts.ResourcePaths.GraphicsStateCollection);

        int warmedStates = 0;
        if (_collection != null)
        {
            string currentQuality = GetCurrentQualityName();
            if (!string.IsNullOrEmpty(_collection.qualityLevelName) &&
                !string.Equals(
                    _collection.qualityLevelName,
                    currentQuality,
                    StringComparison.Ordinal))
            {
                // Unity warns that a GraphicsStateCollection is inaccurate when
                // its recorded quality differs from the active one. Do not feed
                // that collection into the warmup API: it only burns startup
                // time and still leaves the real variants cold.
                Debug.LogWarning(
                    $"[ShaderWarmup] Skipping collection recorded for quality " +
                    $"'{_collection.qualityLevelName}' while active quality is " +
                    $"'{currentQuality}'.");
            }
            else
            {
                warmedStates = await WarmUpAsync(_collection, progressCallback, cancellationToken);
            }
        }
        else
        {
            // Коллекции ещё нет. Это не поломка: состояния построятся в тех
            // кадрах, где понадобятся, ровно как до всякого прогрева. Но у
            // игрока это заикания, поэтому молчать об этом нельзя.
            Debug.LogWarning(
                "[ShaderWarmup] No graphics state collection at " +
                $"'Resources/{ProjectRuntimeContracts.ResourcePaths.GraphicsStateCollection}'. " +
                "Pipeline states will be built on demand, in the frame that needs them.");
        }

        // Compute-ядра компилируются при загрузке ассета; трасса их не ловит.
        // Прежний цикл звал FindKernel, который лишь ищет индекс по имени, и
        // пропускал кадр ради каждого такого поиска.
        _ = Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute);
        _ = Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.PostProcessCompute);

        BeginSessionTrace();
        Application.quitting += Close;

        Debug.Log(
            $"[ShaderWarmup] Primed {warmedStates} graphics state(s) in " +
            $"{stopwatch.ElapsedMilliseconds} ms.");
        progressCallback?.Invoke("Ready", 1.0f);
    }

    private static string GetCurrentQualityName()
    {
        string[] qualityNames = QualitySettings.names;
        int qualityIndex = QualitySettings.GetQualityLevel();
        return qualityIndex >= 0 && qualityIndex < qualityNames.Length
            ? qualityNames[qualityIndex]
            : string.Empty;
    }

    private static async UniTask<int> WarmUpAsync(
        GraphicsStateCollection collection,
        Action<string, float>? progressCallback,
        CancellationToken cancellationToken)
    {
        int total = collection.totalGraphicsStateCount;
        var frameTimer = System.Diagnostics.Stopwatch.StartNew();
        int previousCompleted = -1;

        while (!collection.isWarmedUp)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Заход, не давший прироста, означает, что остаток трассы этому
            // устройству не подходит — например, коллекция записана под
            // другой графический API. Крутиться дальше незачем.
            int completed = collection.completedWarmupCount;
            if (completed == previousCompleted)
            {
                Debug.LogWarning(
                    $"[ShaderWarmup] The collection stalled at {completed}/{total} state(s); " +
                    "the rest does not apply to this device.");
                break;
            }

            previousCompleted = completed;

            // Порция дожидается своего задания: конвейер обязан существовать
            // к моменту, когда его ждут, иначе прогрев становится обещанием,
            // а заикание остаётся в кадре.
            JobHandle handle = collection.WarmUpProgressively(WarmupBatchSize, default);
            handle.Complete();

            if (frameTimer.Elapsed.TotalMilliseconds < FrameBudgetMilliseconds)
            {
                continue;
            }

            frameTimer.Restart();
            progressCallback?.Invoke(
                "Warming graphics states",
                total > 0 ? (float)collection.completedWarmupCount / total : 1.0f);
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        return collection.completedWarmupCount;
    }

    private void BeginSessionTrace()
    {
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        var trace = new GraphicsStateCollection
        {
            graphicsDeviceType = SystemInfo.graphicsDeviceType,
            runtimePlatform = Application.platform,
            qualityLevelName = GetCurrentQualityName(),
        };

        if (!trace.BeginTrace())
        {
            Debug.LogWarning(
                "[ShaderWarmup] The graphics device refused a state trace; " +
                "this session will not extend the collection.");
            return;
        }

        _sessionTrace = trace;
#endif
    }

    // Коллекция дополняется, а не переписывается: один сеанс обходит не весь
    // мир, и перезапись схлопнула бы набор до того, что успели посмотреть.
    private void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Application.quitting -= Close;

        GraphicsStateCollection? collection = _collection;

#if UNITY_EDITOR
        // Коллекции ещё нет: набор рождается из первой же записанной трассы и
        // ложится сразу по своему пути. Переносить файл руками не нужно —
        // иначе служба не умела бы завестись без человека.
        if (collection == null)
        {
            BootstrapCollectionAsset();
            return;
        }
#endif

        if (collection == null)
        {
            return;
        }

        // Промахи собирает движок и в релизной сборке тоже — это то, чем
        // замыкается цикл у игрока, там где трассировки нет.
        if (collection.isTracingCacheMisses)
        {
            GraphicsStateCollection misses = collection.cacheMissCollection;
            if (misses.totalGraphicsStateCount > 0)
            {
                Debug.Log(
                    $"[ShaderWarmup] {misses.totalGraphicsStateCount} graphics state(s) " +
                    "were missing from the collection this session.");
                collection.Append(misses);
                collection.EraseCacheMissCollection();
            }
        }

#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        if (_sessionTrace != null)
        {
            GraphicsStateCollection trace = _sessionTrace;
            _sessionTrace = null;
            trace.EndTrace();
            collection.Append(trace);
        }
#endif

#if UNITY_EDITOR
        // В редакторе набор возвращается прямо в проект, чтобы уехать в
        // сборку с обычным коммитом. Никакого переноса файлов руками.
        string assetPath = UnityEditor.AssetDatabase.GetAssetPath(collection);
        if (!string.IsNullOrEmpty(assetPath) && collection.SaveToFile(assetPath))
        {
            ImportWhenEditing(assetPath);
            Debug.Log(
                $"[ShaderWarmup] Wrote {collection.totalGraphicsStateCount} " +
                $"graphics state(s) to '{assetPath}'.");
        }
#endif
    }

#if UNITY_EDITOR
    // Путь собирается из того же контракта, по которому ассет потом грузится:
    // разъехаться им нельзя.
    private const string CollectionAssetRoot = "Assets/Resources";

    // Close срабатывает, когда редактор выходит из Play Mode. Импорт в этот
    // момент заставлял AssetDatabase подхватить все изменения на диске, включая
    // правленые скрипты: перекомпиляция и перезагрузка домена шли посреди
    // выхода из игры, и сценарий Play Mode ловил «Mismatched state in
    // DefaultScenario, was Stopping but expected Idle». Файл пишется сразу,
    // а импорт ждёт режима редактирования.
    private static void ImportWhenEditing(string assetPath)
    {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
        {
            UnityEditor.EditorApplication.delayCall += () => ImportWhenEditing(assetPath);
            return;
        }

        UnityEditor.AssetDatabase.ImportAsset(assetPath);
    }

    private void BootstrapCollectionAsset()
    {
        GraphicsStateCollection? trace = _sessionTrace;
        _sessionTrace = null;
        if (trace == null)
        {
            return;
        }

        trace.EndTrace();
        if (trace.totalGraphicsStateCount == 0)
        {
            return;
        }

        string assetPath = System.IO.Path.Combine(
            CollectionAssetRoot,
            ProjectRuntimeContracts.ResourcePaths.GraphicsStateCollection + ".graphicsstate");
        string? directory = System.IO.Path.GetDirectoryName(assetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        if (!trace.SaveToFile(assetPath))
        {
            Debug.LogWarning(
                $"[ShaderWarmup] Could not create the graphics state collection at '{assetPath}'.");
            return;
        }

        ImportWhenEditing(assetPath);
        Debug.Log(
            $"[ShaderWarmup] Created the graphics state collection at '{assetPath}' " +
            $"with {trace.totalGraphicsStateCount} state(s) from this session.");
    }
#endif

    public void Dispose()
    {
        Close();
    }
}
