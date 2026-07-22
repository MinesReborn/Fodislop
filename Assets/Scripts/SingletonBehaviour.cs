using UnityEngine;

namespace Fodinae.Scripts
{
    /// <summary>
    /// Generic base class for MonoBehaviour singletons.
    /// Provides lazy initialization, DontDestroyOnLoad behavior, and quit-safe access.
    /// </summary>
    /// <typeparam name="T">The concrete singleton type.</typeparam>
        public abstract class SingletonBehaviour<T> : MonoBehaviour
            where T : SingletonBehaviour<T>
    {
        protected static T _instance;
        protected static bool _isQuitting;

        /// <summary>
        /// Returns the singleton instance if it exists, without creating one.
        /// Returns null if the instance has not been created or the application is quitting.
        /// </summary>
        public static T InstanceIfExists => _instance;

        /// <summary>
        /// Returns the singleton instance, creating it if necessary.
        /// Returns null if the application is quitting.
        /// </summary>
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
                    _instance = FindFirstObjectByType<T>();
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

        /// <summary>
        /// Singleton initialization. Destroys duplicate instances and sets up DontDestroyOnLoad.
        /// Override to add custom initialization, but call base.Awake() first.
        /// </summary>
        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = (T)this;
            _isQuitting = false;

            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        /// <summary>
        /// Cleans up the singleton instance reference.
        /// Override to add custom cleanup, but call base.OnDestroy().
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _isQuitting = true;
                _instance = null;
            }
        }

        /// <summary>
        /// Marks the application as quitting so Instance returns null.
        /// Override to add custom quit logic, but call base.OnApplicationQuit().
        /// </summary>
        protected virtual void OnApplicationQuit()
        {
            _isQuitting = true;
        }
    }
}
