#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Player;
using Fodinae.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fodinae.UI;

internal static class DebugOverlayGizmos
{
    public static void DrawWorldDebugGizmos(
        bool showGrid,
        bool showCursor,
        MapManager? mapManager,
        IWorldDataStorage? storage,
        ILocalPlayerState? localPlayer,
        IGameplayCamera? gameplayCamera)
    {
        if (mapManager == null || !mapManager.IsWorldInitialized)
        {
            return;
        }

        ILocalPlayer? player = localPlayer?.Current;
        if (showGrid && player != null)
        {
            DrawChunkGrid(player.Position, mapManager.WorldHeight);
        }

        if (showCursor)
        {
            DrawCursorHighlight(mapManager, storage, gameplayCamera);
        }
    }

    private static void DrawChunkGrid(Vector2Int playerServerPos, int worldHeight)
    {
        const int chunkSize = ProjectRuntimeContracts.World.ChunkSize;
        int playerChunkX = playerServerPos.x / chunkSize;
        int playerChunkY = playerServerPos.y / chunkSize;

        for (int cx = playerChunkX - 1; cx <= playerChunkX + 1; cx++)
        {
            for (int cy = playerChunkY - 1; cy <= playerChunkY + 1; cy++)
            {
                if (cx < 0 || cy < 0)
                {
                    continue;
                }

                int serverLeft = cx * chunkSize;
                int serverTop = cy * chunkSize;
                Vector3 origin = CoordinateUtils.ServerToUnityPos(serverLeft, serverTop, worldHeight);
                Vector3 center = origin + new Vector3(chunkSize * 0.5f - 0.5f, -(chunkSize * 0.5f - 0.5f), 0f);

                FodinaeGizmos.DrawBounds(center, new Vector2(chunkSize, chunkSize), new Color(0f, 0.8f, 1f, 0.4f));
            }
        }
    }

    private static void DrawCursorHighlight(
        MapManager mapManager,
        IWorldDataStorage? storage,
        IGameplayCamera? gameplayCamera)
    {
        Camera? cam = gameplayCamera?.Camera;
        if (cam == null || Mouse.current == null)
        {
            return;
        }

        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(mouseScreen.x, mouseScreen.y, -cam.transform.position.z));
        if (worldPos.y < 0f || worldPos.y >= mapManager.WorldHeight || worldPos.x < 0f)
        {
            return;
        }

        Vector2Int serverCell = CoordinateUtils.UnityToServerPos(worldPos, mapManager.WorldHeight);
        Vector3 cellCenter = CoordinateUtils.ServerToUnityPos(serverCell.x, serverCell.y, mapManager.WorldHeight);

        bool passable = false;
        if (storage?.CellLayer != null)
        {
            CellType type = storage.CellLayer.GetCellSync(serverCell.x, serverCell.y);
            var config = mapManager.GetCellConfig(type);
            passable = type == CellType.Empty || ((CellConfigProperties)config.Properties).HasFlag(CellConfigProperties.Passable);
        }

        Color highlightColor = passable ? Color.green : Color.red;
        FodinaeGizmos.DrawBounds(cellCenter, Vector2.one * 0.95f, highlightColor);
    }
}
