#nullable enable

using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using UnityEngine;
using UnityEngine.Serialization;
using VContainer;

using Kern.Game.Rendering;

namespace Kern.Game.Visual.Effects.Pooling
{
    // Один общий пул слотов. Слот — пустой носитель: что в нём нарисовать,
    // решает имя эффекта из пакета и стриминг ассетов. Раньше пул делился на
    // клиентские типы (Bz, Destroy, Death, Custom) — урезанный дубль
    // протокольного VisualEffect, который ничем, кроме имени ведра, не отличался.
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Kern.Game", sourceAssembly: null, sourceClassName: "VFXPool")]
    public class VisualEffectPool : MonoBehaviour, IVisualEffectService
    {
        [SerializeField]
        [FormerlySerializedAs("_defaultInitialSize")]
        private int _initialSize = 2;

        [SerializeField]
        private float _shrinkDelay = 30f;

        private readonly Queue<PooledSlot> _available = new();
        private readonly List<PooledSlot> _active = new();
        private bool _initialized;

        [Inject]
        private WorldEntityBatchRenderer _entityBatchRenderer = null!;
        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;

        protected void Start()
        {
            EnsureInitialized();
        }

        protected void OnDestroy()
        {
            while (_available.Count > 0)
            {
                DestroyPooledSlot(_available.Dequeue());
            }

            foreach (PooledSlot slot in _active)
            {
                DestroyPooledSlot(slot);
            }

            _active.Clear();
            _initialized = false;
        }

        protected void Update()
        {
            // Слот, чей объект уничтожили снаружи (смена сцены), из учёта
            // убирается, иначе пул держал бы мёртвую ссылку вечно.
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].GameObject == null)
                {
                    DestroyPooledSlot(_active[i]);
                    _active.RemoveAt(i);
                }
            }

            ShrinkExpiredSlots(Time.realtimeSinceStartup);
        }

        public IVisualEffectSlot? Acquire(int sortingOrder = -500)
        {
            EnsureInitialized();
            if (_sceneObjects == null || _entityBatchRenderer == null)
            {
                return null;
            }

            PooledSlot? slot = null;
            int availableCount = _available.Count;
            for (int index = 0; index < availableCount; index++)
            {
                PooledSlot candidate = _available.Dequeue();
                if (slot == null && candidate.SortingOrder == sortingOrder)
                {
                    slot = candidate;
                }
                else
                {
                    _available.Enqueue(candidate);
                }
            }

            slot ??= CreatePooledSlot(sortingOrder);
            slot.IsInPool = false;
            _active.Add(slot);
            if (slot.GameObject != null)
            {
                slot.GameObject.transform.localScale = Vector3.one;
                slot.GameObject.SetActive(true);
            }

            return slot;
        }

        public void Release(IVisualEffectSlot slot)
        {
            if (slot is not PooledSlot pooled || pooled.IsInPool)
            {
                return;
            }

            int index = _active.IndexOf(pooled);
            if (index < 0)
            {
                return;
            }

            pooled.ResetVisual();
            if (pooled.GameObject != null)
            {
                pooled.GameObject.transform.rotation = Quaternion.identity;
                pooled.GameObject.transform.localScale = Vector3.one;
                pooled.GameObject.SetActive(false);
            }

            pooled.IsInPool = true;
            pooled.ReleasedAt = Time.realtimeSinceStartup;
            _active.RemoveAt(index);
            _available.Enqueue(pooled);
        }

        private void EnsureInitialized()
        {
            if (_initialized || _sceneObjects == null || _entityBatchRenderer == null)
            {
                return;
            }

            _initialized = true;
            int initialSize = Mathf.Max(_initialSize, 1);
            while (_available.Count + _active.Count < initialSize)
            {
                _available.Enqueue(CreatePooledSlot(-500));
            }
        }

        private void ShrinkExpiredSlots(float now)
        {
            int minimumSize = Mathf.Max(_initialSize, 1);
            if (_available.Count <= minimumSize)
            {
                return;
            }

            const int MaximumSlotsDestroyedPerFrame = 4;
            int destroyed = 0;
            while (_available.Count > minimumSize && destroyed < MaximumSlotsDestroyedPerFrame)
            {
                PooledSlot oldest = _available.Peek();
                if (now - oldest.ReleasedAt < _shrinkDelay)
                {
                    break;
                }

                DestroyPooledSlot(_available.Dequeue());
                destroyed++;
            }
        }

        private PooledSlot CreatePooledSlot(int sortingOrder)
        {
            GameObject go = _sceneObjects.Create("PooledVisualEffect", RuntimeOwner.VisualEffects);
            go.SetActive(false);

            WorldEntityBatchRenderer.SpriteHandle? handle =
                _entityBatchRenderer?.RegisterSprite(go.transform, sortingOrder);

            return new PooledSlot
            {
                GameObject = go,
                EntityBatchRenderer = _entityBatchRenderer!,
                BatchHandle = handle!,
                SortingOrder = sortingOrder,
                IsInPool = true,
                ReleasedAt = Time.realtimeSinceStartup,
            };
        }

        private void DestroyPooledSlot(PooledSlot slot)
        {
            if (_entityBatchRenderer != null && slot.BatchHandle != null)
            {
                _entityBatchRenderer.UnregisterSprite(slot.BatchHandle);
            }

            if (slot.GameObject != null)
            {
                Destroy(slot.GameObject);
            }
        }

        public sealed class PooledSlot : IVisualEffectSlot
        {
            public GameObject? GameObject { get; set; }
            public WorldEntityBatchRenderer EntityBatchRenderer = null!;
            public WorldEntityBatchRenderer.SpriteHandle BatchHandle = null!;
            public int SortingOrder;
            public bool IsInPool;
            public float ReleasedAt;

            public void SetSprite(Sprite? sprite)
            {
                EntityBatchRenderer.SetSprite(BatchHandle, sprite);
            }

            public void SetColor(Color color)
            {
                BatchHandle.SetColor(color);
            }

            public void SetEnabled(bool enabled)
            {
                BatchHandle.SetEnabled(enabled);
            }

            public void ResetVisual()
            {
                SetSprite(null);
                SetColor(Color.white);
                SetEnabled(false);
            }
        }
    }
}
