using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Categorization.CategoryInfo(Name = "R: Film Grain Textures", Order = 1000)]
    [Categorization.ElementInfo(Order = 0), HideInInspector]
    sealed class UniversalRenderPipelineFilmGrainResources : IRenderPipelineResources
    {
        [SerializeField]
        [ResourcePaths(new[]
        {
            "Textures/FilmGrain/Thin01.png",
            "Textures/FilmGrain/Thin02.png",
            "Textures/FilmGrain/Medium01.png",
            "Textures/FilmGrain/Medium02.png",
            "Textures/FilmGrain/Medium03.png",
            "Textures/FilmGrain/Medium04.png",
            "Textures/FilmGrain/Medium05.png",
            "Textures/FilmGrain/Medium06.png",
            "Textures/FilmGrain/Large01.png",
            "Textures/FilmGrain/Large02.png"
        })]
        Texture2D[] m_Textures;

        public Texture2D[] textures
        {
            get => m_Textures;
            set => this.SetValueAndNotify(ref m_Textures, value);
        }

        [SerializeField][HideInInspector] int m_Version = 0;

        public int version => m_Version;

        public bool isAvailableInPlayerBuild => true;
    }
}
