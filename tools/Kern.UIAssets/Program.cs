using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Kern.UIAssets;

internal static class Program
{
    private const string OutputDir = "Assets/Textures/UI";

    public static int Main(string[] args)
    {
        Directory.CreateDirectory(OutputDir);

        GenerateExactPlanet(1024);
        GenerateBrandLogo(128);
        GenerateCleanSpaceBg(1920, 1080);
        GenerateSidebarIcons();

        return 0;
    }

    private static void GenerateExactPlanet(int size)
    {
        using var img = new Image<Rgba32>(size, size);
        Random random = new Random(999);

        double cx = size * 0.5;
        double cy = size * 0.5;
        double radius = size * 0.40;
        double atmoOuter = size * 0.48;

        double sx = cx - radius * 0.36;
        double sy = cy - radius * 0.40;

        (byte r, byte g, byte b) cHighlight = (235, 142, 86);
        (byte r, byte g, byte b) cMid = (195, 96, 48);
        (byte r, byte g, byte b) cDeep = (96, 32, 14);
        (byte r, byte g, byte b) cNight = (3, 6, 10);
        (byte r, byte g, byte b) cAtmo = (112, 229, 221);

        float[,] pGrid = new float[64, 64];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                pGrid[y, x] = (float)random.NextDouble();
            }
        }

        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < accessor.Width; x++)
                {
                    double dx = x - cx;
                    double dy = y - cy;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist > radius)
                    {
                        if (dist <= atmoOuter)
                        {
                            double atmoT = (dist - radius) / (atmoOuter - radius);
                            byte alpha = (byte)Math.Clamp((int)Math.Round(255.0 * Math.Exp(-atmoT * 4.5) * 0.9), 0, 255);
                            row[x] = new Rgba32(cAtmo.r, cAtmo.g, cAtmo.b, alpha);
                        }
                        continue;
                    }

                    double distSun = Math.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy)) / (radius * 1.6);
                    distSun = Math.Min(1.0, distSun);

                    double nx = dx / radius;
                    double ny = dy / radius;
                    double nz = Math.Sqrt(Math.Max(0.0, 1.0 - nx * nx - ny * ny));
                    double uCoord = Math.Atan2(nx, nz) / Math.PI * 0.5 + 0.5;
                    double vCoord = Math.Asin(Math.Max(-1.0, Math.Min(1.0, ny))) / Math.PI + 0.5;

                    double geo = Noise2D(pGrid, uCoord, vCoord) * 0.18;

                    double t = Math.Clamp(distSun + geo - 0.09, 0.0, 1.0);
                    int cr, cg, cb;
                    if (t < 0.46)
                    {
                        double k = t / 0.46;
                        cr = (int)Math.Round(cHighlight.r + (cMid.r - cHighlight.r) * k);
                        cg = (int)Math.Round(cHighlight.g + (cMid.g - cHighlight.g) * k);
                        cb = (int)Math.Round(cHighlight.b + (cMid.b - cHighlight.b) * k);
                    }
                    else if (t < 0.72)
                    {
                        double k = (t - 0.46) / 0.26;
                        cr = (int)Math.Round(cMid.r + (cDeep.r - cMid.r) * k);
                        cg = (int)Math.Round(cMid.g + (cDeep.g - cMid.g) * k);
                        cb = (int)Math.Round(cMid.b + (cDeep.b - cMid.b) * k);
                    }
                    else
                    {
                        double k = (t - 0.72) / 0.28;
                        cr = (int)Math.Round(cDeep.r + (cNight.r - cDeep.r) * k);
                        cg = (int)Math.Round(cDeep.g + (cNight.g - cDeep.g) * k);
                        cb = (int)Math.Round(cDeep.b + (cNight.b - cDeep.b) * k);
                    }

                    row[x] = new Rgba32((byte)cr, (byte)cg, (byte)cb, 255);
                }
            }
        });

        img.Save(Path.Combine(OutputDir, "planet_exact.png"));
        Console.WriteLine("Generated planet_exact.png");
    }

    private static void GenerateBrandLogo(int size)
    {
        using var img = new Image<Rgba32>(size, size);
        double cx = size / 2.0;
        double cy = size / 2.0;
        double r = size * 0.44;

        var points = new (double x, double y)[6];
        for (int i = 0; i < 6; i++)
        {
            double angle = Math.PI * 60 * i / 180.0 - Math.PI / 6.0;
            points[i] = (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
        }

        DrawPolygon(img, size, points, ((byte)255, (byte)255, (byte)255, (byte)255));

        double rInner = r * 0.62;
        var innerPoints = new (double x, double y)[6];
        for (int i = 0; i < 6; i++)
        {
            double angle = Math.PI * 60 * i / 180.0 - Math.PI / 6.0;
            innerPoints[i] = (cx + rInner * Math.Cos(angle), cy + rInner * Math.Sin(angle));
        }

        FillPolygon(img, size, innerPoints, ((byte)0, (byte)0, (byte)0, (byte)0));

        double rCore = r * 0.28;
        var corePoints = new (double x, double y)[4];
        for (int i = 0; i < 4; i++)
        {
            double angle = Math.PI * 90 * i / 180.0;
            corePoints[i] = (cx + rCore * Math.Cos(angle), cy + rCore * Math.Sin(angle));
        }

        FillPolygon(img, size, corePoints, ((byte)255, (byte)255, (byte)255, (byte)255));

        img.Save(Path.Combine(OutputDir, "mm_logo.png"));
        Console.WriteLine("Generated mm_logo.png");
    }

    private static void GenerateCleanSpaceBg(int width, int height)
    {
        using var img = new Image<Rgba32>(width, height);
        Random random = new Random(4242);

        double ncx = width * 0.74;
        double ncy = height * 0.50;
        double nebulaRad = width * 0.45;

        var stars = new List<(int x, int y, byte b, int sz)>();
        for (int i = 0; i < 220; i++)
        {
            int sx = random.Next((int)(width * 0.15), width);
            int sy = random.Next(0, height);
            int brightness = random.Next(140, 256);
            int sz = brightness >= 220 ? 2 : 1;
            stars.Add((sx, sy, (byte)brightness, sz));
        }

        var starMap = new Dictionary<(int, int), byte>();
        foreach (var (sx, sy, b, sz) in stars)
        {
            for (int oy = 0; oy < sz; oy++)
            {
                for (int ox = 0; ox < sz; ox++)
                {
                    int px = sx + ox;
                    int py = sy + oy;
                    if (px >= 0 && px < width && py >= 0 && py < height)
                    {
                        starMap[(px, py)] = b;
                    }
                }
            }
        }

        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < accessor.Width; x++)
                {
                    double tVert = (double)y / height;
                    int baseR = (int)Math.Round(3 * (1 - tVert) + 1 * tVert);
                    int baseG = (int)Math.Round(6 * (1 - tVert) + 2 * tVert);
                    int baseB = (int)Math.Round(10 * (1 - tVert) + 4 * tVert);

                    double dNebula = Math.Sqrt((x - ncx) * (x - ncx) + (y - ncy) * (y - ncy));
                    if (dNebula < nebulaRad)
                    {
                        double tNebula = 1.0 - dNebula / nebulaRad;
                        baseR = (int)Math.Round(baseR + 14 * tNebula * tNebula);
                        baseG = (int)Math.Round(baseG + 48 * tNebula * tNebula);
                        baseB = (int)Math.Round(baseB + 56 * tNebula * tNebula);
                    }

                    double tLeft = Math.Max(0.0, 1.0 - (x / (width * 0.55)));
                    double leftShade = Math.Pow(tLeft, 1.5);
                    baseR = (int)Math.Round(baseR * (1 - leftShade * 0.75) + 3 * leftShade * 0.75);
                    baseG = (int)Math.Round(baseG * (1 - leftShade * 0.75) + 6 * leftShade * 0.75);
                    baseB = (int)Math.Round(baseB * (1 - leftShade * 0.75) + 10 * leftShade * 0.75);

                    if (starMap.TryGetValue((x, y), out byte sb))
                    {
                        baseR = Math.Min(255, baseR + sb);
                        baseG = Math.Min(255, baseG + sb);
                        baseB = Math.Min(255, baseB + sb);
                    }

                    row[x] = new Rgba32(
                        (byte)Math.Clamp(baseR, 0, 255),
                        (byte)Math.Clamp(baseG, 0, 255),
                        (byte)Math.Clamp(baseB, 0, 255),
                        255);
                }
            }
        });

        img.Save(Path.Combine(OutputDir, "mm_space_bg.png"));
        Console.WriteLine("Generated mm_space_bg.png");
    }

    private static void GenerateSidebarIcons()
    {
        GenerateIcon("mm_icon_chronicle.png", 128, (image, s) =>
        {
            double pad = s * 0.22;
            double x0 = pad, y0 = pad * 0.8, x1 = s - pad, y1 = s - pad * 0.8;
            DrawPolygon(image, s, new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }, ((byte)255, (byte)255, (byte)255, (byte)255));
            float lw = (int)(s * 0.035);
            DrawLine(image, s, (int)(pad + s * 0.1), (int)(y0 + s * 0.18), (int)(x1 - s * 0.1), (int)(y0 + s * 0.18), ((byte)255, (byte)255, (byte)255, (byte)255), lw);
        });

        GenerateIcon("mm_icon_settings.png", 128, (image, s) =>
        {
            double cx = s / 2.0, cy = s / 2.0;
            double rOuter = s * 0.34;
            FillCircle(image, s, cx, cy, rOuter, ((byte)255, (byte)255, (byte)255, (byte)255));
            FillCircle(image, s, cx, cy, s * 0.12, ((byte)0, (byte)0, (byte)0, (byte)0));
        });

        GenerateIcon("mm_icon_repair.png", 128, (image, s) =>
        {
            float lw = (int)(s * 0.09);
            DrawLine(image, s, (int)(s * 0.28), (int)(s * 0.72), (int)(s * 0.62), (int)(s * 0.38), ((byte)255, (byte)255, (byte)255, (byte)255), lw);
            FillCircle(image, s, s * 0.68, s * 0.32, s * 0.16, ((byte)255, (byte)255, (byte)255, (byte)255));
            FillCircle(image, s, s * 0.68, s * 0.32, s * 0.09, ((byte)0, (byte)0, (byte)0, (byte)0));
            FillCircle(image, s, s * 0.26, s * 0.74, s * 0.08, ((byte)255, (byte)255, (byte)255, (byte)255));
        });

        GenerateIcon("mm_icon_update.png", 128, (image, s) =>
        {
            float lw = (int)(s * 0.045);
            DrawArc(image, s, s * 0.28, s * 0.24, s * 0.44, s * 0.44, 180, 180, ((byte)255, (byte)255, (byte)255, (byte)255), lw);
            DrawLine(image, s, (int)(s * 0.28), (int)(s * 0.46), (int)(s * 0.22), (int)(s * 0.66), ((byte)255, (byte)255, (byte)255, (byte)255), lw);
            DrawLine(image, s, (int)(s * 0.72), (int)(s * 0.46), (int)(s * 0.78), (int)(s * 0.66), ((byte)255, (byte)255, (byte)255, (byte)255), lw);
            DrawLine(image, s, (int)(s * 0.18), (int)(s * 0.66), (int)(s * 0.82), (int)(s * 0.66), ((byte)255, (byte)255, (byte)255, (byte)255), lw);
            FillCircle(image, s, s / 2, s * 0.75, s * 0.06, ((byte)255, (byte)255, (byte)255, (byte)255));
        });

        GenerateIcon("mm_icon_discord.png", 128, (image, s) =>
        {
            double padX = s * 0.20, padY = s * 0.28;
            DrawPolygon(image, s, new[] { (padX, padY), (s - padX, padY), (s - padX, s - padY), (padX, s - padY) }, ((byte)255, (byte)255, (byte)255, (byte)255));
            FillCircle(image, s, s / 2 - s * 0.12, s / 2, s * 0.05, ((byte)255, (byte)255, (byte)255, (byte)255));
            FillCircle(image, s, s / 2 + s * 0.12, s / 2, s * 0.05, ((byte)255, (byte)255, (byte)255, (byte)255));
        });

        GenerateIcon("mm_icon_telegram.png", 128, (image, s) =>
        {
            var pts = new (double x, double y)[]
            {
                (s * 0.80, s * 0.22),
                (s * 0.22, s * 0.52),
                (s * 0.44, s * 0.60),
                (s * 0.52, s * 0.78),
                (s * 0.62, s * 0.62)
            };
            FillTriangle(image, s, pts[0], pts[1], pts[2], ((byte)255, (byte)255, (byte)255, (byte)255));
            FillTriangle(image, s, pts[0], pts[2], pts[3], ((byte)255, (byte)255, (byte)255, (byte)255));
            FillTriangle(image, s, pts[0], pts[3], pts[4], ((byte)255, (byte)255, (byte)255, (byte)255));
        });

        GenerateIcon("mm_icon_vk.png", 128, (image, s) =>
        {
            double pad = s * 0.22;
            DrawPolygon(image, s, new[] { (pad, pad), (s - pad, pad), (s - pad, s - pad), (pad, s - pad) }, ((byte)255, (byte)255, (byte)255, (byte)255));
            float lw = (int)(s * 0.05);
            DrawLine(image, s, (int)(s * 0.38), (int)(s * 0.32), (int)(s * 0.38), (int)(s * 0.68), ((byte)255, (byte)255, (byte)255, (byte)255), lw);
            DrawLine(image, s, (int)(s * 0.38), (int)(s * 0.50), (int)(s * 0.62), (int)(s * 0.32), ((byte)255, (byte)255, (byte)255, (byte)255), lw);
            DrawLine(image, s, (int)(s * 0.44), (int)(s * 0.46), (int)(s * 0.62), (int)(s * 0.68), ((byte)255, (byte)255, (byte)255, (byte)255), lw);
        });

        GenerateIcon("mm_icon_exit.png", 128, (image, s) =>
        {
            float lw = (int)(s * 0.045);
            DrawLine(image, s, (int)(s * 0.54), (int)(s * 0.22), (int)(s * 0.24), (int)(s * 0.22), ((byte)255, (byte)255, (byte)255, (byte)255), lw);
            DrawLine(image, s, (int)(s * 0.24), (int)(s * 0.22), (int)(s * 0.24), (int)(s * 0.78), ((byte)255, (byte)255, (byte)255, (byte)255), lw);
            DrawLine(image, s, (int)(s * 0.24), (int)(s * 0.78), (int)(s * 0.54), (int)(s * 0.78), ((byte)255, (byte)255, (byte)255, (byte)255), lw);
            DrawLine(image, s, (int)(s * 0.40), (int)(s * 0.50), (int)(s * 0.76), (int)(s * 0.50), ((byte)255, (byte)255, (byte)255, (byte)255), lw);
            DrawLine(image, s, (int)(s * 0.62), (int)(s * 0.36), (int)(s * 0.76), (int)(s * 0.50), ((byte)255, (byte)255, (byte)255, (byte)255), lw);
            DrawLine(image, s, (int)(s * 0.62), (int)(s * 0.64), (int)(s * 0.76), (int)(s * 0.50), ((byte)255, (byte)255, (byte)255, (byte)255), lw);
        });
    }

    private delegate void IconDraw(Image<Rgba32> image, int size);

    private static void GenerateIcon(string filename, int size, IconDraw drawFunc)
    {
        int hiSize = size * 4;
        using var img = new Image<Rgba32>(hiSize, hiSize);
        drawFunc(img, hiSize);
        img.Mutate(ctx => ctx.Resize(size, size, KnownResamplers.Lanczos3));
        img.Save(Path.Combine(OutputDir, filename));
        Console.WriteLine($"Generated {filename}");
    }

    private static float Noise2D(float[,] grid, double u, double v)
    {
        int w = grid.GetLength(1);
        int h = grid.GetLength(0);
        double x = u * w - 0.5;
        double y = v * h - 0.5;
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        double fx = x - x0;
        double fy = y - y0;
        int x1 = (x0 + 1) % w;
        int y0c = Math.Clamp(y0, 0, h - 1);
        int y1c = Math.Clamp(y0 + 1, 0, h - 1);

        float top = grid[y0c, x0] * (int)(1 - fx) + grid[y0c, x1] * (float)fx;
        float bottom = grid[y1c, x0] * (int)(1 - fx) + grid[y1c, x1] * (float)fx;
        return top * (int)(1 - fy) + bottom * (float)fy;
    }

    private static void DrawPolygon(Image<Rgba32> img, int width, (double x, double y)[] points, (byte r, byte g, byte b, byte a) color)
    {
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var intersections = new List<int>();
                for (int i = 0; i < points.Length; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % points.Length];
                    if ((a.y <= y && b.y > y) || (b.y <= y && a.y > y))
                    {
                        double t = (y - a.y) / (b.y - a.y);
                        int x = (int)Math.Round(a.x + t * (b.x - a.x));
                        intersections.Add(x);
                    }
                }
                intersections.Sort();
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int i = 0; i < intersections.Count - 1; i += 2)
                {
                    for (int x = intersections[i]; x <= intersections[i + 1]; x++)
                    {
                        if (x >= 0 && x < width)
                        {
                            row[x] = new Rgba32(color.r, color.g, color.b, color.a);
                        }
                    }
                }
            }
        });
    }

    private static void FillPolygon(Image<Rgba32> img, int width, (double x, double y)[] points, (byte r, byte g, byte b, byte a) color)
    {
        DrawPolygon(img, width, points, color);
    }

    private static void FillTriangle(Image<Rgba32> img, int width, (double x, double y) a, (double x, double y) b, (double x, double y) c, (byte r, byte g, byte b, byte a) color)
    {
        DrawPolygon(img, width, new[] { a, b, c }, color);
    }

    private static void DrawLine(Image<Rgba32> img, int width, int x0, int y0, int x1, int y1, (byte r, byte g, byte b, byte a) color, float thickness)
    {
        if (thickness <= 1.0f)
        {
            DrawLineThin(img, width, x0, y0, x1, y1, color);
            return;
        }

        int r = (int)Math.Ceiling(thickness / 2.0);
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy <= r * r)
                {
                    DrawLineThin(img, width, x0 + dx, y0 + dy, x1 + dx, y1 + dy, color);
                }
            }
        }
    }

    private static void DrawLineThin(Image<Rgba32> img, int width, int x0, int y0, int x1, int y1, (byte r, byte g, byte b, byte a) color)
    {
        img.ProcessPixelRows(accessor =>
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < accessor.Height)
                {
                    accessor.GetRowSpan(y0)[x0] = new Rgba32(color.r, color.g, color.b, color.a);
                }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        });
    }

    private static void FillCircle(Image<Rgba32> img, int width, double cx, double cy, double radius, (byte r, byte g, byte b, byte a) color)
    {
        img.ProcessPixelRows(accessor =>
        {
            int xMin = (int)Math.Floor(cx - radius);
            int xMax = (int)Math.Ceiling(cx + radius);
            int yMin = (int)Math.Floor(cy - radius);
            int yMax = (int)Math.Ceiling(cy + radius);

            for (int y = yMin; y <= yMax; y++)
            {
                if (y < 0 || y >= accessor.Height) continue;
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = xMin; x <= xMax; x++)
                {
                    if (x >= 0 && x < width)
                    {
                        double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        if (d <= radius)
                        {
                            row[x] = new Rgba32(color.r, color.g, color.b, color.a);
                        }
                    }
                }
            }
        });
    }

    private static void DrawArc(Image<Rgba32> img, int width, double cx, double cy, double rx, double ry, int startAngle, int sweepAngle, (byte r, byte g, byte b, byte a) color, float thickness)
    {
        int steps = (int)Math.Max(Math.Abs(sweepAngle), 1);
        double startRad = startAngle * Math.PI / 180.0;
        double endRad = (startAngle + sweepAngle) * Math.PI / 180.0;

        for (int i = 0; i <= steps; i++)
        {
            double frac = (double)i / steps;
            double angle = startRad + frac * (endRad - startRad);
            double x = cx + rx * Math.Cos(angle);
            double y = cy + ry * Math.Sin(angle);
            FillCircle(img, width, x, y, thickness / 2.0, color);
        }
    }
}
