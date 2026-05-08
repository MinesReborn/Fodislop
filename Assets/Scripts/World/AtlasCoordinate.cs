using System;
using UnityEngine;
using MinesServer.Data;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Represents coordinates within a texture atlas for a specific cell type
    /// </summary>
    public struct AtlasCoordinate : IEquatable<AtlasCoordinate>
    {
        public static readonly AtlasCoordinate Empty = new AtlasCoordinate(0, 0, 0, 0, 1, 1);

        public int AtlasX { get; }
        public int AtlasY { get; }
        public int Width { get; }
        public int Height { get; }
        public int AtlasWidth { get; }
        public int AtlasHeight { get; }

        public float U1 => (float)AtlasX / AtlasWidth;
        public float V1 => (float)AtlasY / AtlasHeight;
        public float U2 => (float)(AtlasX + Width) / AtlasWidth;
        public float V2 => (float)(AtlasY + Height) / AtlasHeight;

        public Rect UVRect => new Rect(U1, V1, (float)Width / AtlasWidth, (float)Height / AtlasHeight);

        public AtlasCoordinate(int atlasX, int atlasY, int width, int height, int atlasWidth, int atlasHeight)
        {
            AtlasX = atlasX;
            AtlasY = atlasY;
            Width = width;
            Height = height;
            AtlasWidth = atlasWidth;
            AtlasHeight = atlasHeight;
        }

        public bool Equals(AtlasCoordinate other)
        {
            return AtlasX == other.AtlasX &&
                   AtlasY == other.AtlasY &&
                   Width == other.Width &&
                   Height == other.Height &&
                   AtlasWidth == other.AtlasWidth &&
                   AtlasHeight == other.AtlasHeight;
        }

        public override bool Equals(object obj)
        {
            return obj is AtlasCoordinate other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(AtlasX, AtlasY, Width, Height, AtlasWidth, AtlasHeight);
        }

        public static bool operator ==(AtlasCoordinate left, AtlasCoordinate right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AtlasCoordinate left, AtlasCoordinate right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Get UV coordinates for a specific variation
        /// </summary>
        /// <param name="variation">The cell variation</param>
        /// <param name="variationSize">Size of each variation in pixels</param>
        /// <returns>UV coordinates for the variation</returns>
        public AtlasCoordinate WithVariation(CellVariation variation, int variationSize)
        {
            if (!variation.HasVariations)
                return this;

            int variationX = variation.Horizontal ? variationSize : 0;
            int variationY = variation.Vertical ? variationSize : 0;

            return new AtlasCoordinate(
                AtlasX + variationX,
                AtlasY + variationY,
                Width,
                Height,
                AtlasWidth,
                AtlasHeight
            );
        }

        /// <summary>
        /// Get UV coordinates for a specific animation frame
        /// </summary>
        /// <param name="frameIndex">The animation frame index</param>
        /// <param name="framesPerRow">Number of frames per row in the texture</param>
        /// <param name="frameSize">Size of each frame</param>
        /// <returns>UV coordinates for the animation frame</returns>
        public AtlasCoordinate WithAnimationFrame(int frameIndex, int framesPerRow, int frameSize)
        {
            int frameX = (frameIndex % framesPerRow) * frameSize;
            int frameY = (frameIndex / framesPerRow) * frameSize;

            return new AtlasCoordinate(
                AtlasX + frameX,
                AtlasY + frameY,
                Width,
                Height,
                AtlasWidth,
                AtlasHeight
            );
        }

        /// <summary>
        /// Get UV coordinates for a specific animation frame using server-provided frame height
        /// </summary>
        /// <param name="frameIndex">The animation frame index</param>
        /// <param name="frameHeightInTiles">Frame height in tiles (each tile is CELL_SIZE pixels)</param>
        /// <returns>UV coordinates for the animation frame</returns>
        public AtlasCoordinate WithAnimationFrameFromServer(int frameIndex, int frameHeightInTiles)
        {
            int frameHeightInPixels = frameHeightInTiles * RenderingConstants.CELL_SIZE;
            
            // Calculate frame position (assuming frames are stacked vertically)
            int frameY = frameIndex * frameHeightInPixels;

            return new AtlasCoordinate(
                AtlasX,
                AtlasY + frameY,
                Width,
                frameHeightInPixels,
                AtlasWidth,
                AtlasHeight
            );
        }

        public override string ToString()
        {
            return $"AtlasCoord(X:{AtlasX}, Y:{AtlasY}, W:{Width}, H:{Height}, Atlas:{AtlasWidth}x{AtlasHeight})";
        }
    }

    /// <summary>
    /// Represents cell texture variations based on position
    /// </summary>
    public struct CellVariation
    {
        public static readonly CellVariation None = new CellVariation { Horizontal = false, Vertical = false };

        public bool Horizontal { get; set; }
        public bool Vertical { get; set; }

        public bool HasVariations => Horizontal || Vertical;

        public override string ToString()
        {
            return $"Variation(H:{Horizontal}, V:{Vertical})";
        }
    }

    /// <summary>
    /// Information about a cell texture including variations and animations
    /// </summary>
    public struct CellTextureInfo
    {
        public CellType CellType { get; set; }
        public Texture2D BaseTexture { get; set; }
        public bool HasVariations { get; set; }
        public int VariationCount { get; set; }
        public int AnimationFrames { get; set; }
        public int FramesPerRow { get; set; }
        public int FrameSize { get; set; }

        public bool HasAnimations => AnimationFrames > 1;

        public override string ToString()
        {
            return $"CellTexture(Cell:{CellType}, Variations:{HasVariations}, Animations:{HasAnimations})";
        }
    }
}