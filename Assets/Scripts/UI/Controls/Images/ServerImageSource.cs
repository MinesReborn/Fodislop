#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.AssetPipeline.Loading;
using Kern.Core.Interfaces;
using UnityEngine;

namespace Kern.UI.Controls
{
    // Картинка, которую назвал сервер (миссия, сообщение сервера): адрес http(s) грузится из сети,
    // остальное — имя ресурса из пакетов ассетов
    public static class ServerImageSource
    {
        public static async UniTask<Texture2D?> LoadAsync(
            IAssetLoader assets,
            IWebAssetLoader web,
            string uri,
            CancellationToken token)
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out Uri? absolute) &&
                (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            {
                return await web.GetTextureAsync(uri, token);
            }

            return await assets.GetTextureAsync(uri, token);
        }
    }
}
