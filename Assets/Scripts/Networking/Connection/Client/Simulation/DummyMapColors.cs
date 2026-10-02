#nullable enable

using Kern.World;
using MinesServer.Data;

namespace MinesServer.Networking.Connection.Client;

internal static class DummyMapColors
{
    public static int Get(int cellId)
    {
        if (cellId is < 0 or > byte.MaxValue)
        {
            return unchecked((int)0xFF808080);
        }

        return MapBlockColors.GetPackedColor((CellType)cellId);
    }
}
