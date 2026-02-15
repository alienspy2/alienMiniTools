using System;
using System.Collections.Generic;
using Silk.NET.Input;
using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace RoseEngine
{
    public static class Input
    {
        // 키보드 상태
        private static readonly HashSet<KeyCode> _keysHeld = new();
        private static readonly HashSet<KeyCode> _keysDown = new();
        private static readonly HashSet<KeyCode> _keysUp = new();

        // 마우스 버튼 상태 (0=Left, 1=Right, 2=Middle)
        private static readonly bool[] _mouseHeld = new bool[3];
        private static readonly bool[] _mouseDown = new bool[3];
        private static readonly bool[] _mouseUp = new bool[3];

        // 마우스 위치/이동
        private static Vector2 _mousePosition;
        private static Vector2 _prevMousePosition;
        private static Vector2 _mouseDelta;
        private static float _mouseScrollDelta;

        // 프레임 간 이벤트 축적 버퍼
        private static readonly List<(KeyCode code, bool down)> _pendingKeyEvents = new();
        private static readonly List<(int index, bool down)> _pendingMouseEvents = new();
        private static float _pendingScrollDelta;
        private static Vector2 _latestMousePosition;

        // --- Public API: 키보드 ---

        public static bool GetKey(KeyCode key) => _keysHeld.Contains(key);
        public static bool GetKeyDown(KeyCode key) => _keysDown.Contains(key);
        public static bool GetKeyUp(KeyCode key) => _keysUp.Contains(key);

        public static bool anyKey => _keysHeld.Count > 0 || _mouseHeld[0] || _mouseHeld[1] || _mouseHeld[2];
        public static bool anyKeyDown => _keysDown.Count > 0 || _mouseDown[0] || _mouseDown[1] || _mouseDown[2];

        // --- Public API: 마우스 ---

        public static bool GetMouseButton(int button) => button >= 0 && button < 3 && _mouseHeld[button];
        public static bool GetMouseButtonDown(int button) => button >= 0 && button < 3 && _mouseDown[button];
        public static bool GetMouseButtonUp(int button) => button >= 0 && button < 3 && _mouseUp[button];

        public static Vector2 mousePosition => _mousePosition;
        public static float mouseScrollDelta => _mouseScrollDelta;

        // --- Public API: 축 입력 ---

        public static float GetAxis(string axisName)
        {
            switch (axisName)
            {
                case "Horizontal":
                {
                    float val = 0f;
                    if (_keysHeld.Contains(KeyCode.D) || _keysHeld.Contains(KeyCode.RightArrow)) val += 1f;
                    if (_keysHeld.Contains(KeyCode.A) || _keysHeld.Contains(KeyCode.LeftArrow)) val -= 1f;
                    return val;
                }
                case "Vertical":
                {
                    float val = 0f;
                    if (_keysHeld.Contains(KeyCode.W) || _keysHeld.Contains(KeyCode.UpArrow)) val += 1f;
                    if (_keysHeld.Contains(KeyCode.S) || _keysHeld.Contains(KeyCode.DownArrow)) val -= 1f;
                    return val;
                }
                case "Mouse X":
                    return _mouseDelta.x;
                case "Mouse Y":
                    return _mouseDelta.y;
                default:
                    return 0f;
            }
        }

        // --- Internal: 초기화 (Silk.NET IInputContext 이벤트 등록) ---

        internal static void Initialize(IInputContext context)
        {
            foreach (var kb in context.Keyboards)
            {
                kb.KeyDown += OnKeyDown;
                kb.KeyUp += OnKeyUp;
            }
            foreach (var mouse in context.Mice)
            {
                mouse.MouseDown += OnMouseDown;
                mouse.MouseUp += OnMouseUp;
                mouse.MouseMove += OnMouseMove;
                mouse.Scroll += OnScroll;
            }
        }

        // --- Internal: 매 프레임 시작 시 호출 (축적된 이벤트 처리) ---

        internal static void Update()
        {
            // 이전 프레임 Down/Up 초기화
            _keysDown.Clear();
            _keysUp.Clear();
            Array.Clear(_mouseDown, 0, 3);
            Array.Clear(_mouseUp, 0, 3);

            // 키보드 이벤트 처리
            foreach (var (code, down) in _pendingKeyEvents)
            {
                if (down)
                {
                    if (_keysHeld.Add(code))
                        _keysDown.Add(code);
                }
                else
                {
                    if (_keysHeld.Remove(code))
                        _keysUp.Add(code);
                }
            }
            _pendingKeyEvents.Clear();

            // 마우스 버튼 이벤트 처리
            foreach (var (idx, down) in _pendingMouseEvents)
            {
                if (down)
                {
                    if (!_mouseHeld[idx])
                        _mouseDown[idx] = true;
                    _mouseHeld[idx] = true;
                }
                else
                {
                    if (_mouseHeld[idx])
                        _mouseUp[idx] = true;
                    _mouseHeld[idx] = false;
                }
            }
            _pendingMouseEvents.Clear();

            // 마우스 위치 및 델타
            _prevMousePosition = _mousePosition;
            _mousePosition = _latestMousePosition;
            _mouseDelta = _mousePosition - _prevMousePosition;

            // 마우스 스크롤
            _mouseScrollDelta = _pendingScrollDelta;
            _pendingScrollDelta = 0f;
        }

        // --- Silk.NET 이벤트 콜백 (이벤트 축적) ---

        private static void OnKeyDown(IKeyboard kb, SilkKey key, int scancode)
        {
            var code = KeyCodeMapping.FromSilkNet(key);
            if (code != KeyCode.None)
                _pendingKeyEvents.Add((code, true));
        }

        private static void OnKeyUp(IKeyboard kb, SilkKey key, int scancode)
        {
            var code = KeyCodeMapping.FromSilkNet(key);
            if (code != KeyCode.None)
                _pendingKeyEvents.Add((code, false));
        }

        private static void OnMouseDown(IMouse mouse, SilkMouseButton button)
        {
            int idx = MouseButtonToIndex(button);
            if (idx >= 0)
                _pendingMouseEvents.Add((idx, true));
        }

        private static void OnMouseUp(IMouse mouse, SilkMouseButton button)
        {
            int idx = MouseButtonToIndex(button);
            if (idx >= 0)
                _pendingMouseEvents.Add((idx, false));
        }

        private static void OnMouseMove(IMouse mouse, System.Numerics.Vector2 pos)
        {
            _latestMousePosition = new Vector2(pos.X, pos.Y);
        }

        private static void OnScroll(IMouse mouse, ScrollWheel wheel)
        {
            _pendingScrollDelta += wheel.Y;
        }

        private static int MouseButtonToIndex(SilkMouseButton button)
        {
            return button switch
            {
                SilkMouseButton.Left => 0,
                SilkMouseButton.Right => 1,
                SilkMouseButton.Middle => 2,
                _ => -1,
            };
        }
    }
}
