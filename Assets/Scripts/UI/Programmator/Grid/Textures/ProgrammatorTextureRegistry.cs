#nullable enable

using System;
using System.Collections.Generic;
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
}

// Иконки операторов вшиты в клиент (Resources/Programmator/Icons) и по сети не приходят.
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

    private readonly Dictionary<ProgAction, ProgGlyph> _localCache = new();
    private readonly List<Texture2D> _runtimeCopies = new();

    public ProgGlyph? GetGlyph(ProgAction action)
    {
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

    private ProgGlyph PrepareGlyph(Texture2D source)
    {
        RuntimeTextureFactory.ApplySampling(
            source,
            FilterMode.Point,
            TextureWrapMode.Clamp);

        // Не лезем в суперсэмплинг, если расширенная копия не влезает в
        // лимит GPU (для текущего арта ≤31px запас большой).
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
