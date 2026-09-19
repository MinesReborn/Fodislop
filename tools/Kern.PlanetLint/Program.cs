using Kern.PlanetBaker;
using Kern.PlanetPreview;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;

namespace Kern.PlanetLint;

internal sealed class FFT
{
    private readonly int _size;
    private readonly int _bits;
    private readonly int[] _reversed;
    private readonly Complex[,] _twiddles;

    public FFT(int size)
    {
        _size = size;
        _bits = (int)Math.Log(size, 2);
        _reversed = new int[size];
        for (int i = 0; i < size; i++)
        {
            _reversed[i] = ReverseBits(i, _bits);
        }

        _twiddles = new Complex[_bits, size / 2];
        for (int b = 0; b < _bits; b++)
        {
            int n = 1 << (b + 1);
            for (int k = 0; k < n / 2; k++)
            {
                double angle = -2.0 * Math.PI * k / n;
                _twiddles[b, k] = new Complex(Math.Cos(angle), Math.Sin(angle));
            }
        }
    }

    public void Forward2D(Complex[,] data)
    {
        int n = _size;
        for (int y = 0; y < n; y++)
        {
            Forward1D(data, y, true);
        }
        for (int x = 0; x < n; x++)
        {
            Forward1D(data, x, false);
        }
    }

    private void Forward1D(Complex[,] data, int line, bool rowMajor)
    {
        int n = _size;
        for (int i = 0; i < n; i++)
        {
            int j = _reversed[i];
            if (i < j)
            {
                if (rowMajor)
                {
                    (data[line, i], data[line, j]) = (data[line, j], data[line, i]);
                }
                else
                {
                    (data[i, line], data[j, line]) = (data[j, line], data[i, line]);
                }
            }
        }

        for (int b = 0; b < _bits; b++)
        {
            int n1 = 1 << (b + 1);
            int n2 = n1 >> 1;
            for (int k = 0; k < n; k += n1)
            {
                for (int j = 0; j < n2; j++)
                {
                    Complex t = _twiddles[b, j];
                    if (rowMajor)
                    {
                        Complex a = data[line, k + j];
                        Complex bVal = data[line, k + j + n2] * t;
                        data[line, k + j] = a + bVal;
                        data[line, k + j + n2] = a - bVal;
                    }
                    else
                    {
                        Complex a = data[k + j, line];
                        Complex bVal = data[k + j + n2, line] * t;
                        data[k + j, line] = a + bVal;
                        data[k + j + n2, line] = a - bVal;
                    }
                }
            }
        }
    }

    private static int ReverseBits(int value, int bits)
    {
        int result = 0;
        for (int i = 0; i < bits; i++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }
        return result;
    }
}

internal static class Program
{
    private const string TextureDir = "Assets/Textures/UI";
    private const int RenderSize = 900;
    private const double Zoom = 3.2;
    private const int RenderTargetSide = 3072;
    private const double RenderTargetAspect = 0.55;
    private static readonly double[] Background = { 0.02, 0.03, 0.06 };

    public static int Main(string[] args)
    {
        int failures = 0;

        Console.WriteLine("Рендер общего плана...");
        using var wide = PlanetPreviewApi.Render(RenderSize, 1.0);
        Console.WriteLine("Рендер приближения 3.2x...");
        using var zoomed = PlanetPreviewApi.Render(RenderSize, Zoom);

        var backgroundByte = new Rgb24(
            (byte)Math.Clamp((int)Math.Round(Background[0] * 255.0), 0, 255),
            (byte)Math.Clamp((int)Math.Round(Background[1] * 255.0), 0, 255),
            (byte)Math.Clamp((int)Math.Round(Background[2] * 255.0), 0, 255));

        bool[,] wideMask = new bool[RenderSize, RenderSize];
        bool[,] zoomMask = new bool[RenderSize, RenderSize];
        for (int y = 0; y < RenderSize; y++)
        {
            for (int x = 0; x < RenderSize; x++)
            {
                var wp = wide[x, y];
                wideMask[y, x] = Math.Abs(wp.R - backgroundByte.R) > 2 ||
                                  Math.Abs(wp.G - backgroundByte.G) > 2 ||
                                  Math.Abs(wp.B - backgroundByte.B) > 2;
                zoomMask[y, x] = true;
            }
        }

        using var normal = Image.Load<Rgb24>(Path.Combine(TextureDir, "planet_normal.png"));
        using var packed = Image.Load<Rgb24>(Path.Combine(TextureDir, "planet_packed.png"));
        int mapWidth = normal.Width;

        Console.WriteLine($"\nКарты {mapWidth}x{normal.Height}\n");

        Console.WriteLine("Резкость и разрешение:");
        if (!Check("sharpness_wide", Sharpness(wide, wideMask))) failures++;
        if (!Check("sharpness_zoom", Sharpness(zoomed, zoomMask))) failures++;
        if (!Check("spectral_cutoff", SpectralCutoff(zoomed, zoomMask), " от Найквиста")) failures++;
        if (!Check("texels_per_pixel", TexelsPerPixel(RenderSize, Zoom, mapWidth), " текс/пикс")) failures++;

        Console.WriteLine("\nПоверхность:");
        if (!Check("local_contrast", LocalContrast(zoomed, zoomMask))) failures++;

        Console.WriteLine("\nРифты:");
        if (!Check("rift_elongation", RiftElongation(packed))) failures++;

        Console.WriteLine("\nКвантование карт:");
        if (!Check("normal_levels_x", CountLevels(normal, 0), " уровней")) failures++;
        if (!Check("normal_levels_y", CountLevels(normal, 1), " уровней")) failures++;
        if (!Check("roughness_levels", CountLevels(packed, 0), " уровней")) failures++;

        Console.WriteLine("\nБюджет GPU:");

        double mapsBytes = 0;
        foreach (string name in new[] { "planet_albedo", "planet_normal", "planet_packed" })
        {
            using var img = Image.Load<Rgb24>(Path.Combine(TextureDir, $"{name}.png"));
            mapsBytes += img.Width * img.Height * 1.3333;
        }
        if (!Check("vram_maps_mb", mapsBytes / (1024.0 * 1024.0), " МБ")) failures++;

        int targetH = (int)(RenderTargetSide * RenderTargetAspect);
        double targetsBytes = RenderTargetSide * targetH * (4.0 + 2.0) + RenderTargetSide * targetH * 4.0;
        if (!Check("vram_targets_mb", targetsBytes / (1024.0 * 1024.0), " МБ")) failures++;
        if (!Check("static_render_mpx", RenderTargetSide * targetH / 1e6, " млн пикс")) failures++;

        Console.WriteLine($"\n{(failures == 0 ? "ВСЁ ЧИСТО" : $"НАРУШЕНИЙ: {failures}")}");
        return failures == 0 ? 0 : 1;
    }

    private static bool Check(string name, double value, string unit = "")
    {
        var (limit, side) = Thresholds[name];
        bool ok = side == "min" ? value >= limit : value <= limit;
        string arrow = side == "min" ? ">=" : "<=";
        Console.WriteLine($"  {(ok ? "OK  " : "ПЛОХО")}  {name,-18} {value,8:F4}{unit}  (нужно {arrow} {limit})");
        return ok;
    }

    private static readonly Dictionary<string, (double limit, string side)> Thresholds = new()
    {
        ["sharpness_wide"] = (0.055, "min"),
        ["sharpness_zoom"] = (0.045, "min"),
        ["spectral_cutoff"] = (0.55, "min"),
        ["texels_per_pixel"] = (1.0, "min"),
        ["rift_elongation"] = (0.30, "max"),
        ["local_contrast"] = (0.030, "min"),
        ["normal_levels_x"] = (128, "min"),
        ["normal_levels_y"] = (128, "min"),
        ["roughness_levels"] = (64, "min"),
        ["vram_maps_mb"] = (140.0, "max"),
        ["vram_targets_mb"] = (60.0, "max"),
        ["static_render_mpx"] = (6.0, "max"),
    };

    private static double Luma(Rgb24 c)
    {
        return (c.R * 0.2126 + c.G * 0.7152 + c.B * 0.0722) / 255.0;
    }

    private static double Sharpness(Image<Rgb24> image, bool[,] mask)
    {
        int w = image.Width;
        int h = image.Height;
        var lum = new double[h, w];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                lum[y, x] = Luma(image[x, y]);
            }
        }

        double sumAbs = 0.0;
        int count = 0;
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                if (!mask[y, x]) continue;
                double lap = -4.0 * lum[y, x] + lum[y - 1, x] + lum[y + 1, x] + lum[y, x - 1] + lum[y, x + 1];
                sumAbs += Math.Abs(lap);
                count++;
            }
        }

        if (count < 100) return 0.0;

        double mean = sumAbs / count;
        double spread = 0.0;
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                if (!mask[y, x]) continue;
                double lap = -4.0 * lum[y, x] + lum[y - 1, x] + lum[y + 1, x] + lum[y, x - 1] + lum[y, x + 1];
                spread += (Math.Abs(lap) - mean) * (Math.Abs(lap) - mean);
            }
        }
        spread = Math.Sqrt(spread / count);
        return spread > 1e-6 ? mean / spread : 0.0;
    }

    private static double SpectralCutoff(Image<Rgb24> image, bool[,] mask)
    {
        int w = image.Width;
        int h = image.Height;
        var rows = new List<int>();
        var cols = new List<int>();
        for (int y = 0; y < h; y++)
        {
            bool any = false;
            for (int x = 0; x < w; x++)
            {
                if (mask[y, x]) any = true;
            }
            if (any) rows.Add(y);
        }
        for (int x = 0; x < w; x++)
        {
            bool any = false;
            for (int y = 0; y < h; y++)
            {
                if (mask[y, x]) any = true;
            }
            if (any) cols.Add(x);
        }

        if (rows.Count == 0 || cols.Count == 0) return 0.0;

        int cy = (rows[0] + rows[^1]) / 2;
        int cx = (cols[0] + cols[^1]) / 2;
        int half = Math.Min(rows[^1] - rows[0], cols[^1] - cols[0]) / 4;
        if (half < 32) return 0.0;

        int size = 2 * half;
        var patch = new double[size, size];
        double patchMean = 0.0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                patch[y, x] = Luma(image[cx - half + x, cy - half + y]);
                patchMean += patch[y, x];
            }
        }
        patchMean /= size * size;

        var spectrum = new System.Numerics.Complex[size, size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double window = (0.5 - 0.5 * Math.Cos(2.0 * Math.PI * x / (size - 1))) *
                                (0.5 - 0.5 * Math.Cos(2.0 * Math.PI * y / (size - 1)));
                spectrum[y, x] = new System.Numerics.Complex((patch[y, x] - patchMean) * window, 0);
            }
        }

        var fft = new FFT(size);
        fft.Forward2D(spectrum);

        int nyquist = Math.Min(size, size) / 2;
        var profile = new double[nyquist];
        var counts = new double[nyquist];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int dx = x - size / 2;
                int dy = y - size / 2;
                int r = (int)Math.Sqrt(dx * dx + dy * dy);
                if (r < nyquist)
                {
                    profile[r] += spectrum[y, x].Magnitude;
                    counts[r]++;
                }
            }
        }
        for (int r = 0; r < nyquist; r++)
        {
            if (counts[r] > 0) profile[r] /= counts[r];
        }

        double reference = 0.0;
        int refCount = 0;
        for (int r = 1; r < 5 && r < nyquist; r++)
        {
            reference += profile[r];
            refCount++;
        }
        reference = refCount > 0 ? reference / refCount : 0.0;

        int above = 0;
        for (int r = 0; r < nyquist; r++)
        {
            if (profile[r] > reference * 0.01) above = r;
        }

        return above > 0 ? (double)above / nyquist : 0.0;
    }

    private static double TexelsPerPixel(int renderSize, double zoom, int mapWidth)
    {
        double distance = PlanetPreviewApi.CameraDistance;
        double visibleHalfAngle = Math.Acos(1.0 / distance);
        double visibleFraction = visibleHalfAngle / Math.PI;
        double texelsAcross = mapWidth * visibleFraction / Math.Max(zoom, 1e-6);
        return texelsAcross / renderSize;
    }

    private static double LocalContrast(Image<Rgb24> image, bool[,] mask)
    {
        int w = image.Width;
        int h = image.Height;
        int block = 12;
        int bh = h - h % block;
        int bw = w - w % block;

        var deviations = new List<double>();
        for (int by = 0; by < bh; by += block)
        {
            for (int bx = 0; bx < bw; bx += block)
            {
                bool allCovered = true;
                var vals = new List<double>();
                for (int dy = 0; dy < block && allCovered; dy++)
                {
                    for (int dx = 0; dx < block && allCovered; dx++)
                    {
                        if (!mask[by + dy, bx + dx]) allCovered = false;
                        vals.Add(Luma(image[bx + dx, by + dy]));
                    }
                }
                if (allCovered && vals.Count > 0)
                {
                    double mean = vals.Average();
                    double var = vals.Sum(v => (v - mean) * (v - mean)) / vals.Count;
                    deviations.Add(Math.Sqrt(var));
                }
            }
        }

        return deviations.Count > 0 ? deviations.Average() : 0.0;
    }

    private static double RiftElongation(Image<Rgb24> packed)
    {
        int w = packed.Width;
        int h = packed.Height;
        bool[,] mask = new bool[h, w];
        int area = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                mask[y, x] = packed[x, y].R > 24;
                if (mask[y, x]) area++;
            }
        }

        if (area < 64) return 1.0;

        int perimeter = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!mask[y, x]) continue;
                bool hasNeighbor = false;
                if (y > 0 && mask[y - 1, x]) hasNeighbor = true;
                else if (y < h - 1 && mask[y + 1, x]) hasNeighbor = true;
                else if (x > 0 && mask[y, x - 1]) hasNeighbor = true;
                else if (x < w - 1 && mask[y, x + 1]) hasNeighbor = true;
                if (!hasNeighbor) perimeter++;
            }
        }

        if (perimeter == 0) return 1.0;
        return Math.Min(4.0 * Math.PI * area / (perimeter * perimeter), 1.0);
    }

    private static double CountLevels(Image<Rgb24> image, int channel)
    {
        var values = new HashSet<byte>();
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                byte v = channel switch
                {
                    0 => image[x, y].R,
                    1 => image[x, y].G,
                    2 => image[x, y].B,
                    _ => image[x, y].R
                };
                values.Add(v);
            }
        }
        return values.Count;
    }
}
