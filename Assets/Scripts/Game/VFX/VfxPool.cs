#nullable enable

using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using UnityEngine;
using UnityEngine.Serialization;
using VContainer;

namespace Kern.Game
{
    // Один общий пул слотов. Слот — пустой носитель: что в нём нарисовать,
    // решает имя эффекта из пакета и стриминг ассетов. Раньше пул делился на
    // клиентские типы (Bz, Destroy, Death, Custom) — урезанный дубль
    // протокольного VFX, который ничем, кроме имени ведра, не отличался.
    public class VfxPool : MonoBehaviour, IVfxService
    {
        [SerializeField]
        [FormerlySerializedAs("_defaultInitialSize")]
        private int _initialSize = 2;

        [SerializeField]
        private float _shrinkDelay = 30f;

        private readonly Queue<PooledSlot> _available = new();
        private readonly List<PooledSlot> _active = new();
        private int _targetSize;
        private float _lastReleaseTime;
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
                    _active.RemoveAt(i);
                }
            }

            ShrinkIfIdle(Time.realtimeSinceStartup);
        }

        public IVfxSlot? Acquire()
        {
            EnsureInitialized();
            if (_sceneObjects == null || _entityBatchRenderer == null)
            {
                return null;
            }

            PooledSlot slot = _available.Count > 0 ? _available.Dequeue() : CreatePooledSlot();
            slot.IsInPool = false;
            _active.Add(slot);
            _targetSize = Mathf.Max(_targetSize, _available.Count + _active.Count);
            if (slot.GameObject != null)
            {
                slot.GameObject.SetActive(true);
            }

            return slot;
        }

        public void Release(IVfxSlot slot)
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
                pooled.GameObject.SetActive(false);
            }

            pooled.IsInPool = true;
            _active.RemoveAt(index);
            _available.Enqueue(pooled);
            _lastReleaseTime = Time.realtimeSinceStartup;
        }

        private void EnsureInitialized()
        {
            if (_initialized || _sceneObjects == null || _entityBatchRenderer == null)
            {
                return;
            }

            _initialized = true;
            _targetSize = Mathf.Max(_initialSize, 1);
            _lastReleaseTime = Time.realtimeSinceStartup;
            while (_available.Count + _active.Count < _targetSize)
            {
                _available.Enqueue(CreatePooledSlot());
            }
        }

        private void ShrinkIfIdle(float now)
        {
            if (_available.Count <= _initialSize || now - _lastReleaseTime < _shrinkDelay)
            {
                return;
            }

            int excess = _available.Count - Mathf.Max(_targetSize, _initialSize);
            for (int i = 0; i < excess && _available.Count > 0; i++)
            {
                DestroyPooledSlot(_available.Dequeue());
            }

            if (_targetSize > _initialSize)
            {
                _targetSize = Mathf.Max(_initialSize, _targetSize - 1);
            }
        }

        private PooledSlot CreatePooledSlot()
        {
            GameObject go = _sceneObjects.Create("PooledVfx", RuntimeOwner.VFX);
            go.SetActive(false);

            WorldEntityBatchRenderer.SpriteHandle? handle =
                _entityBatchRenderer?.RegisterSprite(go.transform, -500);

            return new PooledSlot
            {
                GameObject = go,
                EntityBatchRenderer = _entityBatchRenderer!,
                BatchHandle = handle!,
                IsInPool = true,
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

        public sealed class PooledSlot : IVfxSlot
        {
            public GameObject? GameObject { get; set; }
            public WorldEntityBatchRenderer EntityBatchRenderer = null!;
            public WorldEntityBatchRenderer.SpriteHandle BatchHandle = null!;
            public bool IsInPool;

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
