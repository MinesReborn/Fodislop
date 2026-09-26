#nullable enable

using System;
using VContainer;

namespace Kern.UI;

public static class MapServiceRegistration
{
    public static void Register(IContainerBuilder builder)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Register<MapCellSampler>(Lifetime.Singleton);
    }
}
