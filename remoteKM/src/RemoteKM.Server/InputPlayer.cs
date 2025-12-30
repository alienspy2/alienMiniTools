namespace RemoteKM.Server;

internal sealed class InputPlayer
{
    private readonly object _cursorLock = new();
    private bool _cursorInitialized;
    private int _cursorX;
    private int _cursorY;
    private CaptureEdge _stopEdge = CaptureEdge.None;

    internal event Action? CaptureStopRequested;

    private const int EdgeOffset = 16;

    internal void PlayKeyboard(KeyboardEvent evt)
    {
        if (Program.VerboseFlag)
        {
            Console.WriteLine($"Keyboard: msg=0x{evt.Message:X} vk={evt.VkCode} scan={evt.ScanCode} flags=0x{evt.Flags:X}");
        }

        var flags = 0u;

        if ((evt.Flags & NativeMethods.LLKHF_EXTENDED) != 0)
        {
            flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        }

        if (evt.Message == NativeMethods.WM_KEYUP || evt.Message == NativeMethods.WM_SYSKEYUP || (evt.Flags & NativeMethods.LLKHF_UP) != 0)
        {
            flags |= NativeMethods.KEYEVENTF_KEYUP;
        }

        var input = new NativeMethods.INPUT
        {
            type = 1,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)evt.VkCode,
                    wScan = (ushort)evt.ScanCode,
                    dwFlags = flags
                }
            }
        };

        if (evt.ScanCode != 0)
        {
            input.U.ki.wVk = 0;
            input.U.ki.dwFlags |= NativeMethods.KEYEVENTF_SCANCODE;
        }

        var sent = NativeMethods.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
        if (Program.VerboseFlag && sent != 1)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            Console.WriteLine($"SendInput keyboard mismatch: sent={sent} error={error}");
        }
    }

    internal void PlayMouse(MouseEvent evt)
    {
        if (Program.VerboseFlag)
        {
            Console.WriteLine($"Mouse: msg=0x{evt.Message:X} x={evt.X} y={evt.Y} data={evt.MouseData} flags=0x{evt.Flags:X}");
        }

        var inputs = new List<NativeMethods.INPUT>();

        if (evt.Message == NativeMethods.WM_MOUSEMOVE)
        {
            var (x, y) = AccumulateCursor(evt.X, evt.Y);
            inputs.Add(BuildMouseMoveAbsolute(x, y));
        }
        else
        {
            var (x, y) = AccumulateCursor(evt.X, evt.Y);
            inputs.Add(BuildMouseMoveAbsolute(x, y));

            var mouseFlags = MapMouseFlags(evt.Message);
            if (mouseFlags == 0)
            {
                if (Program.VerboseFlag)
                {
                    Console.WriteLine($"Mouse ignored: msg=0x{evt.Message:X}");
                }
                return;
            }

            var mouseData = (uint)evt.MouseData;
            if (evt.Message == NativeMethods.WM_MOUSEWHEEL || evt.Message == NativeMethods.WM_MOUSEHWHEEL)
            {
                mouseData = unchecked((uint)ExtractWheelDelta(evt.MouseData));
            }

            inputs.Add(new NativeMethods.INPUT
            {
                type = 0,
                U = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = mouseData,
                        dwFlags = mouseFlags
                    }
                }
            });
        }

        var sent = NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
        if (Program.VerboseFlag && sent != inputs.Count)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            Console.WriteLine($"SendInput mouse mismatch: expected={inputs.Count} sent={sent} error={error}");
        }
    }

    internal void BeginCapture(CaptureEdge localEdge)
    {
        if (localEdge == CaptureEdge.None)
        {
            EndCapture();
            return;
        }

        var stopEdge = GetOppositeEdge(localEdge);
        lock (_cursorLock)
        {
            _stopEdge = stopEdge;
        }

        var (x, y) = GetEdgeStartPosition(stopEdge, EdgeOffset);
        SetCursorPosition(x, y);
    }

    internal void EndCapture()
    {
        lock (_cursorLock)
        {
            _stopEdge = CaptureEdge.None;
        }
    }

    private (int X, int Y) AccumulateCursor(int deltaX, int deltaY)
    {
        bool stopCapture = false;
        int x;
        int y;
        lock (_cursorLock)
        {
            var screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            var screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

            if (!_cursorInitialized)
            {
                if (NativeMethods.GetCursorPos(out var point))
                {
                    _cursorX = point.X;
                    _cursorY = point.Y;
                }
                else
                {
                    _cursorX = screenWidth / 2;
                    _cursorY = screenHeight / 2;
                }

                _cursorInitialized = true;
            }

            _cursorX = Math.Clamp(_cursorX + deltaX, 0, Math.Max(0, screenWidth - 1));
            _cursorY = Math.Clamp(_cursorY + deltaY, 0, Math.Max(0, screenHeight - 1));
            x = _cursorX;
            y = _cursorY;

            if (_stopEdge != CaptureEdge.None && IsAtEdge(x, y, _stopEdge, screenWidth, screenHeight))
            {
                _stopEdge = CaptureEdge.None;
                stopCapture = true;
            }
        }

        if (stopCapture)
        {
            CaptureStopRequested?.Invoke();
        }

        return (x, y);
    }

    private (int X, int Y) GetEdgeStartPosition(CaptureEdge edge, int offset)
    {
        var screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        var screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        var right = Math.Max(0, screenWidth - 1);
        var bottom = Math.Max(0, screenHeight - 1);

        int x;
        int y;
        lock (_cursorLock)
        {
            EnsureCursorInitialized(screenWidth, screenHeight);
            x = _cursorX;
            y = _cursorY;
        }
        switch (edge)
        {
            case CaptureEdge.Left:
                x = offset;
                break;
            case CaptureEdge.Right:
                x = Math.Max(0, right - offset);
                break;
            case CaptureEdge.Top:
                y = offset;
                break;
            case CaptureEdge.Bottom:
                y = Math.Max(0, bottom - offset);
                break;
        }

        x = Math.Clamp(x, 0, right);
        y = Math.Clamp(y, 0, bottom);
        return (x, y);
    }

    private void EnsureCursorInitialized(int screenWidth, int screenHeight)
    {
        if (_cursorInitialized)
        {
            return;
        }

        if (NativeMethods.GetCursorPos(out var point))
        {
            _cursorX = Math.Clamp(point.X, 0, Math.Max(0, screenWidth - 1));
            _cursorY = Math.Clamp(point.Y, 0, Math.Max(0, screenHeight - 1));
        }
        else
        {
            _cursorX = screenWidth / 2;
            _cursorY = screenHeight / 2;
        }

        _cursorInitialized = true;
    }

    private void SetCursorPosition(int x, int y)
    {
        int cursorX;
        int cursorY;
        int screenWidth;
        int screenHeight;

        lock (_cursorLock)
        {
            screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            var right = Math.Max(0, screenWidth - 1);
            var bottom = Math.Max(0, screenHeight - 1);
            cursorX = Math.Clamp(x, 0, right);
            cursorY = Math.Clamp(y, 0, bottom);
            _cursorX = cursorX;
            _cursorY = cursorY;
            _cursorInitialized = true;
        }

        var input = BuildMouseMoveAbsolute(cursorX, cursorY);
        NativeMethods.SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static CaptureEdge GetOppositeEdge(CaptureEdge edge)
    {
        return edge switch
        {
            CaptureEdge.Left => CaptureEdge.Right,
            CaptureEdge.Right => CaptureEdge.Left,
            CaptureEdge.Top => CaptureEdge.Bottom,
            CaptureEdge.Bottom => CaptureEdge.Top,
            _ => CaptureEdge.None
        };
    }

    private static bool IsAtEdge(int x, int y, CaptureEdge edge, int screenWidth, int screenHeight)
    {
        var right = Math.Max(0, screenWidth - 1);
        var bottom = Math.Max(0, screenHeight - 1);
        return edge switch
        {
            CaptureEdge.Left => x <= 0,
            CaptureEdge.Right => x >= right,
            CaptureEdge.Top => y <= 0,
            CaptureEdge.Bottom => y >= bottom,
            _ => false
        };
    }

    private static NativeMethods.INPUT BuildMouseMoveAbsolute(int x, int y)
    {
        var screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN) - 1;
        var screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN) - 1;

        var normalizedX = (int)Math.Round(x * 65535.0 / Math.Max(1, screenWidth));
        var normalizedY = (int)Math.Round(y * 65535.0 / Math.Max(1, screenHeight));

        return new NativeMethods.INPUT
        {
            type = 0,
            U = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = normalizedX,
                    dy = normalizedY,
                    mouseData = 0,
                    dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE
                }
            }
        };
    }

    private static uint MapMouseFlags(int message) => message switch
    {
        NativeMethods.WM_MOUSEMOVE => NativeMethods.MOUSEEVENTF_MOVE,
        NativeMethods.WM_LBUTTONDOWN => NativeMethods.MOUSEEVENTF_LEFTDOWN,
        NativeMethods.WM_LBUTTONUP => NativeMethods.MOUSEEVENTF_LEFTUP,
        NativeMethods.WM_RBUTTONDOWN => NativeMethods.MOUSEEVENTF_RIGHTDOWN,
        NativeMethods.WM_RBUTTONUP => NativeMethods.MOUSEEVENTF_RIGHTUP,
        NativeMethods.WM_MBUTTONDOWN => NativeMethods.MOUSEEVENTF_MIDDLEDOWN,
        NativeMethods.WM_MBUTTONUP => NativeMethods.MOUSEEVENTF_MIDDLEUP,
        NativeMethods.WM_MOUSEWHEEL => NativeMethods.MOUSEEVENTF_WHEEL,
        NativeMethods.WM_MOUSEHWHEEL => NativeMethods.MOUSEEVENTF_HWHEEL,
        NativeMethods.WM_XBUTTONDOWN => NativeMethods.MOUSEEVENTF_XDOWN,
        NativeMethods.WM_XBUTTONUP => NativeMethods.MOUSEEVENTF_XUP,
        _ => 0
    };

    private static int ExtractWheelDelta(int mouseData)
    {
        return (short)((mouseData >> 16) & 0xFFFF);
    }
}
