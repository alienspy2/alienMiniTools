using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RemoteKM.Client;

internal sealed class HotKeyWindow : Form
{
    private readonly Action _toggleAction;
    private readonly Func<bool> _isCaptureActive;
    private readonly Action<int, int> _rawMouseAction;
    private readonly int _hotKeyId = 1;
    private HotKeyConfig _hotKey;
    private readonly System.Windows.Forms.Timer _releaseTimer;
    private bool _pendingToggle;

    internal HotKeyWindow(HotKeyConfig hotKey, Action toggleAction, Func<bool> isCaptureActive, Action<int, int> rawMouseAction)
    {
        _hotKey = hotKey;
        _toggleAction = toggleAction;
        _isCaptureActive = isCaptureActive;
        _rawMouseAction = rawMouseAction;
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        _releaseTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _releaseTimer.Tick += (_, _) => CheckRelease();
        Load += (_, _) => Register();
        FormClosing += (_, _) => Unregister();
    }

    internal void UpdateHotKey(HotKeyConfig hotKey)
    {
        _hotKey = hotKey;
        Unregister();
        Register();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            if (_isCaptureActive())
            {
                CancelPending();
                _toggleAction();
            }
            else if (!_pendingToggle)
            {
                _pendingToggle = true;
                _releaseTimer.Start();
            }
        }
        else if (m.Msg == NativeMethods.WM_INPUT && _isCaptureActive())
        {
            if (TryGetRawMouseDelta(m.LParam, out var deltaX, out var deltaY))
            {
                _rawMouseAction(deltaX, deltaY);
            }
        }

        base.WndProc(ref m);
    }

    private void Register()
    {
        if (!NativeMethods.RegisterHotKey(Handle, _hotKeyId, _hotKey.Modifiers, _hotKey.VirtualKey))
        {
            Console.WriteLine("Failed to register hotkey.");
        }

        RegisterRawInput();
    }

    private void Unregister()
    {
        NativeMethods.UnregisterHotKey(Handle, _hotKeyId);
    }

    private void RegisterRawInput()
    {
        var devices = new[]
        {
            new NativeMethods.RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x02,
                dwFlags = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget = Handle
            }
        };

        if (!NativeMethods.RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>()))
        {
            Console.WriteLine("Failed to register raw input.");
        }
    }

    private static bool TryGetRawMouseDelta(IntPtr lParam, out int deltaX, out int deltaY)
    {
        deltaX = 0;
        deltaY = 0;

        var headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
        uint size = 0;
        if (NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, buffer, ref size, headerSize) != size)
            {
                return false;
            }

            var header = Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(buffer);
            if (header.dwType != NativeMethods.RIM_TYPEMOUSE)
            {
                return false;
            }

            var mouse = Marshal.PtrToStructure<NativeMethods.RAWMOUSE>(IntPtr.Add(buffer, Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>()));
            deltaX = mouse.lLastX;
            deltaY = mouse.lLastY;
            return deltaX != 0 || deltaY != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void CheckRelease()
    {
        if (IsHotKeyDown())
        {
            return;
        }

        CancelPending();
        _toggleAction();
    }

    private void CancelPending()
    {
        if (_pendingToggle)
        {
            _pendingToggle = false;
            _releaseTimer.Stop();
        }
    }

    private bool IsHotKeyDown()
    {
        if (IsModifierRequired(HotKeyModifiers.Control) && !IsKeyDown(0x11))
        {
            return false;
        }

        if (IsModifierRequired(HotKeyModifiers.Alt) && !IsKeyDown(0x12))
        {
            return false;
        }

        if (IsModifierRequired(HotKeyModifiers.Shift) && !IsKeyDown(0x10))
        {
            return false;
        }

        if (IsModifierRequired(HotKeyModifiers.Win) && !IsKeyDown(0x5B) && !IsKeyDown(0x5C))
        {
            return false;
        }

        return IsKeyDown(_hotKey.VirtualKey);
    }

    private bool IsModifierRequired(HotKeyModifiers modifier) => (_hotKey.Modifiers & (int)modifier) != 0;

    private static bool IsKeyDown(int vKey) => (NativeMethods.GetAsyncKeyState(vKey) & 0x8000) != 0;
}
