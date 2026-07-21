using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Fodinae.Scripts
{
    [RequireComponent(typeof(Image))]
    public class DynamicImage : MonoBehaviour
    {
        private Image _image;

        protected void Awake()
        {
            _image = GetComponent<Image>();
        }

        public void LoadImageFromServer(string assetFilename, string etag)
        {
            // The Action that tells the loader how to apply the texture to this specific Image component.
            Action<Texture2D> applyAction = (texture) =>
            {
                if (this != null && _image != null && texture != null)
                {
                    var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    _image.sprite = sprite;
                }
            };

            // Get a cancellation token that is cancelled when this GameObject is destroyed.
            var cancellationToken = this.GetCancellationTokenOnDestroy();

            // Start the loading process and "forget" it. The loader handles the rest.
            Fodinae.Scripts.ClientAssetLoader.Instance.LoadAndApplyTexture(applyAction, assetFilename, cancellationToken).Forget();
        }
    }
}
