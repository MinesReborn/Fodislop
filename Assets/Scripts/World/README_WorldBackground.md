# World Background Renderer

This system renders the world as a flat 2D mesh that appears as the background layer in your Unity scene.

## Overview

The World Background Renderer automatically connects to the `MapStorage.Instance.CellLayer` and renders the world data as a background layer behind all other game objects. It uses the existing texture atlas system and integrates seamlessly with the dummy connection's world data.

## Components

### 1. WorldBackgroundRenderer
- Main component that handles mesh generation and rendering
- Automatically connects to `MapStorage.Instance.CellLayer`
- Generates flat 2D quads for each cell type
- Uses the existing texture atlas system for rendering
- Supports chunk-based rendering for performance

### 2. WorldBackgroundSetup
- Automatically sets up the background renderer in the scene
- Ensures proper configuration (sorting order, position, shader)
- Can be used as a component or standalone setup script

### 3. SceneSetup
- Persistent scene manager that ensures background renderer is always available
- Runs before other scripts to ensure proper initialization
- Provides static access methods for getting renderer instances

### 4. WorldBackgroundEditor (Editor Only)
- Editor utility for easy setup in the Unity Editor
- Menu items: "Tools/World/Setup Background Renderer"
- Provides setup, remove, and refresh functionality

## Usage

### Automatic Setup (Recommended)
1. The system will automatically create and configure the background renderer when the game starts
2. No manual setup required - it integrates with existing `MapStorage` and `ConnectionManager`

### Manual Setup (Editor)
1. Open the Unity Editor
2. Go to `Tools > World > Setup Background Renderer`
3. The system will create the necessary GameObjects and components

### Manual Setup (Code)
```csharp
// Create background renderer manually
var backgroundGO = new GameObject("WorldBackgroundRenderer");
var backgroundRenderer = backgroundGO.AddComponent<WorldBackgroundRenderer>();

// Configure settings
backgroundRenderer.GetComponent<MeshRenderer>().sortingOrder = -1000;
backgroundRenderer.GetComponent<Transform>().position = new Vector3(0, 0, -10);
```

## Configuration

### WorldBackgroundRenderer Settings
- **Chunk Size**: Size of mesh chunks (default: 32)
- **Render Distance**: How many chunks to render from camera (default: 15)
- **Cell Size**: Size of each cell in world units (default: 1.0f)
- **Background Z**: Z position for background rendering (default: -10f)
- **Sorting Order**: Render order for background layer (default: -1000)

### Performance Settings
- **Enable Batching**: Combine multiple chunks into single meshes (default: true)
- **Max Batch Size**: Maximum chunks to batch together (default: 32)
- **Debug Mode**: Enable debug visualization (default: false)

## Integration

The system automatically integrates with:
- `MapStorage.Instance.CellLayer` - for world data
- `WorldTextureManager.Instance` - for texture loading
- `ConnectionManager.Instance` - for network data flow
- Existing texture atlas system

## Troubleshooting

### World Not Appearing
1. Check that `MapStorage.Instance.CellLayer` is initialized
2. Verify that the dummy connection is sending `MapRegionPacket` data
3. Ensure the background renderer has proper sorting order (-1000)
4. Check that the Z position is behind other objects (-10f)

### Performance Issues
1. Reduce render distance in the inspector
2. Enable mesh batching
3. Reduce max batch size if needed
4. Use debug mode to visualize chunk boundaries

### Texture Issues
1. Ensure `WorldTextureManager` is working properly
2. Check that cell textures are being loaded from the server
3. Verify texture atlas configuration

## Notes

- The background renderer uses the Universal Render Pipeline's Unlit shader
- It automatically handles chunk loading and unloading based on camera position
- The system is designed to work with the existing world data pipeline
- No additional setup is required beyond the automatic initialization

## Files Created

- `WorldBackgroundRenderer.cs` - Main renderer component
- `WorldBackgroundSetup.cs` - Scene setup component
- `SceneSetup.cs` - Persistent scene manager
- `WorldBackgroundEditor.cs` - Editor utility (Editor folder)
- `README_WorldBackground.md` - This documentation file