using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RemoteKM.Client;

internal sealed class HotKeyWindow : Form
{
    private readonly Action<ServerEndpoint> _startAction;
    private readonly Action _stopAction;
    private readonly Func<bool> _isCaptureActive;
    private readonly Action<int, int> _rawMouseAction;
    private IReadOnlyList<HotKeyBinding> _bindings;
    private readonly Dictionary<int, HotKeyBinding> _bindingsById = new();
    private readonly System.Windows.Forms.Timer _releaseTimer;
    private bool _pendingToggle;
    private HotKeyBinding? _pendingBinding;

    internal HotKeyWindow(
        IReadOnlyList<HotKeyBinding> bindings,
        Action<ServerEndpoint> startAction,
        Action stopAction,
        Func<bool> isCaptureActive,
        Action<int, int> rawMouseAction)
    {
        _bindings = bindings;
        _startAction = startAction;
        _stopAction = stopAction;
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

    internal void UpdateHotKeys(IReadOnlyList<HotKeyBinding> bindings)
    {
        _bindings = bindings;
        CancelPending();
        Unregister();
        Register();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            var id = (int)m.WParam;
            if (_isCaptureActive())
            {
                CancelPending();
                _stopAction();
            }
            else if (!_pendingToggle)
            {
                if (_bindingsById.TryGetValue(id, out var binding))
                {
                    _pendingToggle = true;
                    _pendingBinding = binding;
                    _releaseTimer.Start();
                }
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
        _bindingsById.Clear();
        var id = 1;
        foreach (var binding in _bindings)
        {
            if (!NativeMethods.RegisterHotKey(Handle, id, binding.HotKey.Modifiers, binding.HotKey.VirtualKey))
            {
                Console.WriteLine("Failed to register hotkey.");
            }
            _bindingsById[id] = binding;
            id++;
        }

        RegisterRawInput();
    }

    private void Unregister()
    {
        foreach (var id in _bindingsById.Keys.ToArray())
        {
            NativeMethods.UnregisterHotKey(Handle, id);
        }
        _bindingsById.Clear();
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
        var binding = _pendingBinding;
        if (binding == null)
        {
            CancelPending();
            return;
        }

        if (IsHotKeyDown(binding.HotKey))
        {
            return;
        }

        CancelPending();
        _startAction(binding.Server);
    }

    private void CancelPending()
    {
        if (_pendingToggle)
        {
            _pendingToggle = false;
            _pendingBinding = null;
            _releaseTimer.Stop();
        }
    }

    private static bool IsHotKeyDown(HotKeyConfig hotKey)
    {
        if (IsModifierRequired(hotKey, HotKeyModifiers.Control) && !IsKeyDown(0x11))
        {
            return false;
        }

        if (IsModifierRequired(hotKey, HotKeyModifiers.Alt) && !IsKeyDown(0x12))
        {
            return false;
        }

        if (IsModifierRequired(hotKey, HotKeyModifiers.Shift) && !IsKeyDown(0x10))
        {
            return false;
        }

        if (IsModifierRequired(hotKey, HotKeyModifiers.Win) && !IsKeyDown(0x5B) && !IsKeyDown(0x5C))
        {
            return false;
        }

        return IsKeyDown(hotKey.VirtualKey);
    }

    private static bool IsModifierRequired(HotKeyConfig hotKey, HotKeyModifiers modifier)
        => (hotKey.Modifiers & (int)modifier) != 0;

    private static bool IsKeyDown(int vKey) => (NativeMethods.GetAsyncKeyState(vKey) & 0x8000) != 0;
}
