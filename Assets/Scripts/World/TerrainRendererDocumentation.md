# Terrain Renderer Documentation

## Overview

The Terrain Renderer is a Unity component that renders 2D terrain using a flat mesh with one quad per cell. It integrates with the existing dynamic texture atlas system for efficient rendering and supports cell variations and multi-atlas materials.

## Components

### 1. EnhancedTerrainRenderer

The main terrain rendering component that:
- Generates flat 2D meshes with quads for each cell
- Integrates with the WorldLayer for world data
- Supports multi-atlas rendering using sub-meshes
- Implements frustum culling and chunk-based rendering
- Handles texture coordinate calculation with variations

#### Configuration

- **World Layer**: Reference to the WorldLayer<CellType> containing cell data
- **Chunk Size**: Size of chunks for mesh generation (should match WorldLayer chunk size)
- **Render Distance**: Number of chunks to render from camera
- **Cell Size**: Size of each cell in world units
- **Debug Mode**: Enable debug visualization of chunk boundaries
- **Enable Batching**: Combine multiple chunks into larger meshes for performance
- **Max Batch Size**: Maximum number of chunks to batch together

#### Features

- **Chunk-based rendering**: Efficiently renders large worlds by dividing them into chunks
- **Frustum culling**: Only renders chunks visible to the camera
- **Multi-atlas support**: Handles texture atlases that exceed size limits
- **Variation support**: Applies horizontal/vertical variations based on global position
- **Dynamic loading**: Loads textures on-demand as chunks become visible

### 2. MultiAtlasMaterialManager

Manages multiple materials for different texture atlases:
- Creates materials for each atlas texture
- Handles material switching for multi-atlas rendering
- Provides shader property configuration
- Manages material lifecycle

#### Configuration

- **Base Material Template**: Template material for atlas rendering
- **Texture Property Name**: Shader property name for main texture
- **Atlas Texture Property Name**: Shader property name for atlas texture

### 3. TerrainRendererDemo

Demo script for testing the terrain renderer:
- Generates sample world data
- Provides test patterns (random, checkerboard, gradient)
- Displays demo statistics
- Runtime world data updates

#### Test Patterns

- **Random World**: Randomly distributes cell types across the world
- **Checkerboard**: Alternating pattern of two cell types
- **Gradient**: Smooth transition between different cell types
- **Simple Terrain**: Quadrant-based distribution of cell types

## Usage

### Basic Setup

1. **Create a Terrain Object**:
   ```csharp
   GameObject terrainObject = new GameObject("Terrain");
   terrainObject.AddComponent<MeshFilter>();
   terrainObject.AddComponent<MeshRenderer>();
   var terrainRenderer = terrainObject.AddComponent<EnhancedTerrainRenderer>();
   ```

2. **Configure the Renderer**:
   ```csharp
   terrainRenderer._worldLayer = yourWorldLayer;
   terrainRenderer._chunkSize = 32; // Match your WorldLayer chunk size
   terrainRenderer._renderDistance = 10;
   terrainRenderer._cellSize = 1.0f;
   ```

3. **Set Up Multi-Atlas Material Manager**:
   ```csharp
   var materialManager = terrainObject.AddComponent<MultiAtlasMaterialManager>();
   materialManager._baseMaterialTemplate = yourBaseMaterial;
   ```

### Integration with Existing Systems

The terrain renderer integrates seamlessly with your existing systems:

- **WorldLayer**: Uses your existing WorldLayer<CellType> for world data
- **Texture Atlas**: Integrates with WorldTextureManager for texture loading
- **MapManager**: Uses MapManager for cell configuration and animations
- **WorldLayerTextureExtensions**: Leverages existing texture coordinate extensions

### Performance Optimization

1. **Chunk Size**: Use chunk sizes that match your WorldLayer for optimal performance
2. **Render Distance**: Adjust based on your target hardware and world size
3. **Batching**: Enable batching for better performance with many chunks
4. **Culling**: The renderer automatically handles frustum culling

### Shader Requirements

For multi-atlas rendering, your shader should support:
- Multiple texture properties for different atlases
- UV coordinate mapping
- Material array support for sub-meshes

Example shader properties:
```hlsl
TEXTURE2D(_AtlasTexture0);
TEXTURE2D(_AtlasTexture1);
// ... more atlas textures

float4 frag(v2f i) : SV_Target
{
    // Sample appropriate atlas texture based on material index
    return SAMPLE_TEXTURE2D(_AtlasTexture0, sampler_AtlasTexture0, i.uv);
}
```

## Advanced Features

### Multi-Atlas Rendering

The renderer supports multiple texture atlases when the world contains many different cell types:

1. **Automatic Atlas Detection**: Determines which atlas contains each cell's texture
2. **Sub-mesh Generation**: Creates separate sub-meshes for different atlas materials
3. **Material Management**: Automatically manages materials for each atlas

### Variation Support

Cell variations are automatically applied based on global position:
- **Horizontal Variations**: Applied based on X coordinate
- **Vertical Variations**: Applied based on Y coordinate
- **Donut Topology**: Seamless variations across world boundaries

### Dynamic Texture Loading

Textures are loaded on-demand as chunks become visible:
- **Async Loading**: Non-blocking texture loading using UniTask
- **Caching**: Textures are cached to avoid repeated loading
- **Atlas Updates**: Atlases are updated when new textures are loaded

## Troubleshooting

### Common Issues

1. **Missing Textures**: Ensure WorldTextureManager is properly initialized
2. **Incorrect Atlas Mapping**: Verify atlas texture assignment in MultiAtlasMaterialManager
3. **Performance Issues**: Adjust render distance and batch size settings
4. **Culling Problems**: Check camera setup and chunk size configuration

### Debug Mode

Enable debug mode to visualize chunk boundaries:
- Yellow wireframe boxes show visible chunk areas
- Helps verify culling and chunk generation
- Useful for performance tuning

### Performance Monitoring

Monitor performance using:
- **Frame rate**: Watch for drops when rendering large areas
- **Memory usage**: Monitor texture and mesh memory
- **Draw calls**: Check material switching overhead

## Future Enhancements

The terrain renderer is designed to support future enhancements:

- **LOD System**: Add level-of-detail for distant terrain
- **Animation Support**: Integrate with cell animation system
- **Shadow Support**: Add shadow casting and receiving
- **Instanced Rendering**: Use GPU instancing for better performance
- **Procedural Generation**: Integrate with procedural world generation

## Example Scene Setup

```csharp
// Create terrain renderer setup
public class TerrainSetup : MonoBehaviour
{
    public WorldLayer<CellType> worldLayer;
    public int worldSize = 128;
    
    void Start()
    {
        // Create terrain object
        var terrainObject = new GameObject("Terrain");
        var meshFilter = terrainObject.AddComponent<MeshFilter>();
        var meshRenderer = terrainObject.AddComponent<MeshRenderer>();
        
        // Configure terrain renderer
        var terrainRenderer = terrainObject.AddComponent<EnhancedTerrainRenderer>();
        terrainRenderer._worldLayer = worldLayer;
        terrainRenderer._chunkSize = 32;
        terrainRenderer._renderDistance = 15;
        terrainRenderer._cellSize = 1.0f;
        
        // Configure material manager
        var materialManager = terrainObject.AddComponent<MultiAtlasMaterialManager>();
        materialManager._baseMaterialTemplate = Resources.Load<Material>("DefaultTerrainMaterial");
        
        // Set up demo world
        var demo = terrainObject.AddComponent<TerrainRendererDemo>();
        demo._terrainRenderer = terrainRenderer;
        demo._worldLayer = worldLayer;
        demo._worldSize = worldSize;
        demo._generateRandomWorld = true;
        
        // Initialize demo
        demo.InitializeDemoWorld();
    }
}
```

This setup creates a complete terrain rendering system that integrates with your existing world and texture management systems.
