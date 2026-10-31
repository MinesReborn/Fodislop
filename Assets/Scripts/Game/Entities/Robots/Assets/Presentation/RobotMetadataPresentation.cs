#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.Rendering.PostProcessing;
using Kern.World;
using Kern.World.Lighting;
using Kern.World.Terrain;
using UnityEngine;
using VContainer;
using Kern.Game.Events.Playback;
using Kern.Game.Player.Movement;
using Kern.Game.Rendering;

namespace Kern.Game.Entities.Robots;

internal static class RobotMetadataPresentation
{
    internal static void ResetColor(SpriteRenderer? spriteRenderer, RobotVisuals visuals)
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = Color.white;
        }

        visuals.SetColor(Color.white);
    }

    internal static void Load(
        RobotAssetLoader loader,
        string skinPath,
        string tailPath,
        byte clanId,
        bool isLocalPlayer,
        RobotVisuals visuals,
        RobotNameplate nameplate,
        Transform transform,
        Action skinCompleted)
    {
        loader.LoadMetadataAssets(skinPath, tailPath, clanId, isLocalPlayer,
            onSkinLoaded: skinSprite =>
            {
                if (skinSprite != null)
                {
                    visuals.SetSkinSprite(skinSprite);
                    nameplate.InvalidatePosition();
                    nameplate.UpdatePosition(transform.position, visuals.SkinSprite, transform, visuals.ClanTransform);
                }

                skinCompleted();
            },
            onTailLoaded: tailTexture =>
            {
                if (tailTexture != null)
                {
                    visuals.CreateTentacles(tailTexture, transform.position);
                }
                else
                {
                    visuals.ClearTentacles();
                }
            },
            onClanLoaded: clanSprite =>
            {
                if (clanSprite != null)
                {
                    visuals.SetClanSprite(clanSprite);
                }
            });
    }

}
