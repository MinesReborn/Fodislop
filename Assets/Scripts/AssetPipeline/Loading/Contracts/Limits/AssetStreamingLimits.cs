#nullable enable

namespace Kern.Core;

public static class AssetStreamingLimits
{
    public const int RequestBatchIntervalMilliseconds = 50;
    // Запросы ассетов копятся и уходят одной пачкой раз в RequestBatchIntervalMilliseconds: при четырёх за раз сотни файлов
    // манифеста шли по кругу «пачка — ответ — пачка». Больше — риск упереться в таймаут запроса на медленной связи
    public const int MaximumConcurrentManifestAssetDownloads = 16;
    public const int MaximumConcurrentCellTextureLoads = 2;
    public const int AssetRequestTimeoutSeconds = 5;
    public const int LargeAssetRequestTimeoutSeconds = 10;
    public const long AssetCacheCapacityBytes = 256L * 1024 * 1024;
    public const long DecodedAssetCacheCapacityBytes = 256L * 1024 * 1024;

    // Картинки приходят от сервера и по ссылкам из его окон. Крупнейшая своя
    // картинка — 2496 по стороне, крупнейший ассет сервера — PNG 320×320
    // на 270 КБ. Без пределов ответ на гигабайт держался бы в памяти
    // целиком, а PNG 16384×16384 весом в килобайты распаковывался бы в
    // гигабайт RGBA.
    public const int MaximumWebAssetBytes = 16 * 1024 * 1024;
    public const int MaximumEncodedImageSide = 4096;
}
