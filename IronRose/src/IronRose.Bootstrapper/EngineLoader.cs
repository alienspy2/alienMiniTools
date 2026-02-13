using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using IronRose.Contracts;

namespace IronRose.Bootstrapper
{
    public class EngineLoader
    {
        private AssemblyLoadContext? _currentALC;
        private IEngineCore? _currentEngine;

        public IEngineCore LoadEngine(string? hotBuildPath = null)
        {
            if (hotBuildPath != null)
            {
                Console.WriteLine($"[EngineLoader] HOT RELOAD: Loading from {hotBuildPath}");
            }
            else
            {
                Console.WriteLine("[EngineLoader] Loading engine assemblies from standard paths");
            }

            // 새로운 ALC 생성
            _currentALC = new AssemblyLoadContext($"EngineContext_{DateTime.Now.Ticks}", isCollectible: true);

            // 의존성 해결 설정
            _currentALC.Resolving += OnResolving;

            // DLL 로드 경로 결정 (핫 리로드 시 bin-hot 사용)
            var dllPath = hotBuildPath ?? Path.GetFullPath("src/IronRose.Rendering/bin/Debug/net10.0");

            // 모든 DLL 미리 로드 (Shadow Copy로 파일 잠금 방지)
            if (Directory.Exists(dllPath))
            {
                foreach (var dllFile in Directory.GetFiles(dllPath, "*.dll"))
                {
                    var fileName = Path.GetFileName(dllFile);

                    // Contracts와 Veldrid는 기본 ALC에만 있어야 함, 중복 로드 금지
                    if (fileName.StartsWith("IronRose.Contracts") ||
                        fileName.StartsWith("Veldrid") ||
                        fileName.StartsWith("Silk.NET"))
                    {
                        Console.WriteLine($"[EngineLoader] Skipped (use default ALC): {fileName}");
                        continue;
                    }

                    try
                    {
                        // Shadow Copy: 파일을 메모리로 읽어서 LoadFromStream 사용
                        // 이렇게 하면 파일 잠금이 발생하지 않음!
                        byte[] assemblyBytes = File.ReadAllBytes(dllFile);
                        using var ms = new MemoryStream(assemblyBytes);
                        var asm = _currentALC.LoadFromStream(ms);
                        Console.WriteLine($"[EngineLoader] Preloaded (shadow): {asm.GetName().Name}");
                    }
                    catch (Exception ex)
                    {
                        // 일부 DLL은 로드 실패할 수 있음 (네이티브 DLL 등)
                        Console.WriteLine($"[EngineLoader] Skipped: {fileName} ({ex.Message})");
                    }
                }
            }

            // 추가 엔진 DLL들 로드 (이미 로드되지 않은 경우)
            var additionalAssemblies = new[] { "IronRose.Engine", "IronRose.Scripting" };

            foreach (var asmName in additionalAssemblies)
            {
                // 타임스탬프 포함 이름도 허용 (예: IronRose.Engine_20260213_195640)
                if (_currentALC.Assemblies.Any(a => a.GetName().Name.StartsWith(asmName)))
                {
                    Console.WriteLine($"[EngineLoader] Already loaded: {asmName}");
                    continue;
                }

                // 핫 리로드 경로 또는 기본 경로에서 찾기
                string? asmPath = null;
                if (hotBuildPath != null)
                {
                    // bin-hot 폴더에서 패턴 매칭 (IronRose.Engine*.dll)
                    var matchingFiles = Directory.GetFiles(hotBuildPath, $"{asmName}*.dll");
                    asmPath = matchingFiles.FirstOrDefault();
                }
                else
                {
                    asmPath = Path.GetFullPath($"src/{asmName}/bin/Debug/net10.0/{asmName}.dll");
                }

                if (asmPath != null && File.Exists(asmPath))
                {
                    byte[] assemblyBytes = File.ReadAllBytes(asmPath);
                    using var ms = new MemoryStream(assemblyBytes);
                    var assembly = _currentALC.LoadFromStream(ms);
                    Console.WriteLine($"[EngineLoader] Loaded (shadow): {assembly.GetName().Name}");
                }
                else
                {
                    Console.WriteLine($"[EngineLoader] WARNING: Assembly not found for: {asmName}");
                }
            }

            // EngineCore 인스턴스 생성 (타임스탬프 포함 이름도 허용)
            var engineAssembly = _currentALC.Assemblies
                .FirstOrDefault(a => a.GetName().Name.StartsWith("IronRose.Engine"));

            if (engineAssembly == null)
            {
                throw new Exception("[EngineLoader] ERROR: IronRose.Engine assembly not loaded");
            }

            var engineType = engineAssembly.GetType("IronRose.Engine.EngineCore");
            if (engineType == null)
            {
                throw new Exception("[EngineLoader] ERROR: EngineCore type not found");
            }

            _currentEngine = (IEngineCore)Activator.CreateInstance(engineType)!;
            Console.WriteLine("[EngineLoader] EngineCore instantiated");

            return _currentEngine;
        }

        private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
        {
            // 같은 ALC 내에서 어셈블리 찾기
            var assembly = context.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
            if (assembly != null)
            {
                Console.WriteLine($"[EngineLoader] Resolved dependency: {name.Name}");
                return assembly;
            }

            // Contracts 또는 NuGet 패키지는 기본 ALC에서 로드 시도
            try
            {
                var defaultAssembly = AssemblyLoadContext.Default.LoadFromAssemblyName(name);
                Console.WriteLine($"[EngineLoader] Loaded from default ALC: {name.Name}");
                return defaultAssembly;
            }
            catch
            {
                // 기본 ALC에 없음, 계속 진행
            }

            Console.WriteLine($"[EngineLoader] WARNING: Could not resolve: {name.Name}");
            return null;
        }

        public void UnloadEngine()
        {
            Console.WriteLine("[EngineLoader] Unloading engine...");

            try
            {
                if (_currentEngine != null)
                {
                    Console.WriteLine("[EngineLoader] DEBUG: Calling Shutdown");
                    _currentEngine.Shutdown();
                    Console.WriteLine("[EngineLoader] DEBUG: Shutdown complete");
                    _currentEngine = null;
                }

                if (_currentALC != null)
                {
                    Console.WriteLine("[EngineLoader] DEBUG: Creating WeakReference");
                    var weakRef = new WeakReference(_currentALC);

                    Console.WriteLine("[EngineLoader] DEBUG: Removing Resolving event");
                    _currentALC.Resolving -= OnResolving;

                    Console.WriteLine("[EngineLoader] DEBUG: Calling ALC.Unload()");
                    _currentALC.Unload();
                    _currentALC = null;

                    Console.WriteLine("[EngineLoader] DEBUG: Starting GC (5 iterations)");
                    // GC 강제 실행
                    for (int i = 0; i < 5; i++)
                    {
                        Console.WriteLine($"[EngineLoader] DEBUG: GC iteration {i + 1}");
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                    Console.WriteLine("[EngineLoader] DEBUG: GC complete");

                    if (weakRef.IsAlive)
                    {
                        Console.WriteLine("[EngineLoader] WARNING: Engine ALC not fully unloaded!");
                    }
                    else
                    {
                        Console.WriteLine("[EngineLoader] Engine ALC unloaded successfully");
                    }
                }

                Console.WriteLine("[EngineLoader] DEBUG: UnloadEngine COMPLETE");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EngineLoader] ERROR in UnloadEngine: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        public void HotReloadEngine()
        {
            Console.WriteLine("[EngineLoader] Hot reloading engine...");

            UnloadEngine();

            // 새 엔진 로드
            _currentEngine = LoadEngine();
            _currentEngine.Initialize();

            Console.WriteLine("[EngineLoader] Engine hot reload completed!");
        }

        public IEngineCore? CurrentEngine => _currentEngine;
    }
}
