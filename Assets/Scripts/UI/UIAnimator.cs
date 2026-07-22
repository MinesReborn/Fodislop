using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI
{
    public static class UIAnimator
    {
        public static async UniTask FadeIn(VisualElement el, float duration = 0.2f, CancellationToken ct = default)
        {
            if (el == null)
            {
                return;
            }

            el.style.display = DisplayStyle.Flex;
            el.pickingMode = PickingMode.Position;

            float elapsed = 0f;
            el.style.opacity = 0;

            while (elapsed < duration && !ct.IsCancellationRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                el.style.opacity = Mathf.Lerp(0f, 1f, elapsed / duration);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            el.style.opacity = 1;
        }

        public static async UniTask FadeOut(VisualElement el, float duration = 0.2f, CancellationToken ct = default)
        {
            if (el == null)
            {
                return;
            }

            el.pickingMode = PickingMode.Position;

            float elapsed = 0f;
            float startOpacity = el.style.opacity.value;

            while (elapsed < duration && !ct.IsCancellationRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                el.style.opacity = Mathf.Lerp(startOpacity, 0f, elapsed / duration);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            el.style.opacity = 0;
            el.style.display = DisplayStyle.None;
        }

        public static async UniTask SlideIn(VisualElement el, Vector2 from, float duration = 0.25f, CancellationToken ct = default)
        {
            if (el == null)
            {
                return;
            }

            el.style.display = DisplayStyle.Flex;
            el.pickingMode = PickingMode.Position;

            float elapsed = 0f;
            var startTranslate = new Translate(new Length(from.x, LengthUnit.Percent), new Length(from.y, LengthUnit.Percent));
            var endTranslate = new Translate(0, 0);

            el.style.translate = startTranslate;
            el.style.opacity = 0;

            while (elapsed < duration && !ct.IsCancellationRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / duration;
                float x = Mathf.Lerp(from.x, 0, t);
                float y = Mathf.Lerp(from.y, 0, t);
                el.style.translate = new Translate(new Length(x, LengthUnit.Percent), new Length(y, LengthUnit.Percent));
                el.style.opacity = Mathf.Lerp(0f, 1f, t);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            el.style.translate = endTranslate;
            el.style.opacity = 1;
        }

        public static async UniTask SlideOut(VisualElement el, Vector2 to, float duration = 0.25f, CancellationToken ct = default)
        {
            if (el == null)
            {
                return;
            }

            el.pickingMode = PickingMode.Position;

            float elapsed = 0f;
            float startX = el.style.translate.value.x.value;
            float startY = el.style.translate.value.y.value;

            while (elapsed < duration && !ct.IsCancellationRequested)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / duration;
                float x = Mathf.Lerp(startX, to.x, t);
                float y = Mathf.Lerp(startY, to.y, t);
                el.style.translate = new Translate(new Length(x, LengthUnit.Percent), new Length(y, LengthUnit.Percent));
                el.style.opacity = Mathf.Lerp(1f, 0f, t);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            el.style.display = DisplayStyle.None;
            el.style.opacity = 0;
        }
    }
}
