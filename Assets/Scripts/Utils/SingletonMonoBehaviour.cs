using UnityEngine;

namespace Fodinae.Scripts.Utils
{
    public abstract class SingletonMonoBehaviour<T> : MonoBehaviour
        where T : SingletonMonoBehaviour<T>
    {
        private static T _instance;
        private static bool _isQuitting;

        public static T InstanceIfExists => _instance;

        public static T Instance
        {
            get
            {
                if (_isQuitting)
                {
                    return null;
                }

                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<T>();
                    if (_instance == null && !_isQuitting)
                    {
                        var go = new GameObject($"[{typeof(T).Name}]");
                        _instance = go.AddComponent<T>();
                        if (Application.isPlaying)
                        {
                            var parent = GameObject.Find("[Systems]") ?? new GameObject("[Systems]");
                            DontDestroyOnLoad(parent);
                            go.transform.SetParent(parent.transform);
                        }
                    }
                }

                return _instance;
            }
        }

        protected virtual void OnAwake()
        {
        }

        protected virtual void OnDestroyed()
        {
        }

        protected virtual void OnApplicationQuitting()
        {
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = (T)this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
                var parent = GameObject.Find("[Systems]") ?? new GameObject("[Systems]");
                DontDestroyOnLoad(parent);
                transform.SetParent(parent.transform);
            }

            _isQuitting = false;
            OnAwake();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _isQuitting = true;
                OnDestroyed();
            }
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
            OnApplicationQuitting();
        }
    }
}
