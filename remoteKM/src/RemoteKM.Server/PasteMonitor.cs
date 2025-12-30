using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RemoteKM.Server;

internal sealed class PasteMonitor : IDisposable
{
    private readonly Func<string?, bool> _onPaste;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;
    private IntPtr _hook;
    private int _ignoreNextPaste;

    internal PasteMonitor(Func<string?, bool> onPaste)
    {
        _onPaste = onPaste;
        _keyboardProc = KeyboardCallback;
        var moduleHandle = NativeMethods.GetModuleHandle(null);
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
    }

    internal void SuppressNextPaste()
    {
        Interlocked.Exchange(ref _ignoreNextPaste, 1);
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            if (message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN)
            {
                var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if (data.vkCode == (int)Keys.V && (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0)
                {
                    if (Interlocked.Exchange(ref _ignoreNextPaste, 0) == 1)
                    {
                        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                    }

                    if (Program.VerboseFlag)
                    {
                        Console.WriteLine("Paste detected.");
                    }

                    var started = _onPaste(null);
                    if (Program.VerboseFlag)
                    {
                        Console.WriteLine(started ? "File transfer request sent." : "File transfer request rejected.");
                    }

                    if (started)
                    {
                        return new IntPtr(1);
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
