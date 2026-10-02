using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal
{
    public class UniversalResourceData : UniversalResourceDataBase
    {
        internal ActiveID activeColorID { get; set; }

        /// <value>Returns the active color texture between the front and back buffer.</value>
        public TextureHandle activeColorTexture
        {
            get
            {
                if (!CheckAndWarnAboutAccessibility())
                    return TextureHandle.nullHandle;

                switch (activeColorID)
                {
                    case ActiveID.Camera:
                        return cameraColor;
                    case ActiveID.BackBuffer:
                        return backBufferColor;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public void SwitchActiveTexturesToBackbuffer()
        {
            activeColorID = UniversalResourceData.ActiveID.BackBuffer;
            activeDepthID = UniversalResourceData.ActiveID.BackBuffer;
        }

        internal ActiveID activeDepthID { get; set; }

        /// <value>TextureHandle</value>
        public TextureHandle activeDepthTexture
        {
            get
            {
                if (!CheckAndWarnAboutAccessibility())
                    return TextureHandle.nullHandle;

                switch (activeDepthID)
                {
                    case ActiveID.Camera:
                        return cameraDepth;
                    case ActiveID.BackBuffer:
                        return backBufferDepth;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        /// <value>Returns true if the backbuffer is currently in use and false otherwise.</value>
        public bool isActiveTargetBackBuffer
        {
            get
            {
                if (!isAccessible)
                {
                    Debug.LogError("Trying to access frameData outside of the current frame setup.");
                    return false;
                }

                return activeColorID == UniversalResourceData.ActiveID.BackBuffer;
            }
        }


        public TextureHandle backBufferColor
        {
            get => CheckAndGetTextureHandle(ref _backBufferColor);
            internal set => CheckAndSetTextureHandle(ref _backBufferColor, value);
        }
        private TextureHandle _backBufferColor;


        public TextureHandle backBufferDepth
        {
            get => CheckAndGetTextureHandle(ref _backBufferDepth);
            internal set => CheckAndSetTextureHandle(ref _backBufferDepth, value);
        }
        private TextureHandle _backBufferDepth;

        // intermediate camera targets

        public TextureHandle cameraColor
        {
            get => CheckAndGetTextureHandle(ref _cameraColor);
            set => CheckAndSetTextureHandle(ref _cameraColor, value);
        }
        private TextureHandle _cameraColor;

        public TextureHandle cameraDepth
        {
            get => CheckAndGetTextureHandle(ref _cameraDepth);
            set => CheckAndSetTextureHandle(ref _cameraDepth, value);
        }
        private TextureHandle _cameraDepth;

        // shadows

        public TextureHandle mainShadowsTexture
        {
            get => CheckAndGetTextureHandle(ref _mainShadowsTexture);
            set => CheckAndSetTextureHandle(ref _mainShadowsTexture, value);
        }
        private TextureHandle _mainShadowsTexture;

        public TextureHandle additionalShadowsTexture
        {
            get => CheckAndGetTextureHandle(ref _additionalShadowsTexture);
            set => CheckAndSetTextureHandle(ref _additionalShadowsTexture, value);
        }
        private TextureHandle _additionalShadowsTexture;

        // GBuffer targets

        public TextureHandle[] gBuffer
        {
            get => CheckAndGetTextureHandle(ref _gBuffer);
            set => CheckAndSetTextureHandle(ref _gBuffer, value);
        }
        private TextureHandle[] _gBuffer = new TextureHandle[RenderGraphUtils.GBufferSize];

        // camera opaque/depth/normal

        public TextureHandle cameraOpaqueTexture
        {
            get => CheckAndGetTextureHandle(ref _cameraOpaqueTexture);
            internal set => CheckAndSetTextureHandle(ref _cameraOpaqueTexture, value);
        }
        private TextureHandle _cameraOpaqueTexture;

        public TextureHandle cameraDepthTexture
        {
            get => CheckAndGetTextureHandle(ref _cameraDepthTexture);
            internal set => CheckAndSetTextureHandle(ref _cameraDepthTexture, value);
        }
        private TextureHandle _cameraDepthTexture;

        public TextureHandle cameraNormalsTexture
        {
            get => CheckAndGetTextureHandle(ref _cameraNormalsTexture);
            internal set => CheckAndSetTextureHandle(ref _cameraNormalsTexture, value);
        }
        private TextureHandle _cameraNormalsTexture;

        // motion vector

        public TextureHandle motionVectorColor
        {
            get => CheckAndGetTextureHandle(ref _motionVectorColor);
            set => CheckAndSetTextureHandle(ref _motionVectorColor, value);
        }
        private TextureHandle _motionVectorColor;

        public TextureHandle motionVectorDepth
        {
            get => CheckAndGetTextureHandle(ref _motionVectorDepth);
            set => CheckAndSetTextureHandle(ref _motionVectorDepth, value);
        }
        private TextureHandle _motionVectorDepth;

        // postFx

        public TextureHandle internalColorLut
        {
            get => CheckAndGetTextureHandle(ref _internalColorLut);
            set => CheckAndSetTextureHandle(ref _internalColorLut, value);
        }
        private TextureHandle _internalColorLut;

        internal TextureHandle bloom
        {
            get => CheckAndGetTextureHandle(ref _bloom);
            set => CheckAndSetTextureHandle(ref _bloom, value);
        }
        private TextureHandle _bloom;

        [Obsolete("AfterPostProcessColor has never been implemented. Use cameraColor instead.", false)]
        public TextureHandle afterPostProcessColor
        {
            get => CheckAndGetTextureHandle(ref _afterPostProcessColor);
            internal set => CheckAndSetTextureHandle(ref _afterPostProcessColor, value);
        }
        private TextureHandle _afterPostProcessColor;

        public TextureHandle overlayUITexture
        {
            get => CheckAndGetTextureHandle(ref _overlayUITexture);
            internal set => CheckAndSetTextureHandle(ref _overlayUITexture, value);
        }
        private TextureHandle _overlayUITexture;

        // rendering layers

        public TextureHandle renderingLayersTexture
        {
            get => CheckAndGetTextureHandle(ref _renderingLayersTexture);
            internal set => CheckAndSetTextureHandle(ref _renderingLayersTexture, value);
        }
        private TextureHandle _renderingLayersTexture;

        // decals

        public TextureHandle[] dBuffer
        {
            get => CheckAndGetTextureHandle(ref _dBuffer);
            set => CheckAndSetTextureHandle(ref _dBuffer, value);
        }
        private TextureHandle[] _dBuffer = new TextureHandle[RenderGraphUtils.DBufferSize];

        public TextureHandle dBufferDepth
        {
            get => CheckAndGetTextureHandle(ref _dBufferDepth);
            set => CheckAndSetTextureHandle(ref _dBufferDepth, value);
        }
        private TextureHandle _dBufferDepth;

        public TextureHandle ssaoTexture
        {
            get => CheckAndGetTextureHandle(ref _ssaoTexture);
            internal set => CheckAndSetTextureHandle(ref _ssaoTexture, value);
        }
        private TextureHandle _ssaoTexture;

        internal TextureHandle irradianceTexture
        {
            get => CheckAndGetTextureHandle(ref _irradianceTexture);
            set => CheckAndSetTextureHandle(ref _irradianceTexture, value);
        }
        private TextureHandle _irradianceTexture;

#if URP_SCREEN_SPACE_REFLECTION
        internal TextureHandle ssrTexture
        {
            get => CheckAndGetTextureHandle(ref _ssrTexture);
            set => CheckAndSetTextureHandle(ref _ssrTexture, value);
        }
        private TextureHandle _ssrTexture;
#endif

        internal TextureHandle stpDebugView
        {
            get => CheckAndGetTextureHandle(ref _stpDebugView);
            set => CheckAndSetTextureHandle(ref _stpDebugView, value);
        }
        private TextureHandle _stpDebugView;

        //Due to camera stacking, we sometimes need to set a specific (persistent) target texture as destination.
        //We cannot create an RG managed texture for the destination in that case as output/destination.
        //If we woulnd't have camera stacking, then the backbuffer would be the only other persistent destination.
        //The usage of this destination is currently limited to the Uberpost processing pass.
        internal TextureHandle destinationCameraColor
        {
            get => CheckAndGetTextureHandle(ref _destinationCameraColor);
            set => CheckAndSetTextureHandle(ref _destinationCameraColor, value);
        }
        private TextureHandle _destinationCameraColor;

        /// <inheritdoc />
        public override void Reset()
        {
            _backBufferColor = TextureHandle.nullHandle;
            _backBufferDepth = TextureHandle.nullHandle;
            _cameraColor = TextureHandle.nullHandle;
            _cameraDepth = TextureHandle.nullHandle;
            _mainShadowsTexture = TextureHandle.nullHandle;
            _additionalShadowsTexture = TextureHandle.nullHandle;
            _cameraOpaqueTexture = TextureHandle.nullHandle;
            _cameraDepthTexture = TextureHandle.nullHandle;
            _cameraNormalsTexture = TextureHandle.nullHandle;
            _motionVectorColor = TextureHandle.nullHandle;
            _motionVectorDepth = TextureHandle.nullHandle;
            _internalColorLut = TextureHandle.nullHandle;
            _bloom = TextureHandle.nullHandle;
            _afterPostProcessColor = TextureHandle.nullHandle;
            _overlayUITexture = TextureHandle.nullHandle;
            _renderingLayersTexture = TextureHandle.nullHandle;
            _dBufferDepth = TextureHandle.nullHandle;
            _ssaoTexture = TextureHandle.nullHandle;
            _irradianceTexture = TextureHandle.nullHandle;
#if URP_SCREEN_SPACE_REFLECTION
            _ssrTexture = TextureHandle.nullHandle;
#endif
            _stpDebugView = TextureHandle.nullHandle;
            _destinationCameraColor = TextureHandle.nullHandle;

            for (int i = 0; i < _gBuffer.Length; i++)
                _gBuffer[i] = TextureHandle.nullHandle;

            for (int i = 0; i < _dBuffer.Length; i++)
                _dBuffer[i] = TextureHandle.nullHandle;
        }
    }
}
