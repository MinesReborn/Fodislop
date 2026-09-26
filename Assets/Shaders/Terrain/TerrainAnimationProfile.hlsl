#ifndef KERN_TERRAIN_ANIMATION_PROFILE_INCLUDED
#define KERN_TERRAIN_ANIMATION_PROFILE_INCLUDED

static const int KERN_TERRAIN_ANIMATION_PROFILE_PRISMATIC_CRYSTAL = 1;
static const int KERN_TERRAIN_ANIMATION_PROFILE_FACETED_CRYSTAL = 3;

bool TerrainAnimationUsesFlowMap(int animationType, int animationProfile)
{
    if (animationProfile == KERN_TERRAIN_ANIMATION_PROFILE_PRISMATIC_CRYSTAL)
    {
        return true;
    }

    if (animationProfile == KERN_TERRAIN_ANIMATION_PROFILE_FACETED_CRYSTAL)
    {
        return false;
    }

    return animationType == 2;
}

#endif
