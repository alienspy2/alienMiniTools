using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RemoteKM.Client;

internal sealed class ClientAppContext : ApplicationContext
{
    private readonly string _settingsPath;
    private readonly NotifyIcon _tray;
    private readonly Icon? _trayIcon;
    private readonly Dictionary<EndpointKey, TcpSender> _senders;
    private readonly HookService _hooks;
    private readonly ClipboardSyncService _clipboardSync;
    private readonly HotKeyWindow _hotKeyWindow;
    private readonly System.Windows.Forms.Timer _edgeMonitor;
    private readonly SynchronizationContext _syncContext;
    private readonly TransferProgressPopup _transferPopup;
    private readonly PasteMonitor _pasteMonitor;
    private bool _active;
    private ClientSettings _settings;
    private readonly Dictionary<CaptureEdge, bool> _edgeArmed = new();
    private ServerEndpoint? _currentServer;
    private ServerEndpoint? _activeServer;
    private bool _hasConnected;

    private const int EdgeOffset = 16;

    private sealed record EndpointKey(string Host, int Port);

    private sealed class EndpointKeyComparer : IEqualityComparer<EndpointKey>
    {
        public bool Equals(EndpointKey? x, EndpointKey? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return x.Port == y.Port && string.Equals(x.Host, y.Host, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(EndpointKey obj)
        {
            return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Host), obj.Port);
        }
    }

    internal ClientAppContext()
    {
        _settingsPath = Path.Combine(GetAppDirectory(), "settings.json");
        _settings = ClientSettings.Load(_settingsPath);
        Console.WriteLine($"Client base dir: {AppContext.BaseDirectory}");
        Console.WriteLine($"Client settings path: {_settingsPath}");
        Console.WriteLine($"Client settings servers: {_settings.Servers.Count}");

        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _senders = new Dictionary<EndpointKey, TcpSender>(new EndpointKeyComparer());
        _currentServer = SelectInitialServer(_settings);
        UpdateConnections(_settings);
        _hooks = new HookService(GetActiveSender, BuildHotKeyConfigs(_settings));
        _clipboardSync = new ClipboardSyncService(
            text => Task.Run(() => SendClipboardText(text)),
            paths => Task.Run(() => SendClipboardFileList(paths)),
            () =>
            {
                ClearFileLists();
            });

        _hooks.CaptureStopRequested += () => _syncContext.Post(_ => StopCapture(sendStop: true), null);
        _hotKeyWindow = new HotKeyWindow(
            BuildHotKeyBindings(_settings),
            StartCaptureFromHotKey,
            StopCaptureFromHotKey,
            () => _active,
            _hooks.HandleRawMouseDelta);
        _hotKeyWindow.ShowInTaskbar = false;
        _hotKeyWindow.Load += (_, _) => _hotKeyWindow.Hide();
        _hotKeyWindow.Show();
        _hotKeyWindow.Hide();

        _trayIcon = LoadTrayIcon();
        _tray = new NotifyIcon
        {
            Icon = _trayIcon ?? SystemIcons.Application,
            Text = "RemoteKM Client",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _transferPopup = new TransferProgressPopup("RemoteKM Client");
        _pasteMonitor = new PasteMonitor(TryStartFileTransfer);

        _edgeMonitor = new System.Windows.Forms.Timer { Interval = 30 };
        _edgeMonitor.Tick += (_, _) => CheckEdgeTrigger();
        _edgeMonitor.Start();

        ResetEdgeArming();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var settingsItem = new ToolStripMenuItem("Settings");
        var toggleItem = new ToolStripMenuItem("Toggle Capture");
        var exitItem = new ToolStripMenuItem("Exit");

        settingsItem.Click += (_, _) => OpenSettings();
        toggleItem.Click += (_, _) => Toggle();
        exitItem.Click += (_, _) => ExitThread();

        menu.Items.Add(settingsItem);
        menu.Items.Add(toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settingsPath, _settings, ApplySettings);
        form.ShowDialog();
    }

    private void ApplySettings(ClientSettings settings)
    {
        if (_active)
        {
            StopCapture(sendStop: true);
        }

        _settings = settings;
        _hasConnected = false;
        ResetEdgeArming();
        UpdateConnections(settings);

        var nextServer = SelectMatchingServer(_currentServer, settings) ?? SelectInitialServer(settings);
        if (nextServer != null)
        {
            SetCurrentServer(nextServer);
        }
        else
        {
            _currentServer = null;
        }

        _hooks.UpdateHotKeys(BuildHotKeyConfigs(settings));
        _hotKeyWindow.UpdateHotKeys(BuildHotKeyBindings(settings));
    }

    private void UpdateConnections(ClientSettings settings)
    {
        var desired = new HashSet<EndpointKey>(new EndpointKeyComparer());
        foreach (var server in settings.Servers)
        {
            desired.Add(ToKey(server));
        }

        var existing = new List<EndpointKey>(_senders.Keys);
        foreach (var key in existing)
        {
            if (!desired.Contains(key))
            {
                _senders[key].Dispose();
                _senders.Remove(key);
            }
        }

        foreach (var key in desired)
        {
            if (!_senders.ContainsKey(key))
            {
                var sender = new TcpSender(key.Host, key.Port);
                RegisterSenderEvents(sender, key);
                _senders.Add(key, sender);
            }
        }

        WarmConnectAll();
    }

    private void RegisterSenderEvents(TcpSender sender, EndpointKey key)
    {
        sender.ControlReceived += (message, value) => _syncContext.Post(_ => OnControlReceived(key, message, value), null);
        sender.ClipboardReceived += text => _syncContext.Post(_ => OnClipboardReceived(key, text), null);
        sender.FileTransferProgress += progress => _syncContext.Post(_ => OnFileTransferProgress(key, progress), null);
        sender.FileTransferReceived += tempRoot => _syncContext.Post(_ => OnFileTransferReceived(key, tempRoot), null);
        sender.ConnectionEstablished += () => _syncContext.Post(_ => OnConnectionEstablished(key), null);
        sender.ConnectionLost += () => _syncContext.Post(_ => OnConnectionLost(key), null);
    }

    private void WarmConnectAll()
    {
        foreach (var sender in _senders.Values)
        {
            Task.Run(() => sender.TryConnect());
        }
    }

    private TcpSender? GetActiveSender() => GetSender(_activeServer);

    private TcpSender? GetSender(ServerEndpoint? server)
    {
        if (server == null)
        {
            return null;
        }

        _senders.TryGetValue(ToKey(server), out var sender);
        return sender;
    }

    private TcpSender GetOrCreateSender(ServerEndpoint server)
    {
        var key = ToKey(server);
        if (!_senders.TryGetValue(key, out var sender))
        {
            sender = new TcpSender(server.Host, server.Port);
            RegisterSenderEvents(sender, key);
            _senders.Add(key, sender);
        }

        return sender;
    }

    private void ClearFileLists()
    {
        foreach (var sender in _senders.Values)
        {
            sender.ClearRemoteFileList();
            sender.ClearLocalFileList();
        }
    }

    private static EndpointKey ToKey(ServerEndpoint server) => new(server.Host, server.Port);

    private bool IsCurrentServer(EndpointKey key) => MatchesEndpoint(_currentServer, key);

    private bool IsActiveServer(EndpointKey key) => MatchesEndpoint(_activeServer, key);

    private static bool MatchesEndpoint(ServerEndpoint? server, EndpointKey key)
    {
        return server != null
            && string.Equals(server.Host, key.Host, StringComparison.OrdinalIgnoreCase)
            && server.Port == key.Port;
    }

    private void Toggle()
    {
        if (_active)
        {
            StopCapture(sendStop: true);
        }
        else
        {
            var server = _currentServer ?? SelectInitialServer(_settings);
            if (server == null)
            {
                MessageBox.Show("No servers configured. Add one in Settings.", "RemoteKM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            StartCapture(server);
        }
    }

    private void StartCaptureFromHotKey(ServerEndpoint server)
    {
        if (_active)
        {
            return;
        }

        StartCapture(server);
    }

    private void StopCaptureFromHotKey()
    {
        if (_active)
        {
            StopCapture(sendStop: true);
        }
    }

    protected override void ExitThreadCore()
    {
        _edgeMonitor.Stop();
        _edgeMonitor.Dispose();
        if (_active)
        {
            StopCapture(sendStop: true);
        }
        else
        {
            _hooks.SetActive(false);
        }
        _tray.Visible = false;
        _tray.Dispose();
        _trayIcon?.Dispose();
        _hotKeyWindow.Close();
        _hotKeyWindow.Dispose();
        _pasteMonitor.Dispose();
        _transferPopup.Dispose();
        _clipboardSync.Dispose();
        _hooks.Dispose();
        foreach (var sender in _senders.Values)
        {
            sender.Dispose();
        }
        _senders.Clear();
        base.ExitThreadCore();
    }

    private void StartCapture(ServerEndpoint server)
    {
        if (_active)
        {
            return;
        }

        var sender = GetOrCreateSender(server);
        SetCurrentServer(server);
        if (!sender.SendCaptureStart(server.CaptureEdge))
        {
            return;
        }

        _active = true;
        _activeServer = server;
        _hooks.SetActive(true);
        UpdateTrayText();
    }

    private void StopCapture(bool sendStop)
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        _hooks.SetActive(false);
        if (sendStop)
        {
            var sender = GetSender(_activeServer);
            sender?.SendCaptureStop();
        }
        MoveCursorInsideEdge(_activeServer?.CaptureEdge ?? CaptureEdge.None, EdgeOffset);
        _activeServer = null;
        UpdateTrayText();
        ResetEdgeArming();
    }

    private void OnConnectionEstablished(EndpointKey key)
    {
        if (!IsCurrentServer(key))
        {
            return;
        }

        _hasConnected = true;
        ShowConnectionStatus(connected: true);
    }

    private void OnConnectionLost(EndpointKey key)
    {
        if (!IsCurrentServer(key))
        {
            return;
        }

        StopCapture(sendStop: false);
        if (!_hasConnected)
        {
            return;
        }
        ShowConnectionStatus(connected: false);
    }

    private void OnControlReceived(EndpointKey key, ControlMessage message, int value)
    {
        if (message != ControlMessage.CaptureStop)
        {
            return;
        }

        if (!IsActiveServer(key))
        {
            return;
        }

        StopCapture(sendStop: false);
    }

    private void OnClipboardReceived(EndpointKey key, string text)
    {
        if (!IsCurrentServer(key))
        {
            return;
        }

        _clipboardSync.ApplyRemoteText(text);
    }

    private void OnFileTransferProgress(EndpointKey key, FileTransferProgress progress)
    {
        if (!IsCurrentServer(key))
        {
            return;
        }

        UpdateTransferPopup(progress);
    }

    private void OnFileTransferReceived(EndpointKey key, string tempRoot)
    {
        if (!IsCurrentServer(key))
        {
            return;
        }

        HandleFileTransferReceived(tempRoot);
    }

    private void CheckEdgeTrigger()
    {
        if (_active)
        {
            return;
        }

        if (_settings.Servers.Count == 0)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out var point))
        {
            return;
        }

        var bounds = GetVirtualScreenBounds();
        foreach (var server in _settings.Servers)
        {
            var edge = server.CaptureEdge;
            if (edge == CaptureEdge.None)
            {
                continue;
            }

            var atEdge = IsAtEdge(point, bounds, edge);
            if (atEdge)
            {
                if (IsEdgeArmed(edge))
                {
                    SetEdgeArmed(edge, false);
                    StartCapture(server);
                    return;
                }
            }
            else
            {
                SetEdgeArmed(edge, true);
            }
        }
    }

    private static bool IsAtEdge(NativeMethods.POINT point, (int Left, int Top, int Right, int Bottom) bounds, CaptureEdge edge)
    {
        return edge switch
        {
            CaptureEdge.Left => point.x <= bounds.Left,
            CaptureEdge.Right => point.x >= bounds.Right,
            CaptureEdge.Top => point.y <= bounds.Top,
            CaptureEdge.Bottom => point.y >= bounds.Bottom,
            _ => false
        };
    }

    private static (int Left, int Top, int Right, int Bottom) GetVirtualScreenBounds()
    {
        var left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        var right = left + Math.Max(1, width) - 1;
        var bottom = top + Math.Max(1, height) - 1;
        return (left, top, right, bottom);
    }

    private static void MoveCursorInsideEdge(CaptureEdge edge, int offset)
    {
        if (edge == CaptureEdge.None)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out var point))
        {
            return;
        }

        var bounds = GetVirtualScreenBounds();
        var x = point.x;
        var y = point.y;
        switch (edge)
        {
            case CaptureEdge.Left:
                x = bounds.Left + offset;
                break;
            case CaptureEdge.Right:
                x = bounds.Right - offset;
                break;
            case CaptureEdge.Top:
                y = bounds.Top + offset;
                break;
            case CaptureEdge.Bottom:
                y = bounds.Bottom - offset;
                break;
        }

        x = Math.Clamp(x, bounds.Left, bounds.Right);
        y = Math.Clamp(y, bounds.Top, bounds.Bottom);
        NativeMethods.SetCursorPos(x, y);
    }

    private void UpdateTrayText()
    {
        _tray.Text = _active ? "RemoteKM Client (Capture ON)" : "RemoteKM Client (Capture OFF)";
    }

    private void ShowConnectionStatus(bool connected)
    {
        if (_currentServer == null)
        {
            return;
        }

        var target = $"{_currentServer.Host}:{_currentServer.Port}";
        var icon = connected ? ToolTipIcon.Info : ToolTipIcon.Warning;
        var message = connected ? $"Server connected: {target}" : $"Server disconnected: {target}";
        _tray.ShowBalloonTip(3000, "RemoteKM Client", message, icon);
    }

    private void UpdateTransferPopup(FileTransferProgress progress)
    {
        _transferPopup.UpdateProgress(
            progress.CurrentFileIndex,
            progress.TotalFiles,
            progress.CurrentFileBytes,
            progress.TotalFileBytes,
            progress.Completed);
    }

    private void HandleFileTransferReceived(string tempRoot)
    {
        var entries = Directory.Exists(tempRoot)
            ? Directory.EnumerateFileSystemEntries(tempRoot).ToArray()
            : Array.Empty<string>();

        if (entries.Length == 0)
        {
            return;
        }

        if (!_clipboardSync.ApplyRemoteFileList(entries))
        {
            return;
        }

        _pasteMonitor.SuppressNextPaste();
        SendCtrlV();
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            CreateKeyInput(NativeMethods.VK_CONTROL, keyUp: false),
            CreateKeyInput(NativeMethods.VK_V, keyUp: false),
            CreateKeyInput(NativeMethods.VK_V, keyUp: true),
            CreateKeyInput(NativeMethods.VK_CONTROL, keyUp: true)
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT CreateKeyInput(ushort vk, bool keyUp)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = keyUp ? (uint)NativeMethods.KEYEVENTF_KEYUP : 0u,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    private bool TryStartFileTransfer(string? destinationPath)
    {
        var sender = GetSender(_currentServer);
        if (sender == null)
        {
            return false;
        }

        return sender.TryRequestFileTransfer(destinationPath);
    }

    private void SendClipboardText(string text)
    {
        var sender = GetSender(_currentServer);
        sender?.SendClipboardText(text);
    }

    private void SendClipboardFileList(IReadOnlyList<string> paths)
    {
        var sender = GetSender(_currentServer);
        sender?.SendClipboardFileList(paths);
    }

    private static ServerEndpoint? SelectInitialServer(ClientSettings settings)
    {
        return settings.Servers.Count > 0 ? settings.Servers[0] : null;
    }

    private static ServerEndpoint? SelectMatchingServer(ServerEndpoint? current, ClientSettings settings)
    {
        if (current == null)
        {
            return null;
        }

        foreach (var server in settings.Servers)
        {
            if (string.Equals(server.Host, current.Host, StringComparison.OrdinalIgnoreCase) && server.Port == current.Port)
            {
                return server;
            }
        }

        return null;
    }

    private void SetCurrentServer(ServerEndpoint server)
    {
        var endpointChanged = _currentServer == null
            || !string.Equals(_currentServer.Host, server.Host, StringComparison.OrdinalIgnoreCase)
            || _currentServer.Port != server.Port;

        _currentServer = server;
        if (endpointChanged)
        {
            var sender = GetSender(server);
            _hasConnected = sender?.IsConnected == true;
        }
    }

    private void ResetEdgeArming()
    {
        _edgeArmed.Clear();
        foreach (var server in _settings.Servers)
        {
            if (server.CaptureEdge != CaptureEdge.None)
            {
                _edgeArmed[server.CaptureEdge] = true;
            }
        }
    }

    private bool IsEdgeArmed(CaptureEdge edge)
    {
        if (_edgeArmed.TryGetValue(edge, out var armed))
        {
            return armed;
        }

        _edgeArmed[edge] = true;
        return true;
    }

    private void SetEdgeArmed(CaptureEdge edge, bool armed)
    {
        _edgeArmed[edge] = armed;
    }

    private static Icon? LoadTrayIcon()
    {
        using var stream = GetTrayIconStream();
        if (stream == null)
        {
            return null;
        }

        using var bitmap = new Bitmap(stream);
        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private static Stream? GetTrayIconStream()
    {
        var stream = typeof(ClientAppContext).Assembly.GetManifestResourceStream("portal.png");
        if (stream != null)
        {
            return stream;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "portal.png");
        return File.Exists(iconPath) ? File.OpenRead(iconPath) : null;
    }

    private static string GetAppDirectory()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var dir = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                return dir;
            }
        }

        return AppContext.BaseDirectory;
    }


    private static HotKeyConfig ParseHotKey(string text)
    {
        var modifiers = 0;
        var keyPart = string.Empty;

        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals("CTRL", StringComparison.OrdinalIgnoreCase) || part.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= (int)HotKeyModifiers.Control;
            }
            else if (part.Equals("ALT", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= (int)HotKeyModifiers.Alt;
            }
            else if (part.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= (int)HotKeyModifiers.Shift;
            }
            else if (part.Equals("WIN", StringComparison.OrdinalIgnoreCase) || part.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= (int)HotKeyModifiers.Win;
            }
            else
            {
                keyPart = part;
            }
        }

        if (!Enum.TryParse<Keys>(keyPart, true, out var key))
        {
            key = Keys.Oem3;
        }

        return new HotKeyConfig(modifiers, (int)key);
    }

    private static IReadOnlyList<HotKeyConfig> BuildHotKeyConfigs(ClientSettings settings)
    {
        var list = new List<HotKeyConfig>();
        foreach (var server in settings.Servers)
        {
            if (string.IsNullOrWhiteSpace(server.HotKey))
            {
                continue;
            }

            list.Add(ParseHotKey(server.HotKey));
        }

        return list;
    }

    private static IReadOnlyList<HotKeyBinding> BuildHotKeyBindings(ClientSettings settings)
    {
        var list = new List<HotKeyBinding>();
        foreach (var server in settings.Servers)
        {
            if (string.IsNullOrWhiteSpace(server.HotKey))
            {
                continue;
            }

            list.Add(new HotKeyBinding(ParseHotKey(server.HotKey), server));
        }

        return list;
    }
}
