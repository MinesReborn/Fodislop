#nullable enable

using Cysharp.Threading.Tasks;

namespace Fodinae.Core.Interfaces
{
    /// <summary>
    /// Единственное место, где решается, откуда игра читает текстуры с диска.
    ///
    /// Часть текстур загружается не через AssetDatabase, а файлами в рантайме:
    /// иконки предметов, тайлы клеток, ассеты, присланные сервером. Путь к ним
    /// в редакторе и в собранном плеере разный, а раньше его независимо угадывали
    /// три подсистемы — TextureStorageManager, MainMenu и ItemRegistry, — каждая
    /// своим списком кандидатов. Из-за этого сборка раскладывала копию каталога
    /// Textures сразу в четыре места, чтобы попасть хоть в один из списков: около
    /// 29 МБ × 4 в каждом билде.
    ///
    /// Теперь корень один и вычисляется один раз. Сборка кладёт текстуры ровно в
    /// StreamingAssets, редактор читает их прямо из Assets.
    /// </summary>
    /// <remarks>
    /// Договор лежит в Fodinae.Contracts отдельно от реализации: им пользуется
    /// Fodinae.AssetPipeline, а тот на Fodinae.Runtime не ссылается и ссылаться
    /// не должен — направление зависимости обратное. Реализация осталась в
    /// Core/Diagnostics/RuntimeAssetPaths.cs.
    /// </remarks>
    public interface IRuntimeAssetPaths
    {
        string BundledTexturesRoot { get; }
        string PersistentTexturesRoot { get; }
        UniTask EnsureReadyAsync();
        string? FindBundledTextureFile(string relativePath);
        string? FindTextureFile(string relativePath);
    }
}
