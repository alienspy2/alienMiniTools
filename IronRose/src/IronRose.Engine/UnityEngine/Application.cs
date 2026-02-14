using System;

namespace UnityEngine
{
    public static class Application
    {
        internal static Action? QuitAction { get; set; }

        public static bool isPlaying { get; internal set; } = false;
        public static bool isPaused { get; internal set; } = false;
        public static string platform => Environment.OSVersion.Platform.ToString();
        public static int targetFrameRate { get; set; } = -1;

        /// <summary>엔진 일시정지 토글. 입력/렌더링은 유지, 게임 로직만 중단.</summary>
        public static void Pause()
        {
            isPaused = true;
            Console.WriteLine("[Engine] PAUSED");
        }

        /// <summary>엔진 재개.</summary>
        public static void Resume()
        {
            isPaused = false;
            Console.WriteLine("[Engine] PLAYING");
        }

        public static void Quit()
        {
            Debug.Log("[Application] Quit requested");
            QuitAction?.Invoke();
        }
    }
}
