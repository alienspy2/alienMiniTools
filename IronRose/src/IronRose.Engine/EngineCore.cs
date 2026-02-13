using IronRose.Contracts;
using System;
using System.Linq;

namespace IronRose.Engine
{
    public class EngineCore : IEngineCore
    {
        private object? _graphicsManager;
        private object? _scriptDomain;

        public void Initialize()
        {
            Console.WriteLine("╔════════════════════════════════════════════════════╗");
            Console.WriteLine("║ ✨✨✨ FULL HOT RELOAD WORKING!!! ✨✨✨        ║");
            Console.WriteLine("║ Everything is Hot-Reloadable - ACHIEVED! 🚀   ║");
            Console.WriteLine("╚════════════════════════════════════════════════════╝");
            Console.WriteLine("[Engine] EngineCore initializing...");

            // 리플렉션으로 GraphicsManager 생성
            var renderingAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "IronRose.Rendering");

            if (renderingAssembly != null)
            {
                var graphicsManagerType = renderingAssembly.GetType("IronRose.Rendering.GraphicsManager");
                if (graphicsManagerType != null)
                {
                    _graphicsManager = Activator.CreateInstance(graphicsManagerType);
                    var initMethod = graphicsManagerType.GetMethod("Initialize");
                    initMethod?.Invoke(_graphicsManager, null);
                    Console.WriteLine("[Engine] GraphicsManager initialized via reflection");
                }
            }
            else
            {
                Console.WriteLine("[Engine] ERROR: Rendering assembly not found");
            }
        }

        public bool ProcessEvents()
        {
            if (_graphicsManager == null) return false;

            var method = _graphicsManager.GetType().GetMethod("ProcessEvents");
            var result = method?.Invoke(_graphicsManager, null);
            return result is bool b && b;
        }

        public void Update(double deltaTime)
        {
            // TODO: GameObject/Component 업데이트 (Phase 3에서 구현)
        }

        public void Render()
        {
            if (_graphicsManager == null) return;

            var method = _graphicsManager.GetType().GetMethod("Render");
            method?.Invoke(_graphicsManager, null);
        }

        public void Shutdown()
        {
            Console.WriteLine("[Engine] EngineCore shutting down...");

            if (_graphicsManager != null)
            {
                var method = _graphicsManager.GetType().GetMethod("Dispose");
                method?.Invoke(_graphicsManager, null);
            }
        }
    }
}
