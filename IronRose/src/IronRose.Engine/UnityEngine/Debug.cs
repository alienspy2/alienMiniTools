using System;
using System.IO;

namespace UnityEngine
{
    public static class Debug
    {
        private static readonly string _logPath;
        private static readonly object _lock = new();

        /// <summary>로그 출력 활성화 여부 (기본 true)</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>와이어프레임 오버레이 표시 여부 (기본 false)</summary>
        public static bool wireframe { get; set; } = false;

        /// <summary>와이어프레임 색상 (기본 검정)</summary>
        public static Color wireframeColor { get; set; } = Color.black;

        static Debug()
        {
            Directory.CreateDirectory("logs");
            _logPath = Path.Combine("logs", $"ironrose_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        }

        public static void Log(object message) => Write("LOG", message);
        public static void LogWarning(object message) => Write("WARNING", message);
        public static void LogError(object message) => Write("ERROR", message);

        private static void Write(string level, object message)
        {
            if (!Enabled) return;

            var line = $"[{level}] {message}";
            Console.WriteLine(line);

            lock (_lock)
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {line}{Environment.NewLine}");
            }
        }
    }
}
