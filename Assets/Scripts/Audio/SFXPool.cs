using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Utils;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Audio
{
    public class SFXPool : SingletonMonoBehaviour<SFXPool>
    {
        [Serializable]
        public struct PoolConfig
        {
            [SerializeField]
            private SFX _sfxType;

            [SerializeField]
            private int _initialSize;

            public SFX SfxType => _sfxType;

            public int InitialSize => _initialSize;
        }

        [SerializeField]
        private PoolConfig[] _configs;

        [SerializeField]
        private int _defaultInitialSize = 2;

        [SerializeField]
        private float _shrinkDelay = 30f;

        [SerializeField]
        private int _softMaxPerType = 30;

        private readonly Dictionary<SFX, SubPool> _pools = new();
        private readonly List<SoundEffectInstance> _fallbackInstances = new();

        protected override void OnAwake()
        {
            InitializePools();
        }

        protected override void OnDestroyed()
        {
            foreach (var kvp in _pools)
            {
                TeardownSubPool(kvp.Value);
            }

            _pools.Clear();
        }

        private void Update()
        {
            var now = Time.realtimeSinceStartup;

            for (int i = _fallbackInstances.Count - 1; i >= 0; i--)
            {
                var fb = _fallbackInstances[i];
                fb.Update();
                if (fb.IsDisposed)
                {
                    _fallbackInstances.RemoveAt(i);
                }
            }

            foreach (var kvp in _pools)
            {
                var pool = kvp.Value;
                var activeList = pool.Active;

                for (int i = activeList.Count - 1; i >= 0; i--)
                {
                    var slot = activeList[i];

                    if (slot.IsManagedExternally)
                    {
                        continue;
                    }

                    if (!slot.AudioSource.isPlaying && (now - slot.PlayStartTime) > 0.05f)
                    {
                        ReleaseInternal(pool, slot, i);
                        continue;
                    }

                    var clipLength = slot.AudioSource.clip != null
                        ? slot.AudioSource.clip.length
                        : 0f;

                    if (clipLength > 0f && (now - slot.PlayStartTime) >= clipLength + 0.1f)
                    {
                        ReleaseInternal(pool, slot, i);
                    }
                }

                ShrinkIfIdle(pool, now);
            }
        }

        private void InitializePools()
        {
            if (_configs == null)
            {
                return;
            }

            foreach (var cfg in _configs)
            {
                var pool = GetOrCreateSubPool(cfg.SfxType);
                pool.TargetSize = Mathf.Max(cfg.InitialSize, 1);
                LoadClipAsync(pool).Forget();
            }
        }

        private SubPool GetOrCreateSubPool(SFX sfxType)
        {
            if (!_pools.TryGetValue(sfxType, out var pool))
            {
                pool = new SubPool
                {
                    SfxType = sfxType,
                    FileName = $"audio/{sfxType.ToString().ToLowerInvariant()}",
                    TargetSize = _defaultInitialSize,
                    LastReleaseTime = Time.realtimeSinceStartup,
                };
                _pools[sfxType] = pool;
            }

            return pool;
        }

        private static async UniTaskVoid LoadClipAsync(SubPool pool)
        {
            if (pool.ClipLoading || pool.ClipReady)
            {
                return;
            }

            pool.ClipLoading = true;

            try
            {
                var loader = ClientAssetLoader.Instance;
                if (loader == null)
                {
                    return;
                }

                var clip = await loader.GetAudioAsync(pool.FileName, timeoutSeconds: 30);

                if (clip != null)
                {
                    pool.CachedClip = clip;
                    pool.ClipReady = true;

                    SpawnToTargetSize(pool);

                    foreach (var slot in pool.Available)
                    {
                        slot.AudioSource.clip = clip;
                    }
                }
                else
                {
                    Debug.LogWarning(
                        $"[SFXPool] No audio data for {pool.SfxType} ({pool.FileName})");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[SFXPool] Failed to load clip for {pool.SfxType}: {ex.Message}");
            }
            finally
            {
                pool.ClipLoading = false;
            }
        }

        public void Play(SFX sfxType, float volume)
        {
            var pool = GetOrCreateSubPool(sfxType);

            var needsLoading = !pool.ClipLoading && !pool.ClipReady;

            if (needsLoading)
            {
                LoadClipAsync(pool).Forget();
            }

            if (pool.ClipReady)
            {
                var slot = AcquireInternal(pool);

                if (slot == null)
                {
                    return;
                }

                slot.PlayStartTime = Time.realtimeSinceStartup;
                slot.GameObject.SetActive(true);

                slot.AudioSource.clip = pool.CachedClip;
                slot.AudioSource.volume = volume;
                slot.AudioSource.time = 0f;
                slot.AudioSource.Play();
            }
            else
            {
                var fallback = new SoundEffectInstance(
                    sfxType,
                    pool.FileName,
                    volume);

                pool.PeakActiveCount = Mathf.Max(pool.PeakActiveCount, pool.Active.Count + 1);

                _fallbackInstances.Add(fallback);
            }
        }

        public PooledSlot Acquire(SFX sfxType)
        {
            var pool = GetOrCreateSubPool(sfxType);

            if (!pool.ClipLoading && !pool.ClipReady)
            {
                LoadClipAsync(pool).Forget();
            }

            var slot = AcquireInternal(pool);
            if (slot == null)
            {
                return null;
            }

            slot.SfxType = sfxType;
            slot.IsManagedExternally = true;
            slot.PlayStartTime = Time.realtimeSinceStartup;
            slot.GameObject.SetActive(true);

            if (pool.ClipReady && pool.CachedClip != null)
            {
                slot.AudioSource.clip = pool.CachedClip;
            }

            return slot;
        }

        public void Release(PooledSlot slot)
        {
            if (slot == null || slot.IsInPool)
            {
                return;
            }

            if (!_pools.TryGetValue(slot.SfxType, out var pool))
            {
                return;
            }

            int idx = pool.Active.IndexOf(slot);
            if (idx < 0)
            {
                return;
            }

            ReleaseInternal(pool, slot, idx);
        }

        private PooledSlot AcquireInternal(SubPool pool)
        {
            PooledSlot slot;

            if (pool.Available.Count > 0)
            {
                slot = pool.Available.Dequeue();
                slot.IsInPool = false;
                pool.Active.Add(slot);
                return slot;
            }

            var total = pool.Available.Count + pool.Active.Count;

            if (total < _softMaxPerType)
            {
                slot = CreatePooledSlot(pool);
                slot.IsInPool = false;
                pool.Active.Add(slot);

                pool.TargetSize = Mathf.Max(pool.TargetSize, total + 1);
                pool.PeakActiveCount = Mathf.Max(pool.PeakActiveCount, pool.Active.Count);

                return slot;
            }

            slot = pool.Active[0];
            slot.AudioSource.Stop();
            slot.IsManagedExternally = false;
            pool.Active.RemoveAt(0);

            slot.IsInPool = false;
            pool.Active.Add(slot);

            return slot;
        }

        private static void ReleaseInternal(SubPool pool, PooledSlot slot, int activeIndex)
        {
            slot.AudioSource.Stop();
            slot.AudioSource.clip = null;
            slot.SpriteRenderer.sprite = null;
            slot.SpriteRenderer.color = Color.white;
            slot.SpriteRenderer.enabled = true;
            slot.GameObject.SetActive(false);
            slot.IsManagedExternally = false;
            slot.IsInPool = true;
            pool.Active.RemoveAt(activeIndex);
            pool.Available.Enqueue(slot);
            pool.LastReleaseTime = Time.realtimeSinceStartup;
        }

        private static void TeardownSubPool(SubPool pool)
        {
            while (pool.Available.Count > 0)
            {
                var slot = pool.Available.Dequeue();
                DestroyPooledSlot(slot);
            }

            foreach (var slot in pool.Active)
            {
                slot.AudioSource.Stop();
                DestroyPooledSlot(slot);
            }

            pool.Active.Clear();

            if (pool.CachedClip != null)
            {
                Destroy(pool.CachedClip);
                pool.CachedClip = null;
            }
        }

        private static void DestroyPooledSlot(PooledSlot slot)
        {
            if (slot.GameObject != null)
            {
                Destroy(slot.GameObject);
            }
        }

        private static PooledSlot CreatePooledSlot(SubPool pool)
        {
            var go = new GameObject($"PooledSFX_{pool.SfxType}");
            go.SetActive(false);

            if (Application.isPlaying)
            {
                DontDestroyOnLoad(go);
            }

            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;

            if (pool.ClipReady)
            {
                source.clip = pool.CachedClip;
            }

            var renderer = go.AddComponent<SpriteRenderer>();

            return new PooledSlot
            {
                SfxType = pool.SfxType,
                GameObject = go,
                AudioSource = source,
                SpriteRenderer = renderer,
                PlayStartTime = 0f,
                IsInPool = true,
            };
        }

        private static void SpawnToTargetSize(SubPool pool)
        {
            var total = pool.Available.Count + pool.Active.Count;
            var needed = pool.TargetSize - total;

            for (int i = 0; i < needed; i++)
            {
                var slot = CreatePooledSlot(pool);
                pool.Available.Enqueue(slot);
            }
        }

        private void ShrinkIfIdle(SubPool pool, float now)
        {
            if (pool.Available.Count <= _defaultInitialSize)
            {
                return;
            }

            if ((now - pool.LastReleaseTime) < _shrinkDelay)
            {
                return;
            }

            var target = Mathf.Max(pool.TargetSize, _defaultInitialSize);
            var excess = pool.Available.Count - target;

            for (int i = 0; i < excess && pool.Available.Count > 0; i++)
            {
                var idle = pool.Available.Dequeue();
                DestroyPooledSlot(idle);
            }

            if (pool.TargetSize > _defaultInitialSize)
            {
                pool.TargetSize = Mathf.Max(_defaultInitialSize, pool.TargetSize - 1);
            }
        }

        public sealed class PooledSlot
        {
            public SFX SfxType;
            public GameObject GameObject;
            public AudioSource AudioSource;
            public SpriteRenderer SpriteRenderer;
            public float PlayStartTime;
            public bool IsInPool;
            public bool IsManagedExternally;
        }

        private sealed class SubPool
        {
            public SFX SfxType;
            public string FileName;

            public readonly Queue<PooledSlot> Available = new();
            public readonly List<PooledSlot> Active = new();

            public AudioClip CachedClip;
            public bool ClipLoading;
            public bool ClipReady;
            public int TargetSize;
            public float LastReleaseTime;
            public int PeakActiveCount;
        }
    }
}
