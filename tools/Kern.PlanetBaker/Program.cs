using Kern.PlanetBaker;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Kern.PlanetBaker;

internal static class Program
{
    private const int Width = 8192;
    private const int Height = 4096;
    private const int BandRows = 128;
    private const string OutputDir = "Assets/Textures/UI";

    public static int Main(string[] args)
    {
        Directory.CreateDirectory(OutputDir);

        Console.WriteLine("Снимаю границы нормировки на грубой сетке...");
        var stats = ProbeTerrainRange();
        Console.WriteLine($"  высота {stats.ElevLo:F3}..{stats.ElevHi:F3}"
                        + $"  наклон {stats.SlopeLo:F5}..{stats.SlopeHi:F5}"
                        + $"  масштаб градиента {stats.GradHi:F5}");

        Console.WriteLine($"Сетка {Width}x{Height}, считаю поля полосами по {BandRows} строк...");

        var albedoOut = new Rgb24[Height, Width];
        var normalOut = new Rgb24[Height, Width];
        var packedOut = new Rgb24[Height, Width];

        double elevLo = double.PositiveInfinity;
        double elevHi = double.NegativeInfinity;
        double roughLo = double.PositiveInfinity;
        double roughHi = double.NegativeInfinity;
        long riftTexels = 0;
        long cloudTexels = 0;

        for (int start = 0; start < Height; start += BandRows)
        {
            int end = Math.Min(start + BandRows, Height);
            int padStart = Math.Max(start - 1, 0);
            int padEnd = Math.Min(end + 1, Height);
            int head = start - padStart;
            int tail = head + (end - start);
            int bandHeight = padEnd - padStart;

            var (dirs, lat) = EquirectDirections(Width, bandHeight);

            var elevation = new double[bandHeight, Width];
            for (int y = 0; y < bandHeight; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    double dirX = dirs[y, x, 0];
                    double dirY = dirs[y, x, 1];
                    double dirZ = dirs[y, x, 2];
                    elevation[y, x] = PlanetMath.ElevationBase(dirX, dirY, dirZ);
                }
            }

            var (dx, dy) = TangentGradients(elevation, lat, Width, Height);
            var normal = SurfaceNormal(dx, dy, stats.GradHi);

            double slopeLo = stats.SlopeLo;
            double slopeHi = stats.SlopeHi;
            double elevLoVal = stats.ElevLo;
            double elevHiVal = stats.ElevHi;

            var slope = new double[bandHeight, Width];
            var elevN = new double[bandHeight, Width];
            for (int y = 0; y < bandHeight; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    double mag = Math.Sqrt(dx[y, x] * dx[y, x] + dy[y, x] * dy[y, x]);
                    slope[y, x] = Math.Clamp((mag - slopeLo) / Math.Max(slopeHi - slopeLo, 1e-6), 0.0, 1.0);
                    elevN[y, x] = Math.Clamp((elevation[y, x] - elevLoVal) / Math.Max(elevHiVal - elevLoVal, 1e-6), 0.0, 1.0);
                }
            }

            var fault = new double[bandHeight, Width];
            var emission = new double[bandHeight, Width];
            var polar = new double[bandHeight, Width];
            for (int y = 0; y < bandHeight; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    double dirX = dirs[y, x, 0];
                    double dirY = dirs[y, x, 1];
                    double dirZ = dirs[y, x, 2];
                    fault[y, x] = PlanetMath.FaultField(dirX, dirY, dirZ);
                    emission[y, x] = BuildEmission(dirX, dirY, dirZ, elevN[y, x], fault[y, x]);
                    polar[y, x] = PlanetMath.PolarCap(dirX, dirY, dirZ, lat[y, x]);
                }
            }

            var province = new double[bandHeight, Width];
            var hue = new double[bandHeight, Width];
            var clouds = new double[bandHeight, Width];
            for (int y = 0; y < bandHeight; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    double dirX = dirs[y, x, 0];
                    double dirY = dirs[y, x, 1];
                    double dirZ = dirs[y, x, 2];
                    province[y, x] = PlanetMath.ProvinceField(dirX, dirY, dirZ);
                    hue[y, x] = PlanetMath.HueField(dirX, dirY, dirZ);
                    clouds[y, x] = PlanetMath.CloudField(dirX, dirY, dirZ);
                }
            }

            var albedo = BuildAlbedo(elevN, slope, province, emission, hue, polar);
            var grain = new double[bandHeight, Width];
            for (int y = 0; y < bandHeight; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    double dirX = dirs[y, x, 0];
                    double dirY = dirs[y, x, 1];
                    double dirZ = dirs[y, x, 2];
                    grain[y, x] = Math.Clamp(PlanetMath.Fbm(dirX * PlanetMath.GRAIN_SCALE, dirY * PlanetMath.GRAIN_SCALE, dirZ * PlanetMath.GRAIN_SCALE, 2) * 0.5 + 0.5, 0.0, 1.0);
                }
            }

            var roughness = new double[bandHeight, Width];
            for (int y = 0; y < bandHeight; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    double r = PlanetMath.ROUGHNESS_FLAT + (PlanetMath.ROUGHNESS_STEEP - PlanetMath.ROUGHNESS_FLAT) * PlanetMath.Smoothstep(0.25, 0.80, slope[y, x]);
                    r += (grain[y, x] - 0.5) * PlanetMath.ROUGHNESS_GRAIN;
                    r = Math.Clamp(r - emission[y, x] * 0.10 - polar[y, x] * 0.16, 0.5, 0.95);
                    roughness[y, x] = r;
                }
            }

            for (int y = head; y < tail; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int yy = padStart + y;
                    double ar = Math.Clamp(albedo[y, x, 0], 0.0, 1.0);
                    double ag = Math.Clamp(albedo[y, x, 1], 0.0, 1.0);
                    double ab = Math.Clamp(albedo[y, x, 2], 0.0, 1.0);
                    albedoOut[yy, x] = new Rgb24(
                        (byte)Math.Clamp((int)Math.Round(LinearToSrgb(ar) * 255.0), 0, 255),
                        (byte)Math.Clamp((int)Math.Round(LinearToSrgb(ag) * 255.0), 0, 255),
                        (byte)Math.Clamp((int)Math.Round(LinearToSrgb(ab) * 255.0), 0, 255));

                    double nx = normal[y, x, 0];
                    double ny = normal[y, x, 1];
                    double nz = normal[y, x, 2];
                    normalOut[yy, x] = new Rgb24(
                        (byte)Math.Clamp((int)Math.Round((nx * 0.5 + 0.5) * 255.0), 0, 255),
                        (byte)Math.Clamp((int)Math.Round((ny * 0.5 + 0.5) * 255.0), 0, 255),
                        (byte)Math.Clamp((int)Math.Round((nz * 0.5 + 0.5) * 255.0), 0, 255));

                    double rr = Math.Clamp(roughness[y, x] * 255.0, 0, 255);
                    double ee = Math.Clamp(emission[y, x] * 255.0, 0, 255);
                    double cc = Math.Clamp(clouds[y, x] * 255.0, 0, 255);
                    packedOut[yy, x] = new Rgb24(
                        (byte)Math.Clamp((int)Math.Round(rr), 0, 255),
                        (byte)Math.Clamp((int)Math.Round(ee), 0, 255),
                        (byte)Math.Clamp((int)Math.Round(cc), 0, 255));
                }
            }

            for (int y = head; y < tail; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int yy = padStart + y;
                    if (elevation[y, x] < elevLo) elevLo = elevation[y, x];
                    if (elevation[y, x] > elevHi) elevHi = elevation[y, x];
                    if (roughness[y, x] < roughLo) roughLo = roughness[y, x];
                    if (roughness[y, x] > roughHi) roughHi = roughness[y, x];
                    if (emission[y, x] > 0.02) riftTexels++;
                    if (clouds[y, x] > 0.5) cloudTexels++;
                }
            }

            Console.WriteLine($"  строки {start,5}..{end,5}");
        }

        long total = (long)Width * Height;
        Console.WriteLine($"  высота        {elevLo:F3} .. {elevHi:F3}");
        Console.WriteLine($"  шероховатость {roughLo:F3} .. {roughHi:F3}");
        Console.WriteLine($"  рифты         покрытие {riftTexels * 100.0 / total:F2}%");
        Console.WriteLine($"  облака        покрытие {cloudTexels * 100.0 / total:F2}%");

        string[] outputs = { "planet_albedo.png", "planet_normal.png", "planet_packed.png" };
        Rgb24[][,] data = { albedoOut, normalOut, packedOut };
        for (int i = 0; i < outputs.Length; i++)
        {
            string path = Path.Combine(OutputDir, outputs[i]);
            var flat = new Rgb24[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    flat[y * Width + x] = data[i][y, x];
                }
            }
            using var img = Image.LoadPixelData<Rgb24>(flat, Width, Height);
            img.Save(path);
            Console.WriteLine($"  записано {path} ({new FileInfo(path).Length / 1024 / 1024:F2} МБ)");
        }

        return 0;
    }

    private static (double[,,] dirs, double[,] lat) EquirectDirections(int width, int height)
    {
        var dirs = new double[height, width, 3];
        var lat = new double[height, width];
        double[] u = new double[width];
        double[] v = new double[height];

        for (int x = 0; x < width; x++)
        {
            u[x] = ((double)x + 0.5) / width;
        }
        for (int y = 0; y < height; y++)
        {
            v[y] = ((double)y + 0.5) / height;
        }

        for (int y = 0; y < height; y++)
        {
            double longitude = (u[0] - 0.5) * 2.0 * Math.PI;
            double latitude = (0.5 - v[y]) * Math.PI;
            double cosLat = Math.Cos(latitude);

            for (int x = 0; x < width; x++)
            {
                double lon = (u[x] - 0.5) * 2.0 * Math.PI;
                dirs[y, x, 0] = cosLat * Math.Cos(lon);
                dirs[y, x, 1] = Math.Sin(latitude);
                dirs[y, x, 2] = cosLat * Math.Sin(lon);
                lat[y, x] = latitude;
            }
        }

        return (dirs, lat);
    }

    private static (double[,] dx, double[,] dy) TangentGradients(double[,] elevation, double[,] latitude, int mapWidth, int mapHeight)
    {
        int h = elevation.GetLength(0);
        int w = elevation.GetLength(1);
        var dx = new double[h, w];
        var dy = new double[h, w];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int xPrev = x == 0 ? w - 1 : x - 1;
                int xNext = x == w - 1 ? 0 : x + 1;
                dx[y, x] = (elevation[y, xNext] - elevation[y, xPrev]) * 0.5;
                dy[y, x] = y > 0 && y < h - 1 ? (elevation[y + 1, x] - elevation[y - 1, x]) * 0.5 : 0.0;
            }
        }

        double metricX = mapWidth / 512.0;
        double metricY = mapHeight / 256.0;

        for (int y = 0; y < h; y++)
        {
            double cosLat = Math.Max(Math.Cos(latitude[y, 0]), 0.25);
            for (int x = 0; x < w; x++)
            {
                dx[y, x] = (dx[y, x] / cosLat) * metricX;
                dy[y, x] = dy[y, x] * metricY;
            }
        }

        return (dx, dy);
    }

    private static double[,,] SurfaceNormal(double[,] dx, double[,] dy, double gradHi)
    {
        int h = dx.GetLength(0);
        int w = dx.GetLength(1);
        var normal = new double[h, w, 3];
        double scale = PlanetMath.NORMAL_SPAN / Math.Max(gradHi, 1e-9);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double nx = -dx[y, x] * scale;
                double ny = dy[y, x] * scale;
                double nz = 1.0;
                double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                normal[y, x, 0] = nx / len;
                normal[y, x, 1] = ny / len;
                normal[y, x, 2] = nz / len;
            }
        }

        return normal;
    }

    private static double[,,] BuildAlbedo(double[,] elevN, double[,] slope, double[,] province, double[,] emission, double[,] hue, double[,] polar)
    {
        int h = elevN.GetLength(0);
        int w = elevN.GetLength(1);
        var albedo = new double[h, w, 3];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double s = slope[y, x];
                double e = elevN[y, x];

                double r = PlanetMath.CRUST_R + (PlanetMath.REGOLITH_R - PlanetMath.CRUST_R) * PlanetMath.Smoothstep(0.22, 0.66, s);
                r = r + (PlanetMath.BASALT_R - r) * PlanetMath.Smoothstep(0.58, 1.00, s);

                double g = PlanetMath.CRUST_G + (PlanetMath.REGOLITH_G - PlanetMath.CRUST_G) * PlanetMath.Smoothstep(0.22, 0.66, s);
                g = g + (PlanetMath.BASALT_G - g) * PlanetMath.Smoothstep(0.58, 1.00, s);

                double b = PlanetMath.CRUST_B + (PlanetMath.REGOLITH_B - PlanetMath.CRUST_B) * PlanetMath.Smoothstep(0.22, 0.66, s);
                b = b + (PlanetMath.BASALT_B - b) * PlanetMath.Smoothstep(0.58, 1.00, s);

                double basin = 1.0 - PlanetMath.Smoothstep(PlanetMath.BASIN_LEVEL - 0.14, PlanetMath.BASIN_LEVEL + 0.14, e);
                double basinMix = basin * (1.0 - PlanetMath.Smoothstep(0.45, 0.80, s));
                r = r + (PlanetMath.CRUST_R * 0.58 - r) * basinMix;
                g = g + (PlanetMath.CRUST_G * 0.58 - g) * basinMix;
                b = b + (PlanetMath.CRUST_B * 0.58 - b) * basinMix;

                double peak = PlanetMath.Smoothstep(PlanetMath.PEAK_LEVEL - 0.18, PlanetMath.PEAK_LEVEL + 0.20, e);
                r = r + (PlanetMath.PEAK_R - r) * peak;
                g = g + (PlanetMath.PEAK_G - g) * peak;
                b = b + (PlanetMath.PEAK_B - b) * peak;

                double riftDim = 1.0 - emission[y, x] * 0.72;
                r *= riftDim;
                g *= riftDim;
                b *= riftDim;

                double tint = 0.74 + province[y, x] * 0.48;
                r *= tint;
                g *= tint;
                b *= tint;

                double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                double compR = PlanetMath.OXIDE_R + (PlanetMath.EVAPORITE_R - PlanetMath.OXIDE_R) * hue[y, x];
                double compG = PlanetMath.OXIDE_G + (PlanetMath.EVAPORITE_G - PlanetMath.OXIDE_G) * hue[y, x];
                double compB = PlanetMath.OXIDE_B + (PlanetMath.EVAPORITE_B - PlanetMath.OXIDE_B) * hue[y, x];
                double compLum = 0.2126 * compR + 0.7152 * compG + 0.0722 * compB;
                double mixFactor = PlanetMath.HUE_STRENGTH;
                r = r + (compR * lum / Math.Max(compLum, 1e-5) - r) * mixFactor;
                g = g + (compG * lum / Math.Max(compLum, 1e-5) - g) * mixFactor;
                b = b + (compB * lum / Math.Max(compLum, 1e-5) - b) * mixFactor;

                double polarMix = polar[y, x] * 0.88;
                r = r + (PlanetMath.POLAR_R - r) * polarMix;
                g = g + (PlanetMath.POLAR_G - g) * polarMix;
                b = b + (PlanetMath.POLAR_B - b) * polarMix;

                albedo[y, x, 0] = Math.Clamp(r, 0.0, 1.0);
                albedo[y, x, 1] = Math.Clamp(g, 0.0, 1.0);
                albedo[y, x, 2] = Math.Clamp(b, 0.0, 1.0);
            }
        }

        return albedo;
    }

    private static double BuildEmission(double dx, double dy, double dz, double elevN, double fault)
    {
        double crack = PlanetMath.Smoothstep(PlanetMath.CRACK_THRESHOLD, 1.0, fault);
        crack = Math.Pow(crack, 1.55);
        double depth = 1.0 - PlanetMath.Smoothstep(PlanetMath.BASIN_LEVEL - 0.06, PlanetMath.BASIN_LEVEL + 0.24, elevN);
        depth = 0.34 + depth * 0.66;
        double breakup = Math.Clamp(PlanetMath.Fbm(dx * PlanetMath.CRACK_SCALE * 2.2, dy * PlanetMath.CRACK_SCALE * 2.2, dz * PlanetMath.CRACK_SCALE * 2.2, 3) * 0.5 + 0.5, 0.0, 1.0);
        breakup = 0.40 + PlanetMath.Smoothstep(0.28, 0.92, breakup) * 0.60;
        double region = PlanetMath.CrackRegion(dx, dy, dz);
        return Math.Clamp(crack * depth * breakup * region, 0.0, 1.0);
    }

    private static double LinearToSrgb(double c)
    {
        return c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(Math.Max(c, 0.0), 1.0 / 2.4) - 0.055;
    }

    private static (double ElevLo, double ElevHi, double SlopeLo, double SlopeHi, double GradHi) ProbeTerrainRange()
    {
        const int probeW = 512;
        const int probeH = 256;
        var (dirs, lat) = EquirectDirections(probeW, probeH);

        var elevation = new double[probeH, probeW];
        for (int y = 0; y < probeH; y++)
        {
            for (int x = 0; x < probeW; x++)
            {
                elevation[y, x] = PlanetMath.ElevationBase(dirs[y, x, 0], dirs[y, x, 1], dirs[y, x, 2]);
            }
        }

        var (dx, dy) = TangentGradients(elevation, lat, probeW, probeH);
        var mag = new double[probeH, probeW];
        for (int y = 0; y < probeH; y++)
        {
            for (int x = 0; x < probeW; x++)
            {
                mag[y, x] = Math.Sqrt(dx[y, x] * dx[y, x] + dy[y, x] * dy[y, x]);
            }
        }

        var sortedElev = new List<double>();
        var sortedSlope = new List<double>();
        var sortedGrad = new List<double>();
        for (int y = 0; y < probeH; y++)
        {
            for (int x = 0; x < probeW; x++)
            {
                sortedElev.Add(elevation[y, x]);
                sortedSlope.Add(mag[y, x]);
                sortedGrad.Add(mag[y, x]);
            }
        }

        sortedElev.Sort();
        sortedSlope.Sort();
        sortedGrad.Sort();

        double ElevLo = sortedElev[(int)(probeW * probeH * 0.02)];
        double ElevHi = sortedElev[(int)(probeW * probeH * 0.98)];
        double GradHi = sortedGrad[(int)(probeW * probeH * 0.99)];
        double SlopeLo = sortedSlope[(int)(probeW * probeH * 0.05)];
        double SlopeHi = sortedSlope[(int)(probeW * probeH * 0.995)];

        return (ElevLo, ElevHi, SlopeLo, SlopeHi, GradHi);
    }
}
