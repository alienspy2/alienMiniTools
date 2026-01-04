using System.Drawing;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RemoteKM.Server;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly SynchronizationContext _uiContext;
    private readonly Icon? _trayIcon;
    private readonly ClipboardSyncService _clipboardSync;
    private readonly TransferProgressPopup _transferPopup;
    private readonly PasteMonitor _pasteMonitor;
    private ServerSettings _settings;
    private TcpListenerService? _service;
    private CancellationTokenSource? _cts;
    private SettingsForm? _settingsForm;

    internal TrayApplicationContext(ServerSettings settings)
    {
        _settings = settings;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings...", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());

        _trayIcon = LoadTrayIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };
        UpdateTooltip();
        _notifyIcon.DoubleClick += (_, _) => ShowSettings();

        _clipboardSync = new ClipboardSyncService(
            text => Task.Run(() => _service?.SendClipboardText(text)),
            paths => Task.Run(() => _service?.SendClipboardFileList(paths)),
            () =>
            {
                _service?.ClearRemoteFileList();
                _service?.ClearLocalFileList();
            });
        _transferPopup = new TransferProgressPopup("RemoteKM Server");
        _pasteMonitor = new PasteMonitor(TryStartFileTransfer);
        StartServer();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    protected override void ExitThreadCore()
    {
        StopServer();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayIcon?.Dispose();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _pasteMonitor.Dispose();
        _transferPopup.Dispose();
        _clipboardSync.Dispose();
        base.ExitThreadCore();
    }

    private void StartServer()
    {
        StopServer();
        _cts = new CancellationTokenSource();
        _service = new TcpListenerService(_settings.IpAddress, _settings.Port, new InputPlayer());
        _service.ClientConnected += OnClientConnected;
        _service.ClientDisconnected += OnClientDisconnected;
        _service.ClipboardReceived += OnClipboardReceived;
        _service.FileTransferProgress += OnFileTransferProgress;
        _service.FileTransferReceived += OnFileTransferReceived;
        _ = Task.Run(() => _service.RunAsync(_cts.Token));
    }

    private void StopServer()
    {
        if (_cts == null)
        {
            return;
        }

        _cts.Cancel();
        if (_service != null)
        {
            _service.ClientConnected -= OnClientConnected;
            _service.ClientDisconnected -= OnClientDisconnected;
            _service.ClipboardReceived -= OnClipboardReceived;
            _service.FileTransferProgress -= OnFileTransferProgress;
            _service.FileTransferReceived -= OnFileTransferReceived;
        }
        _service?.Stop();
        _cts.Dispose();
        _cts = null;
    }

    private void ShowSettings()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_settings, ApplySettings);
            _settingsForm.Show();
        }
        else
        {
            _settingsForm.Activate();
        }
    }

    private void ApplySettings(ServerSettings settings)
    {
        _settings = settings;
        UpdateTooltip();
        StartServer();
    }

    private void UpdateTooltip()
    {
        var text = $"RemoteKM Server ({_settings.Host}:{_settings.Port})";
        _notifyIcon.Text = text.Length <= 63 ? text : "RemoteKM Server";
    }

    private void OnClientConnected(IPEndPoint? endpoint)
    {
        var address = endpoint == null ? "unknown" : $"{endpoint.Address}:{endpoint.Port}";
        _uiContext.Post(_ =>
        {
            _notifyIcon.ShowBalloonTip(
                3000,
                "RemoteKM Server",
                $"Client connected: {address}",
                ToolTipIcon.Info);
        }, null);
    }

    private void OnClientDisconnected(IPEndPoint? endpoint)
    {
        var address = endpoint == null ? "unknown" : $"{endpoint.Address}:{endpoint.Port}";
        _uiContext.Post(_ =>
        {
            _notifyIcon.ShowBalloonTip(
                3000,
                "RemoteKM Server",
                $"Client disconnected: {address}",
                ToolTipIcon.Warning);
        }, null);
    }

    private void OnClipboardReceived(string text)
    {
        _uiContext.Post(_ => _clipboardSync.ApplyRemoteText(text), null);
    }

    private void OnFileTransferProgress(FileTransferProgress progress)
    {
        _uiContext.Post(_ => UpdateTransferPopup(progress), null);
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
        _uiContext.Post(_ =>
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
        }, null);
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
        return _service?.TryRequestFileTransfer(destinationPath) ?? false;
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
        var stream = typeof(TrayApplicationContext).Assembly.GetManifestResourceStream("portal.png");
        if (stream != null)
        {
            return stream;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "portal.png");
        return File.Exists(iconPath) ? File.OpenRead(iconPath) : null;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
        {
            return;
        }

        _uiContext.Post(async _ =>
        {
            // Give the network stack a moment to wake up
            await Task.Delay(2000);
            StopServer();
            StartServer();
            _notifyIcon.ShowBalloonTip(
                3000,
                "RemoteKM Server",
                "Server restarted after resume.",
                ToolTipIcon.Info);
        }, null);
    }
}
