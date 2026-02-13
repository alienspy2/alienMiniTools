using System;
using System.Diagnostics;
using System.IO;
using System.Timers;

namespace IronRose.Bootstrapper
{
    public class EngineWatcher
    {
        private FileSystemWatcher _watcher;
        private System.Timers.Timer _rebuildTimer;
        private bool _isRebuilding = false;

        public event Action<string>? OnEngineRebuilt;  // 새 DLL 경로 전달

        public EngineWatcher()
        {
            // src 폴더의 모든 .cs 파일 감시
            _watcher = new FileSystemWatcher("src", "*.cs")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024  // 64KB (기본: 8KB)
            };

            _watcher.Changed += OnFileChanged;

            // 디바운싱 타이머 (1초 내 여러 변경 → 한 번만 빌드)
            _rebuildTimer = new System.Timers.Timer(1000);
            _rebuildTimer.AutoReset = false;
            _rebuildTimer.Elapsed += (s, e) => RebuildAndReload();

            Console.WriteLine("[EngineWatcher] Initialized");
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine($"[EngineWatcher] Detected change: {e.Name}");

            // 타이머 리셋 (디바운싱)
            _rebuildTimer.Stop();
            _rebuildTimer.Start();
        }

        private void RebuildAndReload()
        {
            if (_isRebuilding)
            {
                Console.WriteLine("[EngineWatcher] Already rebuilding, skipping...");
                return;
            }

            _isRebuilding = true;
            Console.WriteLine("[EngineWatcher] ===== HOT RELOAD START =====");

            // 고유 이름으로 빌드 (타임스탬프 기반)
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string hotDir = Path.GetFullPath($"bin-hot/{timestamp}");
            Directory.CreateDirectory(hotDir);

            // 고유 폴더로 빌드
            Console.WriteLine($"[EngineWatcher] Building to: {hotDir}");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build IronRose.sln --no-restore -o \"{hotDir}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Console.WriteLine("[EngineWatcher] Build FAILED");
                Console.WriteLine(output);
                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine(error);
                }
                _isRebuilding = false;
                return;
            }

            Console.WriteLine("[EngineWatcher] Build SUCCESS");

            // 핫 리로드 트리거 (새 DLL 경로 전달)
            OnEngineRebuilt?.Invoke(hotDir);

            Console.WriteLine("[EngineWatcher] ===== HOT RELOAD COMPLETE =====");
            _isRebuilding = false;
        }

        public void Enable()
        {
            _watcher.EnableRaisingEvents = true;
            Console.WriteLine("[EngineWatcher] Watching src/**/*.cs for changes");
        }

        public void Disable()
        {
            _watcher.EnableRaisingEvents = false;
            Console.WriteLine("[EngineWatcher] Stopped watching");
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _rebuildTimer?.Dispose();
        }
    }
}
