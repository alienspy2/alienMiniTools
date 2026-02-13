# Phase 8: AI 통합 (LLM API)

## 목표
사용자가 자연어로 명령하면 AI가 코드를 생성하고 엔진이 즉시 실행하는 데모를 완성합니다.

---

## 작업 항목

### 8.1 LLM API 통합

**AICodeGenerator.cs (IronRose.AI):**
```csharp
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IronRose.AI
{
    public class AICodeGenerator
    {
        private readonly HttpClient _httpClient = new();
        private readonly string _apiKey;
        private readonly string _apiEndpoint;

        public AICodeGenerator(string apiKey, string endpoint = "https://api.anthropic.com/v1/messages")
        {
            _apiKey = apiKey;
            _apiEndpoint = endpoint;
        }

        public async Task<string> GenerateCode(string prompt)
        {
            var systemPrompt = @"
You are a Unity C# code generator for the IronRose game engine.
Generate ONLY valid C# code that uses UnityEngine namespace.
Do NOT include explanations, markdown, or code blocks.
Just output raw C# code.

Available APIs:
- UnityEngine.GameObject
- UnityEngine.MonoBehaviour
- UnityEngine.Transform
- UnityEngine.Vector3, Quaternion, Color
- UnityEngine.Time, Debug

Example:
using UnityEngine;

public class RotatingCube : MonoBehaviour
{
    void Update()
    {
        transform.Rotate(0, Time.deltaTime * 45, 0);
    }
}
";

            var requestBody = new
            {
                model = "claude-3-5-sonnet-20241022",
                max_tokens = 2048,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, _apiEndpoint)
            {
                Headers =
                {
                    { "x-api-key", _apiKey },
                    { "anthropic-version", "2023-06-01" }
                },
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"
                )
            };

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"API Error: {response.StatusCode} - {responseContent}");
            }

            var jsonDoc = JsonDocument.Parse(responseContent);
            var text = jsonDoc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString();

            return text ?? string.Empty;
        }
    }
}
```

---

### 8.2 명령 인터페이스

**AIConsole.cs (간단한 텍스트 입력):**
```csharp
using System;
using System.Threading.Tasks;
using IronRose.Scripting;

namespace IronRose.AI
{
    public class AIConsole
    {
        private readonly AICodeGenerator _ai;
        private readonly ScriptCompiler _compiler;
        private readonly ScriptDomain _scriptDomain;

        public AIConsole(string apiKey)
        {
            _ai = new AICodeGenerator(apiKey);
            _compiler = new ScriptCompiler();
            _scriptDomain = new ScriptDomain();
        }

        public async Task ProcessCommand(string userPrompt)
        {
            Console.WriteLine($"\n[AI] Processing: \"{userPrompt}\"");
            Console.WriteLine("[AI] Generating code...");

            try
            {
                // 1. AI로부터 코드 생성
                var code = await _ai.GenerateCode(userPrompt);
                Console.WriteLine($"[AI] Generated code:\n{code}\n");

                // 2. 코드 컴파일
                var result = _compiler.CompileFromSource(code, "AIGenerated");

                if (!result.Success)
                {
                    Console.WriteLine("[AI] Compilation errors:");
                    foreach (var error in result.Errors)
                    {
                        Console.WriteLine($"  - {error}");
                    }
                    return;
                }

                // 3. 핫 리로드
                _scriptDomain.Reload(result.AssemblyBytes!);

                Console.WriteLine("[AI] ✓ Code compiled and loaded successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] ERROR: {ex.Message}");
            }
        }
    }
}
```

**Program.cs 통합:**
```csharp
private static AIConsole _aiConsole = null!;

static void Main(string[] args)
{
    // ... 초기화 ...

    string apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
    if (!string.IsNullOrEmpty(apiKey))
    {
        _aiConsole = new AIConsole(apiKey);
        Console.WriteLine("[AI] AI Console enabled! Type '/ai <prompt>' to generate code.");
    }

    // ... 메인 루프 ...
}

static async Task ProcessConsoleInput()
{
    if (Console.KeyAvailable)
    {
        var input = Console.ReadLine();
        if (input?.StartsWith("/ai ") == true)
        {
            var prompt = input.Substring(4);
            await _aiConsole.ProcessCommand(prompt);
        }
    }
}
```

---

### 8.3 코드 검증 및 샌드박싱

**CodeValidator.cs:**
```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;

namespace IronRose.AI
{
    public class CodeValidator
    {
        private readonly string[] _bannedNamespaces = new[]
        {
            "System.IO",
            "System.Net",
            "System.Reflection",
            "System.Diagnostics.Process"
        };

        private readonly string[] _bannedMethods = new[]
        {
            "File.Delete",
            "File.WriteAllText",
            "Directory.Delete",
            "Process.Start",
            "Environment.Exit"
        };

        public ValidationResult Validate(string sourceCode)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = syntaxTree.GetRoot();

            // 금지된 namespace 검사
            var usingDirectives = root.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>();

            foreach (var usingDir in usingDirectives)
            {
                var namespaceName = usingDir.Name.ToString();
                if (_bannedNamespaces.Any(banned => namespaceName.StartsWith(banned)))
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Forbidden namespace: {namespaceName}"
                    };
                }
            }

            // 금지된 메서드 호출 검사 (간단한 텍스트 검색)
            foreach (var banned in _bannedMethods)
            {
                if (sourceCode.Contains(banned))
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Forbidden method call: {banned}"
                    };
                }
            }

            return new ValidationResult { IsValid = true };
        }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
```

**타임아웃 보호:**
```csharp
public class ScriptExecutor
{
    private readonly CancellationTokenSource _cts = new();

    public void ExecuteWithTimeout(Action action, int timeoutMs = 5000)
    {
        var task = Task.Run(action, _cts.Token);

        if (!task.Wait(timeoutMs))
        {
            _cts.Cancel();
            throw new TimeoutException("Script execution timeout!");
        }
    }
}
```

---

### 8.4 데모 시나리오

**예시 프롬프트 및 결과:**

**Prompt 1:**
```
"빨간색 구를 만들고 위아래로 움직이게 해줘"
```

**AI 생성 코드:**
```csharp
using UnityEngine;

public class BouncingSphere : MonoBehaviour
{
    void Start()
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.position = Vector3.zero;

        var renderer = sphere.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material.color = Color.red;
        }
    }

    void Update()
    {
        float y = Mathf.Sin(Time.time * 2f);
        transform.position = new Vector3(0, y, 0);
    }
}
```

**Prompt 2:**
```
"카메라를 천천히 오른쪽으로 회전시켜줘"
```

**AI 생성 코드:**
```csharp
using UnityEngine;

public class CameraRotator : MonoBehaviour
{
    void Update()
    {
        Camera.main.transform.Rotate(0, Time.deltaTime * 10f, 0);
    }
}
```

---

## 검증 기준

✅ 프롬프트 입력 후 3초 이내에 게임에 반영됨
✅ 잘못된 코드는 안전하게 에러 메시지만 출력
✅ 금지된 API 호출 시도 시 차단됨
✅ 무한 루프 시 타임아웃으로 중단됨

---

## 데모 영상 시나리오

1. 엔진 실행
2. `/ai 빨간색 큐브를 만들고 회전시켜줘` 입력
3. 3초 이내에 빨간색 회전하는 큐브 생성
4. `/ai 카메라를 위로 이동시켜줘` 입력
5. 카메라 시점 변경 확인

---

## 예상 소요 시간
**4-5일**

---

## 다음 단계
→ [Phase 9: 최적화 및 안정화](Phase9_Optimization.md)
