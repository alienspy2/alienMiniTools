using System.Drawing;
using System.Runtime.InteropServices;

namespace RemoteKM.Client;

internal sealed class HookService : IDisposable
{
    private readonly TcpSender _sender;
    private HotKeyConfig _hotKey;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardProc;
    private readonly NativeMethods.LowLevelMouseProc _mouseProc;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private bool _active;
    private bool _centerCursor;
    private Point _centerPoint;

    private int _debug_accumulate_x = 0;
    private const int CenterClipRadius = 12;

    internal event Action? CaptureStopRequested;

    internal HookService(TcpSender sender, HotKeyConfig hotKey)
    {
        _sender = sender;
        _hotKey = hotKey;
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
    }

    internal void UpdateHotKey(HotKeyConfig hotKey)
    {
        _hotKey = hotKey;
    }

    internal void SetActive(bool active)
    {
        if (_active == active)
        {
            return;
        }

        _active = active;

        if (_active)
        {
            _centerCursor = true;
            UpdateCenterPoint();
            _debug_accumulate_x = 0;
            NativeMethods.SetCursorPos(_centerPoint.X, _centerPoint.Y);
            ApplyCenterClip();
            InstallHooks();
        }
        else
        {
            _centerCursor = false;
            NativeMethods.ClipCursor(IntPtr.Zero);
            UninstallHooks();
        }
    }

    private void InstallHooks()
    {
        if (_keyboardHook != IntPtr.Zero || _mouseHook != IntPtr.Zero)
        {
            return;
        }

        var moduleHandle = NativeMethods.GetModuleHandle(null);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
    }

    private void UninstallHooks()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_active)
        {
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

        if (Program.EmergencyStopEnabled && IsScrollLockStop(data, wParam))
        {
            SetActive(false);
            CaptureStopRequested?.Invoke();
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        if (IsHotKeyEvent(data))
        {
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        var sent = _sender.SendKeyboard(new KeyboardEvent(wParam.ToInt32(), data.vkCode, data.scanCode, data.flags));
        return sent ? new IntPtr(1) : NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static bool IsScrollLockStop(NativeMethods.KBDLLHOOKSTRUCT data, IntPtr wParam)
    {
        if (data.vkCode != NativeMethods.VK_SCROLL)
        {
            return false;
        }

        var msg = wParam.ToInt32();
        return msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !_active)
        {
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        var message = wParam.ToInt32();

        if (Program.VerboseFlag)
        {
            //Console.WriteLine($"MouseCallback: msg={message} center={_centerCursor}");
        }

        if (message == NativeMethods.WM_MOUSEMOVE && _centerCursor)
        {
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        var sentDefault = _sender.SendMouse(new MouseEvent(message, 0, 0, data.mouseData, data.flags));
        if (IsBlockedCaptureMessage(message))
        {
            return sentDefault ? new IntPtr(1) : NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    internal void HandleRawMouseDelta(int deltaX, int deltaY)
    {
        if (!_active || !_centerCursor)
        {
            return;
        }

        if (deltaX == 0 && deltaY == 0)
        {
            return;
        }

        _debug_accumulate_x += deltaX;

        if (Program.VerboseFlag)
        {
            Console.WriteLine($"Send mouse: deltaX={deltaX} deltaY={deltaY}");
        }

        _sender.SendMouse(new MouseEvent(NativeMethods.WM_MOUSEMOVE, deltaX, deltaY, 0, 0));
    }

    private static bool IsBlockedCaptureMessage(int message)
    {
        return message == NativeMethods.WM_LBUTTONDOWN
            || message == NativeMethods.WM_LBUTTONUP
            || message == NativeMethods.WM_RBUTTONDOWN
            || message == NativeMethods.WM_RBUTTONUP
            || message == NativeMethods.WM_MBUTTONDOWN
            || message == NativeMethods.WM_MBUTTONUP
            || message == NativeMethods.WM_XBUTTONDOWN
            || message == NativeMethods.WM_XBUTTONUP
            || message == NativeMethods.WM_MOUSEWHEEL
            || message == NativeMethods.WM_MOUSEHWHEEL;
    }

    private bool IsHotKeyEvent(NativeMethods.KBDLLHOOKSTRUCT data)
    {
        if (data.vkCode != _hotKey.VirtualKey)
        {
            return false;
        }

        if ((_hotKey.Modifiers & (int)HotKeyModifiers.Control) != 0 && !IsKeyDown(0x11))
        {
            return false;
        }

        if ((_hotKey.Modifiers & (int)HotKeyModifiers.Alt) != 0 && !IsKeyDown(0x12))
        {
            return false;
        }

        if ((_hotKey.Modifiers & (int)HotKeyModifiers.Shift) != 0 && !IsKeyDown(0x10))
        {
            return false;
        }

        if ((_hotKey.Modifiers & (int)HotKeyModifiers.Win) != 0 && !IsKeyDown(0x5B) && !IsKeyDown(0x5C))
        {
            return false;
        }

        return true;
    }

    private static bool IsKeyDown(int vKey) => (NativeMethods.GetAsyncKeyState(vKey) & 0x8000) != 0;

    public void Dispose()
    {
        UninstallHooks();
    }

    private void UpdateCenterPoint()
    {
        var left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        _centerPoint = new Point(left + (width / 2), top + (height / 2));
    }

    private void ApplyCenterClip()
    {
        var rect = new NativeMethods.RECT
        {
            left = _centerPoint.X - CenterClipRadius,
            top = _centerPoint.Y - CenterClipRadius,
            right = _centerPoint.X + CenterClipRadius + 1,
            bottom = _centerPoint.Y + CenterClipRadius + 1
        };

        NativeMethods.ClipCursor(ref rect);
    }
}
