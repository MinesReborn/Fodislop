# Texture Storage System for Dummy Connection

This document explains how to use the hybrid texture storage system that allows you to provide real images instead of randomly generated ones for the DummyConnection.

## Overview

The Texture Storage System provides a seamless way to load real texture files while maintaining backward compatibility with random texture generation as a fallback. This is particularly useful for testing and development when you want to use actual game assets instead of procedurally generated textures.

## How It Works

1. **Smart Folder Detection**: Automatically detects the best texture folder based on your environment (Unity Editor vs. standalone build)
2. **Local Storage Priority**: First tries to load textures from your local storage folder
3. **Fallback Generation**: If a texture file is not found, automatically generates a random texture
4. **Caching**: Caches loaded textures in memory for better performance
5. **Seamless Integration**: Works transparently with the existing DummyConnection

## Folder Structure

The system automatically detects and uses texture folders in this priority order:

### For Unity Editor (Development)
```
Fodinae/
├── Assets/
│   ├── Scripts/
│   │   └── Networking/
│   │       └── Connection/
│   │           └── Client/
│   │               ├── DummyConnection.cs
│   │               ├── TextureStorageManager.cs
│   │               └── TextureStorageTest.cs
│   └── Textures/           ← Preferred location for development
│       └── Cells/
│           ├── 1.png
│           ├── 2.png
│           └── 42.png
```

### For Standalone Builds
```
Game/
├── Game.exe
├── Textures/               ← Preferred location for builds
│   └── Cells/
│       ├── 1.png
│       ├── 2.png
│       └── 42.png
```

### Alternative Build Locations (checked in order)
1. `../Textures/` (relative to executable)
2. `Textures/` (in same directory as executable)
3. Persistent data path (fallback)

## File Naming Convention

Textures must be named according to the cell type they represent:

- **Format**: `/Cells/{cellType}.png`
- **Examples**:
  - `/Cells/1.png` - Cell type 1
  - `/Cells/42.png` - Cell type 42
  - `/Cells/255.png` - Cell type 255

The system automatically handles the leading slash in filenames.

## Usage

### Basic Usage

The system works automatically with the DummyConnection. Simply place your PNG files in the appropriate folder and they will be loaded when requested.

### Manual Testing

You can test the texture storage system using the `TextureStorageTest` component:

1. Add the `TextureStorageTest` component to any GameObject in your scene
2. Configure the test filenames in the Inspector
3. Enable debug logging to see detailed output
4. Run the scene to see test results

### Programmatic Usage

```csharp
using Fodinae.Assets.Scripts.Networking.Connection.Client;

// Get texture data
var textureData = await TextureStorageManager.Instance.GetTextureData("/Cells/1.png");

// Check if texture exists in storage
bool hasTexture = TextureStorageManager.Instance.HasTexture("/Cells/1.png");

// Get current texture folder path
string folderPath = TextureStorageManager.Instance.GetTextureFolderPath();

// Clear cache (useful for testing)
TextureStorageManager.Instance.ClearCache();

// Get cache statistics
string stats = TextureStorageManager.Instance.GetCacheStats();
```

## Configuration

### TextureStorageManager Settings

The `TextureStorageManager` component has the following configurable properties:

- **Enable Debug Logging**: Toggle detailed logging for texture loading operations
- **Fallback Texture Size**: Size of randomly generated textures (default: 32x32)

### TextureStorageTest Settings

The `TextureStorageTest` component has the following configurable properties:

- **Enable Debug Logging**: Toggle detailed logging during tests
- **Test Filenames**: Array of texture filenames to test during startup

## Debugging

### Log Messages

The system provides detailed logging when debug mode is enabled:

```
[TextureStorageManager] Using texture folder: C:/Project/Assets/Textures
[TextureStorageManager] Loaded texture from storage: Cells/1.png
[TextureStorageManager] Cache hit for: Cells/1.png
[TextureStorageManager] Texture not found, generating fallback: Cells/999.png
```

### Test Output

The `TextureStorageTest` component provides comprehensive test results:

```
[TextureStorageTest] === TEST RESULTS ===
[TextureStorageTest] /Cells/1.png: SUCCESS (FROM_STORAGE) in 0.002s
[TextureStorageTest] /Cells/2.png: SUCCESS (FALLBACK_GENERATED) in 0.001s
[TextureStorageTest] /Cells/42.png: SUCCESS (FROM_STORAGE) in 0.001s
[TextureStorageTest] Cache test for /Cells/1.png: 0.000s
[TextureStorageTest] Final cache stats: Texture Cache: 3 entries, Folder: C:/Project/Assets/Textures
[TextureStorageTest] === TEST COMPLETE ===
```

## Performance

### Caching

- Textures are cached in memory after first load
- Cache hits are significantly faster than file I/O
- Cache is persistent across the application lifetime
- Cache can be cleared manually for testing or memory management

### Asynchronous Loading

- All texture loading operations are asynchronous
- File I/O operations don't block the main thread
- Random texture generation happens on the main thread (required for Unity API)

## Troubleshooting

### Common Issues

1. **Textures not loading from storage**
   - Check that files are in the correct folder
   - Verify file naming convention (`/Cells/{cellType}.png`)
   - Ensure files are valid PNG format

2. **Wrong texture folder being used**
   - Check the debug logs to see which folder path is being used
   - Verify folder exists and contains your texture files

3. **Performance issues**
   - Use the `TextureStorageTest` to measure load times
   - Check cache hit rates in the cache statistics
   - Consider pre-loading frequently used textures

### Error Messages

- `[TextureStorageManager] Cannot load texture: filename is null or empty` - Check your filename format
- `[TextureStorageManager] Failed to load texture from storage` - Check file permissions and path
- `[TextureStorageManager] Failed to generate random texture` - Check Unity's Texture2D API availability

## Integration with Existing Systems

The texture storage system integrates seamlessly with:

- **DummyConnection**: Automatically uses the new system for texture requests
- **WorldTextureManager**: Can use textures loaded by this system
- **ClientAssetLoader**: Works alongside the existing asset loading system

## Future Enhancements

Potential future improvements include:

- Support for different image formats (JPG, BMP, etc.)
- Texture compression and optimization
- Batch loading for multiple textures
- Texture preprocessing (resizing, format conversion)
- Integration with Unity's Addressable Assets system

## Examples

### Development Workflow

1. Create your texture files (e.g., `1.png`, `2.png`, `42.png`)
2. Place them in `Assets/Textures/Cells/`
3. Run your Unity project
4. The DummyConnection will automatically use your real textures
5. Missing textures will fall back to random generation

### Build Workflow

1. Build your project
2. Create a `Textures/Cells/` folder next to your executable
3. Copy your texture files to the build folder
4. Run the built application
5. Your real textures will be loaded automatically

This system provides a flexible and powerful way to use real textures in your development and testing workflow while maintaining the convenience of automatic fallback generation.
