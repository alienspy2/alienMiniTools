using System;

namespace RemoteKM.Server;

internal static class ProxyPortHelper
{
    private const int Offset = 10000;

    internal static int GetProxyPort(int port)
    {
        if (port >= 1 && port <= 55534)
        {
            return port + Offset;
        }

        if (port - Offset >= 1)
        {
            return port - Offset;
        }

        return Math.Clamp(port, 1, 65534);
    }
}
