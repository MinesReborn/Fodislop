using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Scripts.Core
{
    public class ObjectPool<T>
        where T : Component
    {
        private readonly Queue<T> _pool = new();
        private readonly T _prefab;
        private readonly Transform _parent;

        public int CountInactive => _pool.Count;

        public ObjectPool(T prefab, Transform parent = null, int preload = 0)
        {
            _prefab = prefab;
            _parent = parent;

            for (int i = 0; i < preload; i++)
            {
                var obj = Object.Instantiate(_prefab, _parent);
                obj.gameObject.SetActive(false);
                _pool.Enqueue(obj);
            }
        }

        public T Get()
        {
            T obj;
            while (_pool.Count > 0)
            {
                obj = _pool.Dequeue();
                if (obj != null)
                {
                    obj.gameObject.SetActive(true);
                    return obj;
                }
            }

            obj = Object.Instantiate(_prefab, _parent);
            return obj;
        }

        public void Return(T obj)
        {
            if (obj == null)
            {
                return;
            }

            obj.gameObject.SetActive(false);
            _pool.Enqueue(obj);
        }

        public void Clear()
        {
            while (_pool.Count > 0)
            {
                var obj = _pool.Dequeue();
                if (obj != null)
                {
                    Object.Destroy(obj.gameObject);
                }
            }
        }
    }
}
