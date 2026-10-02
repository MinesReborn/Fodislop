using System;
using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class VolumeRequiresRendererFeatures : Attribute
    {
        internal HashSet<Type> TargetFeatureTypes;

        /// <param name="featureTypes">The list of required ScriptableRendererFeature types. If any of these types are missing, the VolumeComponent UI shows a warning.</param>
        public VolumeRequiresRendererFeatures(params Type[] featureTypes)
        {
            TargetFeatureTypes = (featureTypes != null) ? new HashSet<Type>(featureTypes) : new HashSet<Type>();
            TargetFeatureTypes.Remove(null);
        }
    }
}
