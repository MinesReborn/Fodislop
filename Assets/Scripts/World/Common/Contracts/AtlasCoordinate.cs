#nullable enable

using System;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World;

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
        public override string ToString()
        {
            return $"AtlasCoord(X:{AtlasX}, Y:{AtlasY}, W:{Width}, H:{Height}, Atlas:{AtlasWidth}x{AtlasHeight})";
        }
    }

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

    public struct CellTextureInfo
    {
        public CellType CellType { get; set; }
        public Texture2D BaseTexture { get; set; }
        public bool OwnsBaseTexture { get; set; }
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
