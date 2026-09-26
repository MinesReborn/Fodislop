// Standalone C# generator for the menu UI textures.
// Generates mm_logo.png and mm_space_bg.png into Assets/Textures/UI/

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

const string OutputDir = "Assets/Textures/UI";
Directory.CreateDirectory(OutputDir);

GenerateBrandLogo(128);
GenerateCleanSpaceBg(1920, 1080);

// ---------------------------------------------------------------------------

static void GenerateBrandLogo(int size)
{
    using var img = new Image<Rgba32>(size, size, new Rgba32(0, 0, 0, 0));

    float cx = size / 2f;
    float cy = size / 2f;
    float r = size * 0.44f;

    // Hexagon points
    var points = new PointF[6];
    for (int i = 0; i < 6; i++)
    {
        double angle = (Math.PI / 180.0) * (60.0 * i - 30.0);
        points[i] = new PointF(cx + r * (float)Math.Cos(angle), cy + r * (float)Math.Sin(angle));
    }

    // Outer gold hexagon outline
    img.Mutate(ctx =>
    {
        var goldOutline = new Rgba32(245, 197, 66, 255);

        ctx.DrawPolygon(new SolidPen(new Rgba32(245, 197, 66, 255), 4f), points);

        // Inner cyber hexagon (fill + outline)
        float rInner = r * 0.62f;
        var innerPoints = new PointF[6];
        for (int i = 0; i < 6; i++)
        {
            double angle = (Math.PI / 180.0) * (60.0 * i - 30.0);
            innerPoints[i] = new PointF(cx + rInner * (float)Math.Cos(angle), cy + rInner * (float)Math.Sin(angle));
        }

        ctx.FillPolygon(Color.FromRgba(14, 22, 38, 240), innerPoints);
        ctx.DrawPolygon(new SolidPen(new Rgba32(86, 221, 212, 220), 2f), innerPoints);

        // Center gold diamond (4-point polygon)
        float rCore = r * 0.28f;
        var corePoints = new PointF[4];
        for (int i = 0; i < 4; i++)
        {
            double angle = (Math.PI / 180.0) * (90.0 * i);
            corePoints[i] = new PointF(cx + rCore * (float)Math.Cos(angle), cy + rCore * (float)Math.Sin(angle));
        }

        ctx.FillPolygon(Color.FromRgba(245, 197, 66, 255), corePoints);
    });

    img.SaveAsPng(System.IO.Path.Combine(OutputDir, "mm_logo.png"));
    Console.WriteLine("Generated cyber mm_logo.png");
}

static void GenerateCleanSpaceBg(int width, int height)
{
    using var img = new Image<Rgba32>(width, height, new Rgba32(3, 6, 10, 255));

    float ncx = width * 0.74f;
    float ncy = height * 0.50f;
    float nebulaRad = width * 0.45f;

    // Deterministic star field (seed 4242)
    var rng = new Random(4242);
    var starMap = new Dictionary<(int x, int y), int>();

    for (int s = 0; s < 220; s++)
    {
        int sx = rng.Next((int)(width * 0.15), width);
        int sy = rng.Next(0, height);
        int brightness = new[] { 140, 180, 220, 255 }[rng.Next(4)];
        int sizePt = brightness < 220 ? 1 : 2;

        for (int ox = 0; ox < sizePt; ox++)
        {
            for (int oy = 0; oy < sizePt; oy++)
            {
                int px = sx + ox;
                int py = sy + oy;
                if (px >= 0 && px < width && py >= 0 && py < height)
                {
                    starMap[(px, py)] = brightness;
                }
            }
        }
    }

    img.ProcessPixelRows(accessor =>
    {
        for (int y = 0; y < height; y++)
        {
            Span<Rgba32> row = accessor.GetRowSpan(y);
            float tVert = y / (float)height;

            for (int x = 0; x < width; x++)
            {
                float baseR = 3f * (1f - tVert) + 1f * tVert;
                float baseG = 6f * (1f - tVert) + 2f * tVert;
                float baseB = 10f * (1f - tVert) + 4f * tVert;

                // Soft ambient cyan nebula behind the menu scene
                float dNebula = MathF.Sqrt((x - ncx) * (x - ncx) + (y - ncy) * (y - ncy));
                if (dNebula < nebulaRad)
                {
                    float tNebula = MathF.Pow(1f - dNebula / nebulaRad, 2f);
                    baseR += 14f * tNebula;
                    baseG += 48f * tNebula;
                    baseB += 56f * tNebula;
                }

                // Left shadow gradient (deep space left side)
                float tLeft = MathF.Max(0f, 1f - x / (width * 0.55f));
                float leftShade = MathF.Pow(tLeft, 1.5f);
                baseR = baseR * (1f - leftShade * 0.75f) + 3f * leftShade * 0.75f;
                baseG = baseG * (1f - leftShade * 0.75f) + 6f * leftShade * 0.75f;
                baseB = baseB * (1f - leftShade * 0.75f) + 10f * leftShade * 0.75f;

                int r = (int)baseR;
                int g = (int)baseG;
                int b = (int)baseB;

                if (starMap.TryGetValue((x, y), out int sb))
                {
                    r = Math.Min(255, r + sb);
                    g = Math.Min(255, g + sb);
                    b = Math.Min(255, b + sb);
                }

                row[x] = new Rgba32((byte)r, (byte)g, (byte)b, 255);
            }
        }
    });

    img.SaveAsPng(System.IO.Path.Combine(OutputDir, "mm_space_bg.png"));
    Console.WriteLine("Generated mm_space_bg.png");
}
