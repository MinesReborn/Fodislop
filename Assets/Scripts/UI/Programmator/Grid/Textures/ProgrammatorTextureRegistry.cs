#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

using Kern.AssetPipeline.Loading;

namespace Kern.UI.Programmator;

// Иконка оператора для отрисовки: Texture — расходник для backgroundImage
// (Point, Clamp), Width/Height — исходные размеры арта в пикселях оператора;
// ими раскладывается глиф в слоте (SetNativeSize старого клиента), сама
// текстура может быть расширена (см. Supersample в реестре).
public readonly record struct ProgGlyph(Texture2D Texture, int Width, int Height);

public interface IProgrammatorTextureCatalog
{
    ProgGlyph? GetGlyph(ProgAction action);

    // Серверная версия иконки догрузилась: видимые ячейки и палитра
    // перерисовывают иконки.
    event Action? ServerTexturesChanged;
}

// Иконки операторов приходят с сервера через IAssetLoader (файлы вида
// 'programmator/166.png' — тот же канал, что VisualEffect/Items/Robot), а не из
// билда клиента: сервер владеет контентом операторов. Пока файл не загружен
// (или сервер недоступен), GetGlyph отдаёт локальную копию из Resources,
// чтобы окно открывалось мгновенно; серверная версия подменяет её по факту
// загрузки и поднимает ServerTexturesChanged.
public sealed class ProgrammatorTextureRegistry : IProgrammatorTextureCatalog, IDisposable
{
    // Панель рендерится с дробным масштабом (PanelSettings:
    // ScaleWithScreenSize 1920×1080, а окно произвольного размера → скейл
    // вроде 1.96). При Point-фильтрации однотексельные линии арта (штрих
    // стрелки «→» у пустой ячейки) на части субпиксельных фаз строки не
    // попадают ни в один экранный пиксель и пропадают целыми строками сетки.
    // Расширяем арт nearest-ом в 3 раза: линии толщиной 1 тексель становятся
    // толщиной 3 и выживают при любой фазе, а края остаются пиксельными.
    // Геометрия глифа при этом не меняется — UI раскладывает иконку по
    // исходным Width/Height.
    private const int Supersample = 3;

    private readonly IAssetLoader _assetLoader;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly Dictionary<ProgAction, ProgGlyph> _localCache = new();
    private readonly Dictionary<ProgAction, ProgGlyph> _serverCache = new();
    private readonly HashSet<ProgAction> _fetchStarted = new();
    private readonly List<Texture2D> _runtimeCopies = new();
    private readonly CancellationTokenSource _lifetime = new();

    public event Action? ServerTexturesChanged;

    public ProgrammatorTextureRegistry(IAssetLoader assetLoader, IAsyncOperationSupervisor operations)
    {
        _assetLoader = assetLoader ?? throw new ArgumentNullException(nameof(assetLoader));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    public ProgGlyph? GetGlyph(ProgAction action)
    {
        if (_serverCache.TryGetValue(action, out var serverGlyph))
        {
            return serverGlyph;
        }

        if (_fetchStarted.Add(action))
        {
            _operations.Run($"programmator_glyph_{(int)action}", token => FetchFromServer(action, token));
        }

        if (_localCache.TryGetValue(action, out var localGlyph))
        {
            return localGlyph;
        }

        string? resourcePath = ProgrammatorIconPaths.Resolve(action);
        if (resourcePath == null)
        {
            return null;
        }

        Texture2D? tex = Resources.Load<Texture2D>(resourcePath);
        if (tex == null)
        {
            return null;
        }

        localGlyph = PrepareGlyph(tex);
        _localCache[action] = localGlyph;
        return localGlyph;
    }

    // Провал (файла нет, сервер недоступен) снимает отметку: следующее
    // обновление UI повторит запрос, уже существующие ответы «нет такого
    // файла» отсекает IsKnownMissing у диспетчера.
    private async UniTask FetchFromServer(ProgAction action, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        string filename = $"programmator/{(int)action}.png";
        try
        {
            if (_assetLoader.IsKnownMissing(filename))
            {
                return;
            }

            Texture2D? texture = await _assetLoader.GetTextureAsync(
                filename,
                linked.Token);
            if (texture == null || _lifetime.IsCancellationRequested)
            {
                _fetchStarted.Remove(action);
                return;
            }

            _serverCache[action] = PrepareGlyph(texture);
            ServerTexturesChanged?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Отмена — штатный путь при закрытии скоупа.
        }
        catch (Exception)
        {
            _fetchStarted.Remove(action);
        }
    }

    private ProgGlyph PrepareGlyph(Texture2D source)
    {
        RuntimeTextureFactory.ApplySampling(
            source,
            FilterMode.Point,
            TextureWrapMode.Clamp);

        // Не лезем в суперсэмплинг, если расширенная копия не влезает в
        // лимит GPU (для текущего арта ≤31px запас большой, но сервер
        // владеет контентом и может прислать что угодно).
        bool fitsGpu = source.width * Supersample <= SystemInfo.maxTextureSize &&
                       source.height * Supersample <= SystemInfo.maxTextureSize;
        Texture2D render = fitsGpu ? SupersampleNearest(source) : source;
        return new ProgGlyph(render, source.width, source.height);
    }

    // Blit с Point-сэмплером источника = nearest ×3; ReadPixels забирает
    // результат в собственную Texture2D. Локальные PNG не readable, поэтому
    // CPU-копия (GetPixels32) недоступна — только GPU-путь. sRGB-путь
    // сквозной (текстура → RT → ReadPixels), значения пикселей доходят без
    // пересчёта цветового пространства.
    private Texture2D SupersampleNearest(Texture2D source)
    {
        var descriptor = new RenderTextureDescriptor(
            source.width * Supersample, source.height * Supersample,
            RenderTextureFormat.ARGB32, depthBufferBits: 0)
        {
            sRGB = true,
            useMipMap = false,
            autoGenerateMips = false,
        };
        RenderTexture rt = RenderTexture.GetTemporary(descriptor);
        RenderTexture previous = RenderTexture.active;
        Texture2D? copy = null;
        try
        {
            Graphics.Blit(source, rt);
            copy = RuntimeTextureFactory.CreateRGBA32NoMip(
                rt.width, rt.height, $"{source.name} x{Supersample}",
                RuntimeTextureColorSpace.Srgb, FilterMode.Point, TextureWrapMode.Clamp);
            RenderTexture.active = rt;
            copy.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            copy.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            _runtimeCopies.Add(copy);
            return copy;
        }
        catch
        {
            if (copy != null)
            {
                UnityEngine.Object.Destroy(copy);
            }

            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();

        foreach (Texture2D copy in _runtimeCopies)
        {
            if (copy != null)
            {
                UnityEngine.Object.Destroy(copy);
            }
        }

        _runtimeCopies.Clear();
    }
}
