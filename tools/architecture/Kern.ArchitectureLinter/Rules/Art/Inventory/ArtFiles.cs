#nullable enable

namespace Kern.ArchitectureLinter.Rules.Art;

/// <summary>
/// Чтение арта без картинковой библиотеки: для метрики хватает заголовка IHDR.
/// </summary>
internal static class ArtFiles
{
    public static (int Width, int Height)? TryReadSize(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[24];
            stream.ReadExactly(header);
            if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47)
            {
                return null;
            }

            int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return (width, height);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IEnumerable<string> EnumeratePngs(params string[] roots)
    {
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
            {
                yield return file;
            }
        }
    }
}
