#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Server.Packets.World;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

// Тип без приехавшей текстуры обязан перерешаться — иначе «текстуры нет»
// закрепилось бы в кэше навсегда. Но перерешаться РАЗ НА ПРОХОД, а не раз на
// клетку: заполнение окна спрашивает метаданные тысячи раз, и цена разрешения
// (конфиг мира, поиск по атласам, три запроса к сервису текстур) умножалась на
// число клеток. Оба свойства проверяются здесь, потому что выкинуть можно
// только одно из них, и выкинуть не то — значит либо вернуть провис, либо
// навсегда оставить клетки без текстуры.
[TestFixture]
public sealed class TerrainCellMetadataCacheTests
{
    private const CellType Unready = (CellType)77;

    [Test]
    public void AnUnreadyTypeIsResolvedOncePerPass()
    {
        var textures = new CountingTextures();
        var cache = new TerrainCellMetadataCache();
        var atlases = new IAtlasDescriptor[] { new AnyAtlas() };
        var mapData = new MinimalMapData();

        cache.BeginPass();
        for (int call = 0; call < 100; call++)
        {
            cache.GetMetadata(Unready, mapData, textures, atlases);
        }

        Assert.AreEqual(1, textures.FrameRectCalls);
    }

    [Test]
    public void TheNextPassResolvesTheUnreadyTypeAgain()
    {
        var textures = new CountingTextures();
        var cache = new TerrainCellMetadataCache();
        var atlases = new IAtlasDescriptor[] { new AnyAtlas() };
        var mapData = new MinimalMapData();

        cache.BeginPass();
        cache.GetMetadata(Unready, mapData, textures, atlases);
        cache.BeginPass();
        cache.GetMetadata(Unready, mapData, textures, atlases);

        Assert.AreEqual(2, textures.FrameRectCalls);
    }

    // Приехавшая текстура объявляет тип устаревшим прямо посреди прохода.
    // Отметка прохода обязана сниматься вместе с ней.
    [Test]
    public void InvalidationInsideAPassForcesReresolution()
    {
        var textures = new CountingTextures();
        var cache = new TerrainCellMetadataCache();
        var atlases = new IAtlasDescriptor[] { new AnyAtlas() };
        var mapData = new MinimalMapData();

        cache.BeginPass();
        cache.GetMetadata(Unready, mapData, textures, atlases);
        cache.Invalidate([Unready]);
        cache.GetMetadata(Unready, mapData, textures, atlases);

        Assert.AreEqual(2, textures.FrameRectCalls);
    }

    private sealed class AnyAtlas : IAtlasDescriptor
    {
        public Texture2D? Texture => null;

        public int Size => 512;

        public bool ContainsCell(CellType cellType) => true;

        public bool IsFullyOpaque(CellType cellType) => false;
    }

    // Прямоугольник нулевой ширины означает «текстура ещё не приехала».
    private sealed class CountingTextures : ITextureService
    {
        public int FrameRectCalls { get; private set; }

        public event Action<string, Texture2D>? OnTextureLoaded
        {
            add { }
            remove { }
        }

        public int PendingCellTextureRequests => 0;

        public Texture2D? PrismaticFlowMapTexture => null;

        public Texture2D? FlowMapTexture => null;

        public Texture2D? TerrainDecalAtlasTexture => null;
        public Texture2D? TerrainDecalStoneAtlasTexture => null;

        public void RequestTexture(CellType cellType)
        {
        }

        public AtlasCoordinate GetCellTextureCoordinate(CellType cellType) => default;

        public Vector4 GetCellFrameRect(CellType cellType)
        {
            FrameRectCalls++;
            return Vector4.zero;
        }

        public int GetAnimationFrameCount(CellType cellType) => 1;

        public int GetFrameSize(CellType cellType) => 32;

        public float GetAnimationSpeedForCell(CellType cellType) => 0f;

        public UniTask<AtlasCoordinate> GetCellTextureCoordinate(
            CellType cellType,
            int globalX,
            int globalY) => throw new NotSupportedException();

        public IReadOnlyList<IAtlasDescriptor> GetAllAtlases() => throw new NotSupportedException();

        public string GetCacheStats() => string.Empty;

        public void FlushDirtyAtlases()
        {
        }

        public void Clear()
        {
        }
    }

    private sealed class MinimalMapData : IMapDataProvider
    {
        public ushort WorldWidth => 512;

        public ushort WorldHeight => 512;

        public Camera MainCamera => throw new NotSupportedException();

        public bool IsStandaloneMode => true;

        public Action? OnWorldInitialized { get; set; }

        public Action? OnWorldDataLoaded { get; set; }

        public CellConfigurationPacket GetCellConfig(CellType type) =>
            new(
                CellConfigProperties.DropsShadow,
                CellDistortionType.Neutral,
                CellAnimationType.None,
                AnimationSpeed: 0,
                FrameOffset: 0,
                Color: unchecked((int)0xFF204060),
                ReliefGroup: 0);

        public float GetMoveCooldown(CellType cellType) => 0f;

        public bool TryGetTileGroup(CellType type, out int groupID)
        {
            groupID = 0;
            return false;
        }

        public Color GetCellMinimapColor(CellType type) => Color.gray;

        public Color32 GetCellMinimapColor32(CellType type) => Color.gray;

        public void UpdateMovementSpeeds(MovementSpeedPacket packet) =>
            throw new NotSupportedException();

        public void LoadWorldInit(WorldInitPacket packet) => throw new NotSupportedException();

        public void ResetWorldState() => throw new NotSupportedException();
    }
}
