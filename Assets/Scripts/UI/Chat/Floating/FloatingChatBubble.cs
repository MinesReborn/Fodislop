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

        // Облако вешается нижним центром в верхнюю грань клетки робота:
        // transform.position робота — центр его клетки (CoordinateUtils.
        // ServerToUnityPos), а спрайт робота центрирован на этой точке. Минус
        // половина клетки по X был подбором на глаз и центрировал только
        // облако шириной ровно в клетку: узкое уезжало влево, широкое — вправо.
        // Горизонтальное центрирование теперь измеряемое, в WorldLabels.Entry.
        private const float CellTopOffset = 0.5f;

        [Inject]
        private IWorldLabels _labels = null!;

        private IWorldLabel? _label;
        private Transform? _target;
        private Vector3? _anchor;
        private float _expiresAt;

        public int OwnerId { get; private set; }

        public void Init(int ownerId, string text, Transform target)
        {
            _target = target;
            _anchor = null;
            Show(ownerId, text);
        }

        /// <summary>
        /// Якорь в мировых координатах для отправителя, чей робот клиенту неизвестен:
        /// сервер кладёт в пакет свои X/Y на момент отправки именно для этого случая.
        /// </summary>
        public void Init(int ownerId, string text, Vector3 anchor)
        {
            _target = null;
            _anchor = new Vector3(anchor.x, anchor.y + CellTopOffset, anchor.z);
            Show(ownerId, text);
        }

        private void Show(int ownerId, string text)
        {
            OwnerId = ownerId;
            _expiresAt = Time.unscaledTime + Lifetime;

            _label ??= _labels.Create(WorldLabelKind.ChatBubble);
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

        // Подпись создаётся заранее, пока идёт загрузка сцены: иначе её
        // строил первый же пакет локального чата посреди кадра.
        public void Prewarm()
        {
            _label ??= _labels.Create(WorldLabelKind.ChatBubble);
            _label.SetVisible(false);
        }

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
            if (_target != null)
            {
                return new Vector3(
                    _target.position.x,
                    _target.position.y + CellTopOffset,
                    _target.position.z);
            }

            return _anchor ?? transform.position;
        }

        protected void OnDisable()
        {
            _target = null;
            _anchor = null;
            _label?.SetVisible(false);
        }

        protected void OnDestroy()
        {
            _label?.Dispose();
            _label = null;
        }
    }
}
