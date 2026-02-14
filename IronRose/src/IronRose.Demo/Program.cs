using System;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using IronRose.Engine;
using UnityEngine;

namespace IronRose.Demo
{
    class Program
    {
        private static EngineCore? _engine;
        private static IWindow? _window;

        private static int _frameCount = 0;
        private static double _fpsTimer = 0;

        static void Main(string[] _)
        {
            Console.WriteLine("[IronRose] Engine Starting...");

            var options = WindowOptions.DefaultVulkan;
            options.Size = new Vector2D<int>(1280, 720);
            options.Position = new Vector2D<int>(100, 100);
            options.Title = "IronRose Engine";
            options.UpdatesPerSecond = 60;
            options.FramesPerSecond = 60;
            options.API = GraphicsAPI.None;

            _window = Window.Create(options);
            _window.Load += OnLoad;
            _window.Update += OnUpdate;
            _window.Render += OnRender;
            _window.Closing += OnClosing;

            _window.Run();

            Console.WriteLine("[IronRose] Engine stopped");
        }

        static void OnLoad()
        {
            Console.WriteLine($"[IronRose] Window created: {_window!.Size.X}x{_window.Size.Y}");

            _engine = new EngineCore();
            _engine.Initialize(_window);

            // Demo MonoBehaviour 등록
            RegisterScene();
        }

        static void RegisterScene()
        {
            Console.WriteLine("[Demo] Registering scene...");

            Register<AnotherScript>();
            Register<TestScript>();

            Console.WriteLine("[Demo] Scene ready");
        }

        static void Register<T>() where T : MonoBehaviour, new()
        {
            var go = new GameObject(typeof(T).Name);
            var behaviour = go.AddComponent<T>();
            SceneManager.RegisterBehaviour(behaviour);
            Console.WriteLine($"[Demo] {typeof(T).Name}");
        }

        static void OnUpdate(double deltaTime)
        {
            _frameCount++;
            _fpsTimer += deltaTime;
            if (_fpsTimer >= 1.0)
            {
                Console.WriteLine($"[IronRose] FPS: {_frameCount / _fpsTimer:F2} | Frame Time: {deltaTime * 1000:F2}ms");
                _frameCount = 0;
                _fpsTimer = 0;
            }

            try { _engine!.Update(deltaTime); }
            catch (Exception ex) { Console.WriteLine($"[IronRose] ERROR: {ex.Message}"); }
        }

        static void OnRender(double deltaTime)
        {
            try { _engine!.Render(); }
            catch (Exception ex) { Console.WriteLine($"[IronRose] ERROR: {ex.Message}"); }
        }

        static void OnClosing()
        {
            Console.WriteLine("[IronRose] Shutting down...");
            _engine?.Shutdown();
        }
    }
}
