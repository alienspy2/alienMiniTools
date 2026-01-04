using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteKM.Client;

internal sealed record ServerEndpoint(string Host, int Port, CaptureEdge CaptureEdge, string HotKey);

internal sealed record ClientSettings(IReadOnlyList<ServerEndpoint> Servers)
{
    private static readonly JsonSerializerOptions LoadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static ClientSettings Load(string path)
    {
        Console.WriteLine($"Settings load: path={path} exists={File.Exists(path)}");
        if (!File.Exists(path))
        {
            Console.WriteLine("Settings load: file missing; using defaults.");
            return Defaults;
        }

        try
        {
            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("servers", out _) || document.RootElement.TryGetProperty("Servers", out _))
                {
                    var settings = JsonSerializer.Deserialize<ClientSettings>(json, LoadOptions);
                    var normalized = Normalize(settings);
                    Console.WriteLine($"Settings load: servers={normalized.Servers.Count}");
                    return normalized;
                }

                var legacy = JsonSerializer.Deserialize<LegacyClientSettings>(json, LoadOptions);
                if (legacy != null)
                {
                    var server = new ServerEndpoint(
                        string.IsNullOrWhiteSpace(legacy.Host) ? Defaults.Servers[0].Host : legacy.Host.Trim(),
                        legacy.Port > 0 ? legacy.Port : Defaults.Servers[0].Port,
                        legacy.CaptureEdge,
                        string.IsNullOrWhiteSpace(legacy.HotKey) ? Defaults.Servers[0].HotKey : legacy.HotKey.Trim());
                    Console.WriteLine("Settings load: legacy format detected.");
                    return new ClientSettings(new[] { server });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Settings load failed: {ex.Message}");
        }

        Console.WriteLine("Settings load: invalid content; using defaults.");
        return Defaults;
    }

    private static ClientSettings Normalize(ClientSettings? settings)
    {
        if (settings == null)
        {
            return Defaults;
        }

        var list = new List<ServerEndpoint>();
        if (settings.Servers != null)
        {
            foreach (var server in settings.Servers)
            {
                var host = string.IsNullOrWhiteSpace(server.Host) ? Defaults.Servers[0].Host : server.Host.Trim();
                var port = server.Port > 0 ? server.Port : Defaults.Servers[0].Port;
                var hotKey = string.IsNullOrWhiteSpace(server.HotKey) ? Defaults.Servers[0].HotKey : server.HotKey.Trim();
                list.Add(new ServerEndpoint(host, port, server.CaptureEdge, hotKey));
            }
        }

        return new ClientSettings(list);
    }

    internal static ClientSettings Defaults => new(
        new[] { new ServerEndpoint("127.0.0.1", 5000, CaptureEdge.None, "Alt+Oem3") });

    private sealed record LegacyClientSettings(string Host, int Port, string HotKey, CaptureEdge CaptureEdge);
}
