using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

namespace IronRose.Scripting
{
    public class ScriptDomain
    {
        private AssemblyLoadContext? _currentALC;
        private Assembly? _currentAssembly;
        private readonly List<object> _scriptInstances = new();

        public bool IsLoaded => _scriptInstances.Count > 0;

        public void LoadScripts(byte[] assemblyBytes)
        {
            Console.WriteLine("[ScriptDomain] Loading scripts...");

            // 새로운 ALC 생성
            _currentALC = new AssemblyLoadContext($"ScriptContext_{DateTime.Now.Ticks}", isCollectible: true);

            // 어셈블리 로드
            using var ms = new System.IO.MemoryStream(assemblyBytes);
            _currentAssembly = _currentALC.LoadFromStream(ms);

            Console.WriteLine($"[ScriptDomain] Loaded assembly: {_currentAssembly.FullName}");

            // 스크립트 클래스 인스턴스화
            InstantiateScripts();
        }

        public void Reload(byte[] newAssemblyBytes)
        {
            Console.WriteLine("[ScriptDomain] Hot reloading scripts...");

            // 기존 상태 저장 (TODO: Phase 2.3에서 구현)
            // SaveState();

            // 기존 ALC 언로드
            UnloadPreviousContext();

            // 새 어셈블리 로드
            LoadScripts(newAssemblyBytes);

            // 상태 복원 (TODO: Phase 2.3에서 구현)
            // RestoreState();

            Console.WriteLine("[ScriptDomain] Hot reload completed!");
        }

        private void UnloadPreviousContext()
        {
            if (_currentALC == null)
            {
                Console.WriteLine("[ScriptDomain] No previous context to unload");
                return;
            }

            Console.WriteLine("[ScriptDomain] Unloading previous context...");

            _scriptInstances.Clear();
            _currentAssembly = null;

            var weakRef = new WeakReference(_currentALC, trackResurrection: true);
            _currentALC.Unload();
            _currentALC = null;

            // GC 강제 실행
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            if (weakRef.IsAlive)
            {
                Console.WriteLine("[ScriptDomain] WARNING: ALC not fully unloaded!");
            }
            else
            {
                Console.WriteLine("[ScriptDomain] Previous context unloaded successfully");
            }
        }

        private void InstantiateScripts()
        {
            if (_currentAssembly == null)
            {
                Console.WriteLine("[ScriptDomain] ERROR: No assembly loaded");
                return;
            }

            Console.WriteLine("[ScriptDomain] Instantiating script classes...");

            foreach (var type in _currentAssembly.GetTypes())
            {
                // Update() 메서드가 있는 클래스만 인스턴스화
                if (type.GetMethod("Update") != null)
                {
                    try
                    {
                        var instance = Activator.CreateInstance(type);
                        if (instance != null)
                        {
                            _scriptInstances.Add(instance);
                            Console.WriteLine($"[ScriptDomain] Instantiated: {type.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ScriptDomain] ERROR instantiating {type.Name}: {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"[ScriptDomain] Total instances: {_scriptInstances.Count}");
        }

        public void Update()
        {
            foreach (var instance in _scriptInstances)
            {
                try
                {
                    var updateMethod = instance.GetType().GetMethod("Update");
                    updateMethod?.Invoke(instance, null);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ScriptDomain] ERROR in Update: {ex.Message}");
                }
            }
        }
    }
}
