// Standalone C# generator for the crystal phase-map fixture.
// Extracts the original OpenMines phase map without resizing or recoloring.

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

// Root is three levels above this binary's source directory
// (tools/Kern.TerrainCrystalTests/GenerateCrystalFixtures → project root)
string thisDir = AppContext.BaseDirectory;
string root = Path.GetFullPath(Path.Combine(thisDir, "..", "..", "..", ".."));

string sourceFile = Path.GetFullPath(Path.Combine(root, "../OpenMines/client/Assets/Images/terrain.png"));
string targetFile = Path.GetFullPath(Path.Combine(root, "Assets/Resources/PrismaticFlowMap.bytes"));

using var image = Image.Load<Rgba32>(sourceFile);

if (image.Width != 2048 || image.Height != 2048)
{
    throw new InvalidOperationException(
        $"Expected terrain.png to be 2048×2048, got {image.Width}×{image.Height}");
}

// CellRender dx2=25,dy2=23,wx2=10,wy2=8; one unit = 16 pixels.
// Crop region: x=[400,560), y=[368,496) → 160×128 pixels
const int CropX = 400, CropY = 368, CropW = 160, CropH = 128;

// Extract raw RGBA bytes from crop, flip vertically (FLIP_TOP_BOTTOM)
byte[] result = new byte[CropW * CropH * 4];
for (int row = 0; row < CropH; row++)
{
    // Flip: source row (CropY + row) maps to result row (CropH - 1 - row)
    int destRow = CropH - 1 - row;
    for (int col = 0; col < CropW; col++)
    {
        var pixel = image[CropX + col, CropY + row];
        int destIdx = (destRow * CropW + col) * 4;
        result[destIdx + 0] = pixel.R;
        result[destIdx + 1] = pixel.G;
        result[destIdx + 2] = pixel.B;
        result[destIdx + 3] = pixel.A;
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
File.WriteAllBytes(targetFile, result);
Console.WriteLine($"Written {result.Length} bytes to {targetFile}");
