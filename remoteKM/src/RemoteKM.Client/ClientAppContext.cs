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
    private readonly TcpSender _sender;
    private readonly HookService _hooks;
    private readonly ClipboardSyncService _clipboardSync;
    private readonly HotKeyWindow _hotKeyWindow;
    private readonly System.Windows.Forms.Timer _edgeMonitor;
    private readonly SynchronizationContext _syncContext;
    private readonly TransferProgressPopup _transferPopup;
    private readonly PasteMonitor _pasteMonitor;
    private readonly ClientRuntimeOptions _runtime;
    private bool _active;
    private ClientSettings _settings;
    private bool _edgeArmed = true;
    private bool _hasConnected;

    private const int EdgeOffset = 16;

    internal ClientAppContext(string settingsPath, ClientSettings settings, ClientRuntimeOptions runtime)
    {
        _settingsPath = settingsPath;
        _settings = settings;
        _runtime = runtime;

        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        var (host, port) = GetEndpoint(_settings);
        _sender = new TcpSender(host, port);
        _hooks = new HookService(_sender, ParseHotKey(_settings.HotKey));
        _clipboardSync = new ClipboardSyncService(
            text => Task.Run(() => _sender.SendClipboardText(text)),
            paths => Task.Run(() => _sender.SendClipboardFileList(paths)),
            () =>
            {
                _sender.ClearRemoteFileList();
                _sender.ClearLocalFileList();
            });

        _sender.ControlReceived += OnControlReceived;
        _sender.ClipboardReceived += text => _syncContext.Post(_ => _clipboardSync.ApplyRemoteText(text), null);
        _sender.FileTransferProgress += progress => _syncContext.Post(_ => UpdateTransferPopup(progress), null);
        _sender.FileTransferReceived += tempRoot => _syncContext.Post(_ => OnFileTransferReceived(tempRoot), null);
        _sender.ConnectionEstablished += () => _syncContext.Post(_ => OnConnectionEstablished(), null);
        _sender.ConnectionLost += () => _syncContext.Post(_ => OnConnectionLost(), null);
        _hooks.CaptureStopRequested += () => _syncContext.Post(_ => StopCapture(sendStop: true), null);
        _hotKeyWindow = new HotKeyWindow(ParseHotKey(_settings.HotKey), Toggle, () => _active, _hooks.HandleRawMouseDelta);
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
    }

    private (string Host, int Port) GetEndpoint(ClientSettings settings)
    {
        if (!_runtime.UseProxy)
        {
            return (settings.Host, settings.Port);
        }

        return (_runtime.ProxyHost, ProxyPortHelper.GetProxyPort(settings.Port));
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
        _settings = settings;
        var (host, port) = GetEndpoint(settings);
        _sender.UpdateEndpoint(host, port);
        _edgeArmed = true;
        _hasConnected = false;

        var hotKey = ParseHotKey(settings.HotKey);
        _hooks.UpdateHotKey(hotKey);
        _hotKeyWindow.UpdateHotKey(hotKey);
    }

    private void Toggle()
    {
        if (_active)
        {
            StopCapture(sendStop: true);
        }
        else
        {
            StartCapture();
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
        _sender.Dispose();
        base.ExitThreadCore();
    }

    private void StartCapture()
    {
        if (_active)
        {
            return;
        }

        if (!_sender.SendCaptureStart(_settings.CaptureEdge))
        {
            return;
        }

        _active = true;
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
            _sender.SendCaptureStop();
        }
        MoveCursorInsideEdge(_settings.CaptureEdge, EdgeOffset);
        UpdateTrayText();
    }

    private void OnConnectionEstablished()
    {
        _hasConnected = true;
        ShowConnectionStatus(connected: true);
    }

    private void OnConnectionLost()
    {
        StopCapture(sendStop: false);
        if (!_hasConnected)
        {
            return;
        }
        ShowConnectionStatus(connected: false);
    }

    private void OnControlReceived(ControlMessage message, int value)
    {
        if (message != ControlMessage.CaptureStop)
        {
            return;
        }

        _syncContext.Post(_ => StopCapture(sendStop: false), null);
    }

    private void CheckEdgeTrigger()
    {
        if (_active || _settings.CaptureEdge == CaptureEdge.None)
        {
            _edgeArmed = true;
            return;
        }

        if (!NativeMethods.GetCursorPos(out var point))
        {
            return;
        }

        var bounds = GetVirtualScreenBounds();
        var atEdge = IsAtEdge(point, bounds, _settings.CaptureEdge);
        if (atEdge)
        {
            if (_edgeArmed)
            {
                _edgeArmed = false;
                StartCapture();
            }
        }
        else
        {
            _edgeArmed = true;
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
        var target = $"{_settings.Host}:{_settings.Port}";
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

    private void OnFileTransferReceived(string tempRoot)
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
        return _sender.TryRequestFileTransfer(destinationPath);
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
}
