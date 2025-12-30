using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteKM.Client;

internal sealed record ClientSettings(string Host, int Port, string HotKey, CaptureEdge CaptureEdge)
{
    private static readonly JsonSerializerOptions LoadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static ClientSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return Defaults;
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<ClientSettings>(json, LoadOptions);
            return settings ?? Defaults;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Settings load failed: {ex.Message}");
            return Defaults;
        }
    }

    internal static ClientSettings Defaults => new("127.0.0.1", 5000, "Alt+Oem3", CaptureEdge.None);
}
