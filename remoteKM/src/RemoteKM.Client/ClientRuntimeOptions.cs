namespace RemoteKM.Client;

internal sealed record ClientRuntimeOptions(bool UseProxy, string ProxyHost)
{
    internal static ClientRuntimeOptions Direct { get; } = new(false, string.Empty);
}
