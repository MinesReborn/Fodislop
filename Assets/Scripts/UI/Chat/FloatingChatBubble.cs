#nullable enable

using Kern.Core.Interfaces;
using UnityEngine;
using VContainer;

namespace Kern.UI
{
    public class FloatingChatBubble : MonoBehaviour
    {
        private const float Lifetime = 3f;
        private const float FollowWeight = 0.3f;
        private const float TargetOffsetX = -0.5f;

        [Inject]
        private IWorldLabels _labels = null!;

        private IWorldLabel? _label;
        private Transform? _target;
        private float _expiresAt;

        public int OwnerID { get; private set; }

        public void Init(int ownerID, string text, Transform target)
        {
            OwnerID = ownerID;
            _target = target;
            _expiresAt = Time.unscaledTime + Lifetime;

            _label ??= _labels.Create(chatBubble: true);
            _label.SetText(text);
            _label.SetOpacity(1f);

            // Первый кадр ставится точно в цель, без сглаживания: иначе облако
            // прилетало бы к роботу из точки, где висело прошлое сообщение.
            transform.position = ResolveTargetPosition();
            _label.SetPosition(transform.position);
            _label.SetVisible(true);
            gameObject.SetActive(true);
        }

        public void Expire() => gameObject.SetActive(false);

        protected void Update()
        {
            Vector3 target = ResolveTargetPosition();
            transform.position =
                (FollowWeight * target) + ((1f - FollowWeight) * transform.position);
            _label?.SetPosition(transform.position);

            if (Time.unscaledTime > _expiresAt)
            {
                gameObject.SetActive(false);
            }
        }

        private Vector3 ResolveTargetPosition()
        {
            // Робот мог исчезнуть, пока облако живёт: тогда оно доживает свои
            // секунды там, где остановилось, а не прыгает в начало координат.
            Vector3 position = _target != null ? _target.position : transform.position;
            position.x += _target != null ? TargetOffsetX : 0f;
            return position;
        }

        protected void OnDisable()
        {
            _target = null;
            _label?.SetVisible(false);
        }

        protected void OnDestroy()
        {
            _label?.Dispose();
            _label = null;
        }
    }
}
