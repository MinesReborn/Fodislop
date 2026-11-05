// Standalone C# generator for the crystal phase-map fixture.
// Extracts the original OpenMines phase map without resizing or recoloring.

using StbImageSharp;

// Root is three levels above this binary's source directory
// (tools/tests/Kern.TerrainCrystalTests/GenerateCrystalFixtures → project root)
string thisDir = AppContext.BaseDirectory;
string root = Path.GetFullPath(Path.Combine(thisDir, "../../../../.."));

string sourceFile = Path.GetFullPath(Path.Combine(root, "../OpenMines/client/Assets/Images/terrain.png"));
string targetFile = Path.GetFullPath(Path.Combine(root, "Assets/Resources/World/Terrain/Animation/Flow/PrismaticFlowMap.bytes"));

using FileStream source = File.OpenRead(sourceFile);
ImageResult image = ImageResult.FromStream(source, ColorComponents.RedGreenBlueAlpha);

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
        int sourceIdx = ((CropY + row) * image.Width + CropX + col) * 4;
        int destIdx = (destRow * CropW + col) * 4;
        Buffer.BlockCopy(image.Data, sourceIdx, result, destIdx, 4);
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
File.WriteAllBytes(targetFile, result);
Console.WriteLine($"Written {result.Length} bytes to {targetFile}");
