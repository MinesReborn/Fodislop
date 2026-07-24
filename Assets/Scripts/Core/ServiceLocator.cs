using VContainer;

namespace Fodinae.Scripts.Core
{
    public static class ServiceLocator
    {
        private static IObjectResolver _resolver;

        public static void Initialize(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        public static T Resolve<T>()
            where T : class
        {
            try
            {
                return _resolver != null ? _resolver.Resolve<T>() : null;
            }
            catch (VContainerException)
            {
                return null;
            }
        }
    }
}
