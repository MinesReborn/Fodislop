// Standalone C# generator for the sidebar UI textures.
// Generates sidebar icon PNGs into Assets/Textures/UI/ using 4x super-sampling + Lanczos downsample.

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using IOPath = System.IO.Path;

const string OutputDir = "Assets/Textures/UI";
Directory.CreateDirectory(OutputDir);

var cyan = new Rgba32(86, 221, 212, 255);
var gold = new Rgba32(245, 197, 66, 255);
var red  = new Rgba32(255, 85, 85, 255);

CreateIcon("mm_icon_chronicle.png", DrawChronicle, cyan);
CreateIcon("mm_icon_settings.png",  DrawSettings,  cyan);
CreateIcon("mm_icon_repair.png",    DrawRepair,    cyan);
CreateIcon("mm_icon_update.png",    DrawUpdate,    gold);
CreateIcon("mm_icon_discord.png",   DrawDiscord,   cyan);
CreateIcon("mm_icon_telegram.png",  DrawTelegram,  cyan);
CreateIcon("mm_icon_vk.png",        DrawVk,        cyan);
CreateIcon("mm_icon_exit.png",      DrawExit,      red);

// ---------------------------------------------------------------------------

/// <summary>
/// Renders at 4x resolution then downsamples with Lanczos for crisp anti-aliased edges.
/// </summary>
static void CreateIcon(string filename, Action<IImageProcessingContext, float, Rgba32> drawFunc, Rgba32 color, int size = 128)
{
    int hiSize = size * 4;
    using var img = new Image<Rgba32>(hiSize, hiSize, new Rgba32(0, 0, 0, 0));

    img.Mutate(ctx =>
    {
        drawFunc(ctx, hiSize, color);
        ctx.Resize(new ResizeOptions
        {
            Size = new Size(size, size),
            Sampler = KnownResamplers.Lanczos3,
        });
    });

    img.SaveAsPng(IOPath.Combine(OutputDir, filename));
    Console.WriteLine($"Generated {filename}");
}

static void DrawChronicle(IImageProcessingContext ctx, float s, Rgba32 c)
{
    float pad = s * 0.22f;
    float x0 = pad, y0 = pad * 0.8f, x1 = s - pad, y1 = s - pad * 0.8f;
    float radius = s * 0.05f;
    float strokeW = s * 0.04f;
    float lw = s * 0.035f;

    // Document outline
    ctx.Draw(
        new DrawingOptions(),
        new SolidPen(c, strokeW),
        new RectangularPolygon(x0, y0, x1 - x0, y1 - y0));

    // Three horizontal text lines
    ctx.DrawLine(c, lw,
        new PointF(pad + s * 0.1f, y0 + s * 0.18f),
        new PointF(x1 - s * 0.1f, y0 + s * 0.18f));
    ctx.DrawLine(c, lw,
        new PointF(pad + s * 0.1f, y0 + s * 0.34f),
        new PointF(x1 - s * 0.1f, y0 + s * 0.34f));
    ctx.DrawLine(c, lw,
        new PointF(pad + s * 0.1f, y0 + s * 0.50f),
        new PointF(x1 - s * 0.22f, y0 + s * 0.50f));
}

static void DrawSettings(IImageProcessingContext ctx, float s, Rgba32 c)
{
    float cx = s / 2f, cy = s / 2f;
    float rOuter = s * 0.34f;
    float rHole  = s * 0.12f;
    int teeth = 6;

    // Gear teeth
    for (int i = 0; i < teeth; i++)
    {
        double angle = (2.0 * Math.PI / teeth) * i;
        float tx = cx + (rOuter + s * 0.04f) * (float)Math.Cos(angle);
        float ty = cy + (rOuter + s * 0.04f) * (float)Math.Sin(angle);
        ctx.Fill(c, new EllipsePolygon(tx, ty, s * 0.07f));
    }

    // Outer circle
    ctx.Fill(c, new EllipsePolygon(cx, cy, rOuter));

    // Inner hole (transparent punch-out)
    ctx.Fill(new Rgba32(0, 0, 0, 0), new EllipsePolygon(cx, cy, rHole));
}

static void DrawRepair(IImageProcessingContext ctx, float s, Rgba32 c)
{
    // Diagonal handle
    ctx.DrawLine(c, s * 0.09f,
        new PointF(s * 0.28f, s * 0.72f),
        new PointF(s * 0.62f, s * 0.38f));

    // Top head (circle outline)
    ctx.Draw(new SolidPen(c, s * 0.07f), new EllipsePolygon(s * 0.68f, s * 0.32f, s * 0.16f));

    // Cutout on top head
    ctx.DrawLine(new Rgba32(0, 0, 0, 0), s * 0.09f,
        new PointF(s * 0.68f, s * 0.32f),
        new PointF(s * 0.82f, s * 0.18f));

    // Bottom head (filled circle)
    ctx.Fill(c, new EllipsePolygon(s * 0.26f, s * 0.74f, s * 0.08f));
}

static void DrawUpdate(IImageProcessingContext ctx, float s, Rgba32 _)
{
    // Notification bell (drawn in gold regardless of passed color)
    var gold = new Rgba32(245, 197, 66, 255);
    float w = s * 0.045f;

    // Bell dome (arc approximated as polygon arc)
    ctx.DrawBeziers(
        new DrawingOptions(),
        new SolidPen(gold, w),
        ArcPoints(s * 0.28f, s * 0.24f, s * 0.72f, s * 0.68f, 180, 0));

    ctx.DrawLine(gold, w, new PointF(s * 0.28f, s * 0.46f), new PointF(s * 0.22f, s * 0.66f));
    ctx.DrawLine(gold, w, new PointF(s * 0.72f, s * 0.46f), new PointF(s * 0.78f, s * 0.66f));
    ctx.DrawLine(gold, w, new PointF(s * 0.18f, s * 0.66f), new PointF(s * 0.82f, s * 0.66f));

    // Clapper
    ctx.Fill(gold, new EllipsePolygon(s / 2f, s * 0.75f, s * 0.06f));
}

static void DrawDiscord(IImageProcessingContext ctx, float s, Rgba32 c)
{
    float padX = s * 0.20f;
    float padY = s * 0.28f;
    float radius = s * 0.12f;
    float strokeW = s * 0.045f;

    // Controller body
    ctx.Draw(
        new DrawingOptions(),
        new SolidPen(c, strokeW),
        new RectangularPolygon(padX, padY, s - padX * 2f, s - padY * 2f));

    float cx = s / 2f, cy = s / 2f;

    // Eyes
    ctx.Fill(c, new EllipsePolygon(cx - s * 0.12f, cy, s * 0.05f));
    ctx.Fill(c, new EllipsePolygon(cx + s * 0.12f, cy, s * 0.05f));
}

static void DrawTelegram(IImageProcessingContext ctx, float s, Rgba32 c)
{
    // Paper plane
    var pts = new[]
    {
        new PointF(s * 0.80f, s * 0.22f), // Top right tip
        new PointF(s * 0.22f, s * 0.52f), // Left
        new PointF(s * 0.44f, s * 0.60f), // Bottom inner
        new PointF(s * 0.52f, s * 0.78f), // Bottom tip
        new PointF(s * 0.62f, s * 0.62f), // Fold
    };

    ctx.FillPolygon(c, pts[0], pts[1], pts[2]);

    // Darker shade for middle triangle
    var darker = new Rgba32(
        (byte)(c.R * 0.8f),
        (byte)(c.G * 0.8f),
        (byte)(c.B * 0.8f),
        255);
    ctx.FillPolygon(darker, pts[0], pts[2], pts[3]);
    ctx.FillPolygon(c, pts[0], pts[3], pts[4]);
}

static void DrawVk(IImageProcessingContext ctx, float s, Rgba32 c)
{
    float pad = s * 0.22f;
    float radius = s * 0.08f;
    float strokeW = s * 0.045f;
    float lw = s * 0.05f;

    // Shield outline
    ctx.Draw(
        new DrawingOptions(),
        new SolidPen(c, strokeW),
        new RectangularPolygon(pad, pad, s - pad * 2f, s - pad * 2f));

    // Stylized VK letter
    ctx.DrawLine(c, lw, new PointF(s * 0.38f, s * 0.32f), new PointF(s * 0.38f, s * 0.68f));
    ctx.DrawLine(c, lw, new PointF(s * 0.38f, s * 0.50f), new PointF(s * 0.62f, s * 0.32f));
    ctx.DrawLine(c, lw, new PointF(s * 0.44f, s * 0.46f), new PointF(s * 0.62f, s * 0.68f));
}

static void DrawExit(IImageProcessingContext ctx, float s, Rgba32 _)
{
    var exitRed = new Rgba32(255, 85, 85, 255);
    float lw = s * 0.045f;

    // Door frame (three sides)
    ctx.DrawLine(exitRed, lw, new PointF(s * 0.54f, s * 0.22f), new PointF(s * 0.24f, s * 0.22f));
    ctx.DrawLine(exitRed, lw, new PointF(s * 0.24f, s * 0.22f), new PointF(s * 0.24f, s * 0.78f));
    ctx.DrawLine(exitRed, lw, new PointF(s * 0.24f, s * 0.78f), new PointF(s * 0.54f, s * 0.78f));

    // Arrow pointing right
    ctx.DrawLine(exitRed, lw, new PointF(s * 0.40f, s * 0.50f), new PointF(s * 0.76f, s * 0.50f));
    ctx.DrawLine(exitRed, lw, new PointF(s * 0.62f, s * 0.36f), new PointF(s * 0.76f, s * 0.50f));
    ctx.DrawLine(exitRed, lw, new PointF(s * 0.62f, s * 0.64f), new PointF(s * 0.76f, s * 0.50f));
}

// ---------------------------------------------------------------------------
// Helper: approximate a PIL-style arc (ellipse bounding box + start/end degrees)
// as a sequence of line-segment points for DrawBeziers / DrawLines.
static PointF[] ArcPoints(float x0, float y0, float x1, float y1, float startDeg, float endDeg, int steps = 32)
{
    float cx = (x0 + x1) / 2f;
    float cy = (y0 + y1) / 2f;
    float rx = (x1 - x0) / 2f;
    float ry = (y1 - y0) / 2f;
    float startRad = startDeg * MathF.PI / 180f;
    float endRad   = endDeg   * MathF.PI / 180f;
    if (endRad < startRad)
    {
        endRad += 2f * MathF.PI;
    }

    var pts = new PointF[steps + 1];
    for (int i = 0; i <= steps; i++)
    {
        float t = startRad + (endRad - startRad) * i / steps;
        pts[i] = new PointF(cx + rx * MathF.Cos(t), cy + ry * MathF.Sin(t));
    }

    return pts;
}
