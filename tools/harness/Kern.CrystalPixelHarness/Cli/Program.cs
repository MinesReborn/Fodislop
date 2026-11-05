using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using StbImageSharp;

namespace Kern.CrystalPixelHarness;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitDifference = 1;
    private const int ExitUsage = 2;

    private static int Main(string[] args)
    {
        if (args.Length < 4 || args[0] != "compare")
        {
            PrintUsage();
            return ExitUsage;
        }

        string referenceDirectory = Path.GetFullPath(args[1]);
        string candidateDirectory = Path.GetFullPath(args[2]);
        string outputDirectory = Path.GetFullPath(args[3]);
        int allowedDifferentPixels = 0;

        for (int argumentIndex = 4; argumentIndex < args.Length; argumentIndex++)
        {
            if (args[argumentIndex] != "--max-different-pixels" ||
                argumentIndex + 1 >= args.Length ||
                !int.TryParse(args[++argumentIndex], out allowedDifferentPixels) ||
                allowedDifferentPixels < 0)
            {
                Console.Error.WriteLine("Некорректный аргумент: " + args[argumentIndex - 1]);
                PrintUsage();
                return ExitUsage;
            }
        }

        try
        {
            ComparisonReport report = Compare(referenceDirectory, candidateDirectory, outputDirectory);
            string reportPath = Path.Combine(outputDirectory, "report.json");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));

            foreach (FrameReport frame in report.Frames)
            {
                Console.WriteLine(
                    $"{frame.Name}: {frame.DifferentPixelCount}/{frame.PixelCount} пикселей отличаются, " +
                    $"max Δ={frame.MaximumChannelDelta}, mean Δ={frame.MeanAbsoluteChannelDelta:F6}");
            }

            Console.WriteLine($"Итого: {report.DifferentPixelCount}/{report.PixelCount} пикселей отличаются.");
            Console.WriteLine($"Теплокарты: {Path.Combine(outputDirectory, "diff")}");
            Console.WriteLine($"Отчёт: {reportPath}");
            return report.DifferentPixelCount <= allowedDifferentPixels ? ExitSuccess : ExitDifference;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            InvalidOperationException)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitUsage;
        }
    }

    private static ComparisonReport Compare(string referenceDirectory, string candidateDirectory, string outputDirectory)
    {
        if (!Directory.Exists(referenceDirectory) || !Directory.Exists(candidateDirectory))
        {
            throw new DirectoryNotFoundException("Не найдена папка исходных или проверяемых кадров.");
        }

        string[] referencePaths = Directory.GetFiles(referenceDirectory, "*.png")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();
        string[] candidatePaths = Directory.GetFiles(candidateDirectory, "*.png")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();

        if (referencePaths.Length == 0 || candidatePaths.Length == 0)
        {
            throw new InvalidDataException("В обеих папках должны быть PNG-кадры.");
        }

        string[] referenceNames = referencePaths.Select(Path.GetFileName).ToArray()!;
        string[] candidateNames = candidatePaths.Select(Path.GetFileName).ToArray()!;
        if (!referenceNames.SequenceEqual(candidateNames, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Наборы кадров не совпадают. Имена файлов должны одинаково задавать порядок и время кадра.");
        }

        string diffDirectory = Path.Combine(outputDirectory, "diff");
        Directory.CreateDirectory(diffDirectory);
        var frames = new List<FrameReport>(referencePaths.Length);

        for (int frameIndex = 0; frameIndex < referencePaths.Length; frameIndex++)
        {
            RgbaImage reference = ReadPng(referencePaths[frameIndex]);
            RgbaImage candidate = ReadPng(candidatePaths[frameIndex]);
            if (reference.Width != candidate.Width || reference.Height != candidate.Height)
            {
                throw new InvalidDataException(
                    $"{referenceNames[frameIndex]}: размер отличается " +
                    $"({reference.Width}x{reference.Height} против {candidate.Width}x{candidate.Height}). " +
                    "Скрытое масштабирование кадров запрещено.");
            }

            FrameReport frame = CompareFrame(referenceNames[frameIndex]!, reference, candidate, diffDirectory);
            frames.Add(frame);
        }

        RgbaImage firstReference = ReadPng(referencePaths[0]);
        return new ComparisonReport
        {
            Width = firstReference.Width,
            Height = firstReference.Height,
            FrameCount = frames.Count,
            PixelCount = frames.Sum(frame => frame.PixelCount),
            DifferentPixelCount = frames.Sum(frame => frame.DifferentPixelCount),
            MaximumChannelDelta = frames.Max(frame => frame.MaximumChannelDelta),
            MeanAbsoluteChannelDelta = frames.Sum(frame => frame.AbsoluteChannelDeltaSum) /
                (double)(frames.Sum(frame => frame.PixelCount) * 4),
            Frames = frames,
        };
    }

    private static FrameReport CompareFrame(
        string name,
        RgbaImage reference,
        RgbaImage candidate,
        string diffDirectory)
    {
        int pixelCount = checked(reference.Width * reference.Height);
        var heatmap = new byte[reference.Pixels.Length];
        var overlay = new byte[reference.Pixels.Length];
        int differentPixels = 0;
        int maximumDelta = 0;
        long absoluteDeltaSum = 0;
        int worstPixelIndex = -1;
        int worstPixelDelta = -1;

        for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
        {
            int byteIndex = pixelIndex * 4;
            int redDelta = Math.Abs(reference.Pixels[byteIndex] - candidate.Pixels[byteIndex]);
            int greenDelta = Math.Abs(reference.Pixels[byteIndex + 1] - candidate.Pixels[byteIndex + 1]);
            int blueDelta = Math.Abs(reference.Pixels[byteIndex + 2] - candidate.Pixels[byteIndex + 2]);
            int alphaDelta = Math.Abs(reference.Pixels[byteIndex + 3] - candidate.Pixels[byteIndex + 3]);
            int pixelDelta = Math.Max(Math.Max(redDelta, greenDelta), Math.Max(blueDelta, alphaDelta));
            int channelDeltaSum = redDelta + greenDelta + blueDelta + alphaDelta;

            if (pixelDelta != 0)
            {
                differentPixels++;
                if (pixelDelta > worstPixelDelta)
                {
                    worstPixelDelta = pixelDelta;
                    worstPixelIndex = pixelIndex;
                }

                heatmap[byteIndex] = 255;
                heatmap[byteIndex + 1] = 0;
                heatmap[byteIndex + 2] = 255;
                heatmap[byteIndex + 3] = 255;
            }

            for (int channel = 0; channel < 4; channel++)
            {
                overlay[byteIndex + channel] = (byte)((reference.Pixels[byteIndex + channel] +
                    candidate.Pixels[byteIndex + channel] + 1) / 2);
            }

            absoluteDeltaSum += channelDeltaSum;
            maximumDelta = Math.Max(maximumDelta, pixelDelta);
        }

        string safeName = Path.GetFileNameWithoutExtension(name);
        WritePng(Path.Combine(diffDirectory, safeName + ".diff.png"),
            new RgbaImage(reference.Width, reference.Height, heatmap));
        WritePng(Path.Combine(diffDirectory, safeName + ".overlay.png"),
            new RgbaImage(reference.Width, reference.Height, overlay));

        int worstX = worstPixelIndex < 0 ? -1 : worstPixelIndex % reference.Width;
        int worstY = worstPixelIndex < 0 ? -1 : worstPixelIndex / reference.Width;
        return new FrameReport
        {
            Name = name,
            PixelCount = pixelCount,
            DifferentPixelCount = differentPixels,
            MaximumChannelDelta = maximumDelta,
            MeanAbsoluteChannelDelta = absoluteDeltaSum / (double)(pixelCount * 4),
            AbsoluteChannelDeltaSum = absoluteDeltaSum,
            WorstPixelX = worstX,
            WorstPixelY = worstY,
        };
    }

    private static RgbaImage ReadPng(string path)
    {
        using FileStream stream = File.OpenRead(path);
        ImageResult decoded = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return new RgbaImage(decoded.Width, decoded.Height, decoded.Data);
    }

    private static void WritePng(string path, RgbaImage image)
    {
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)image.Height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(file, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            int rowLength = checked(image.Width * 4);
            for (int y = 0; y < image.Height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(image.Pixels, y * rowLength, rowLength);
            }
        }

        WriteChunk(file, "IDAT", compressed.ToArray());
        WriteChunk(file, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);

        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        uint crc = 0xffffffff;
        foreach (byte value in typeBytes) { crc = UpdateCrc(crc, value); }
        foreach (byte value in data) { crc = UpdateCrc(crc, value); }
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, ~crc);
        stream.Write(checksum);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bitIndex = 0; bitIndex < 8; bitIndex++)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
        }

        return crc;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Использование: dotnet run --project tools/harness/Kern.CrystalPixelHarness -- " +
            "compare <reference-frames> <candidate-frames> <output> [--max-different-pixels N]");
    }

    private sealed record RgbaImage(int Width, int Height, byte[] Pixels);

    private sealed class ComparisonReport
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public int FrameCount { get; init; }
        public long PixelCount { get; init; }
        public long DifferentPixelCount { get; init; }
        public int MaximumChannelDelta { get; init; }
        public double MeanAbsoluteChannelDelta { get; init; }
        public List<FrameReport> Frames { get; init; } = new();
    }

    private sealed class FrameReport
    {
        public string Name { get; init; } = string.Empty;
        public int PixelCount { get; init; }
        public int DifferentPixelCount { get; init; }
        public int MaximumChannelDelta { get; init; }
        public double MeanAbsoluteChannelDelta { get; init; }
        public long AbsoluteChannelDeltaSum { get; init; }
        public int WorstPixelX { get; init; }
        public int WorstPixelY { get; init; }
    }
}
