#ifndef KERN_TERRAIN_ANIMATION_PROFILE_INCLUDED
#define KERN_TERRAIN_ANIMATION_PROFILE_INCLUDED

static const int KERN_TERRAIN_ANIMATION_PROFILE_PRISMATIC_CRYSTAL = 1;
static const int KERN_TERRAIN_ANIMATION_PROFILE_MOLTEN_SURFACE = 2;
static const int KERN_TERRAIN_ANIMATION_PROFILE_FACETED_CRYSTAL = 3;

bool TerrainAnimationUsesFlowMap(int animationType, int animationProfile)
{
    return animationProfile != KERN_TERRAIN_ANIMATION_PROFILE_MOLTEN_SURFACE &&
        (animationType == 2 ||
            animationProfile == KERN_TERRAIN_ANIMATION_PROFILE_PRISMATIC_CRYSTAL);
}

float TerrainContourAntialiasScale(int animationProfile)
{
    return animationProfile == KERN_TERRAIN_ANIMATION_PROFILE_MOLTEN_SURFACE
        ? _MoltenContourAntialiasScale
        : 1.0;
}

#endif
