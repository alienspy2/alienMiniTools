namespace UnityEngine
{
    public static class Time
    {
        public static float timeScale { get; set; } = 1f;
        public static float deltaTime { get; internal set; }
        public static float unscaledDeltaTime { get; internal set; }
        public static float time { get; internal set; }
        public static int frameCount { get; internal set; }
    }
}
