using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RemoteKM.Server;

internal sealed class ClipboardSyncService : IDisposable
{
    private readonly ClipboardWindow _window;
    private readonly Action<string> _sendText;
    private readonly Action<IReadOnlyList<string>> _sendFiles;
    private readonly Action? _onClipboardChanged;
    private string? _lastText;
    private string? _lastFileSignature;
    private bool _suppressNext;

    internal ClipboardSyncService(Action<string> sendText, Action<IReadOnlyList<string>> sendFiles, Action? onClipboardChanged = null)
    {
        _sendText = sendText;
        _sendFiles = sendFiles;
        _onClipboardChanged = onClipboardChanged;
        _window = new ClipboardWindow(OnClipboardChanged);
    }

    internal void ApplyRemoteText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (text == _lastText)
        {
            return;
        }

        if (TrySetClipboardText(text))
        {
            _lastText = text;
            _suppressNext = true;
        }
    }

    internal bool ApplyRemoteFileList(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return false;
        }

        var signature = BuildFileSignature(paths);
        if (_suppressNext && signature == _lastFileSignature)
        {
            _suppressNext = false;
            return true;
        }

        if (TrySetClipboardFileList(paths))
        {
            _lastFileSignature = signature;
            _suppressNext = true;
            return true;
        }

        return false;
    }

    private void OnClipboardChanged()
    {
        _onClipboardChanged?.Invoke();

        if (TryGetClipboardFileList(out var paths))
        {
            var signature = BuildFileSignature(paths);
            if (_suppressNext && signature == _lastFileSignature)
            {
                _suppressNext = false;
                return;
            }

            _suppressNext = false;

            _lastFileSignature = signature;
            if (Program.VerboseFlag)
            {
                Console.WriteLine($"Clipboard file list detected: count={paths.Count}");
                foreach (var path in paths)
                {
                    Console.WriteLine($"  {path}");
                }
            }
            _sendFiles(paths);
            return;
        }

        if (!TryGetClipboardText(out var text))
        {
            return;
        }

        if (_suppressNext && text == _lastText)
        {
            _suppressNext = false;
            return;
        }

        _suppressNext = false;

        if (text == _lastText)
        {
            return;
        }

        _lastText = text;
        if (Program.VerboseFlag)
        {
            Console.WriteLine($"Clipboard text detected: length={text.Length}");
        }
        _sendText(text);
    }

    private static bool TryGetClipboardText(out string text)
    {
        text = string.Empty;
        try
        {
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                return false;
            }

            text = Clipboard.GetText(TextDataFormat.UnicodeText);
            return !string.IsNullOrEmpty(text);
        }
        catch (ExternalException)
        {
            return false;
        }
        catch (ThreadStateException)
        {
            return false;
        }
    }

    private static bool TrySetClipboardText(string text)
    {
        try
        {
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
        catch (ThreadStateException)
        {
            return false;
        }
    }

    private static bool TrySetClipboardFileList(IReadOnlyList<string> paths)
    {
        try
        {
            var list = new System.Collections.Specialized.StringCollection();
            foreach (var path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    list.Add(path);
                }
            }

            if (list.Count == 0)
            {
                return false;
            }

            Clipboard.SetFileDropList(list);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
        catch (ThreadStateException)
        {
            return false;
        }
    }

    private static bool TryGetClipboardFileList(out List<string> paths)
    {
        paths = new List<string>();
        try
        {
            if (!Clipboard.ContainsFileDropList())
            {
                return false;
            }

            var list = Clipboard.GetFileDropList();
            foreach (string item in list)
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    paths.Add(item);
                }
            }

            return paths.Count > 0;
        }
        catch (ExternalException)
        {
            return false;
        }
        catch (ThreadStateException)
        {
            return false;
        }
    }

    private static string BuildFileSignature(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return string.Empty;
        }

        var ordered = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            ordered.Add(path);
        }

        ordered.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join("|", ordered);
    }

    public void Dispose()
    {
        _window.Dispose();
    }

    private sealed class ClipboardWindow : NativeWindow, IDisposable
    {
        private const int HWND_MESSAGE = -3;
        private readonly Action _onChange;

        internal ClipboardWindow(Action onChange)
        {
            _onChange = onChange;
            CreateHandle(new CreateParams
            {
                Caption = "RemoteKMClipboard",
                Parent = new IntPtr(HWND_MESSAGE)
            });
            NativeMethods.AddClipboardFormatListener(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                _onChange();
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                NativeMethods.RemoveClipboardFormatListener(Handle);
                DestroyHandle();
            }
        }
    }
}
