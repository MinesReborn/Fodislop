using System;
using System.Collections.Generic;

namespace Fodinae.Scripts.Core
{
    /// <summary>
    /// Lightweight service locator for dependency resolution.
    /// Services are registered once at startup by CompositionRoot and resolved
    /// through interfaces rather than concrete singletons.
    ///
    /// Usage:
    ///   ServiceLocator.Register<IMapDataProvider>(MapManager.Instance);
    ///   var map = ServiceLocator.Resolve<IMapDataProvider>();
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> _services = new();

        public static void Register<T>(T service)
            where T : class
        {
            var type = typeof(T);
            if (_services.ContainsKey(type))
            {
                _services[type] = service;
                return;
            }

            _services[type] = service;
        }

        public static T Resolve<T>()
            where T : class
        {
            if (_services.TryGetValue(typeof(T), out var service))
            {
                return service as T;
            }

            return null;
        }

        public static void Clear()
        {
            _services.Clear();
        }
    }
}
