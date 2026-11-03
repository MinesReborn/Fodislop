#ifndef KERN_TERRAIN_ANIMATION_PROFILE_INCLUDED
#define KERN_TERRAIN_ANIMATION_PROFILE_INCLUDED

#include "Assets/Shaders/Terrain/Cells/Format/TerrainCellFormat.hlsl"

// Resolved behavior shared by GPU data preparation, texture sampling, color
// animation and diagnostics. Each animation type maps to one complete behavior.
static const uint KERN_TERRAIN_PROFILE_BASE_ANIMATION = 0u;
static const uint KERN_TERRAIN_PROFILE_MOLTEN = 1u;
static const uint KERN_TERRAIN_PROFILE_FACETED = 2u;
static const uint KERN_TERRAIN_PROFILE_PRISMATIC = 3u;

static const uint KERN_TERRAIN_FLOW_NONE = 0u;
static const uint KERN_TERRAIN_FLOW_STANDARD = 1u;
static const uint KERN_TERRAIN_FLOW_PRISMATIC = 2u;

struct TerrainAnimationProfile
{
    uint behavior;
    uint animationType;
    uint flowSource;
    bool hasCellPhase;
};

TerrainAnimationProfile ResolveTerrainAnimationProfile(int cellAnimationType)
{
    TerrainAnimationProfile profile;
    profile.behavior = KERN_TERRAIN_PROFILE_BASE_ANIMATION;
    profile.animationType = (uint)cellAnimationType;
    profile.flowSource = KERN_TERRAIN_FLOW_NONE;
    profile.hasCellPhase = false;

    if (cellAnimationType == (int)KERN_TERRAIN_ANIMATION_TYPE_PRISMATIC)
    {
        profile.behavior = KERN_TERRAIN_PROFILE_PRISMATIC;
        profile.flowSource = KERN_TERRAIN_FLOW_PRISMATIC;
        return profile;
    }

    if (cellAnimationType == (int)KERN_TERRAIN_ANIMATION_TYPE_FACETED)
    {
        profile.behavior = KERN_TERRAIN_PROFILE_FACETED;
        profile.hasCellPhase = true;
        return profile;
    }

    if (cellAnimationType == (int)KERN_TERRAIN_ANIMATION_TYPE_MOLTEN)
    {
        profile.behavior = KERN_TERRAIN_PROFILE_MOLTEN;
        return profile;
    }

    bool usesStandardFlow = cellAnimationType == (int)KERN_TERRAIN_ANIMATION_TYPE_SHIMMER ||
        cellAnimationType == (int)KERN_TERRAIN_ANIMATION_TYPE_LEGACY2TEST ||
        cellAnimationType == (int)KERN_TERRAIN_ANIMATION_TYPE_LEGACY5TEST;
    profile.flowSource = usesStandardFlow ? KERN_TERRAIN_FLOW_STANDARD : KERN_TERRAIN_FLOW_NONE;
    profile.hasCellPhase = cellAnimationType == (int)KERN_TERRAIN_ANIMATION_TYPE_BLINKING;
    return profile;
}

#endif
