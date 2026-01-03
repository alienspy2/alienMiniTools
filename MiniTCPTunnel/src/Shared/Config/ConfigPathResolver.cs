using System;

namespace MiniTCPTunnel.Shared.Config;

// 간단한 커맨드라인에서 --config 값을 추출한다.
public static class ConfigPathResolver
{
    public static string ResolveConfigPath(string[] args, string defaultPath)
    {
        if (args is null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        var result = defaultPath;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-c", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    result = args[i + 1];
                }
                continue;
            }

            if (arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
            {
                result = arg.Substring("--config=".Length);
            }
        }

        return result;
    }
}
