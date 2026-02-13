# Engine Hot Reload 문제 분석

**날짜**: 2026-02-13
**상태**: 핫 리로드 메커니즘은 작동하지만, 코드 변경사항이 반영되지 않음

---

## 현재 상황

### ✅ 작동하는 것
1. **핫 리로드 메커니즘**: EngineWatcher가 파일 변경 감지, 빌드 트리거, 엔진 리로드 성공
2. **윈도우 보존**: 핫 리로드 시 윈도우가 닫히지 않고 유지됨
3. **ALC 타입 격리 해결**: Veldrid를 기본 ALC에서만 로드하여 타입 일치
4. **스크린샷 기능**: logs/*.png로 자동 캡처, SwapBuffers() 전 캡처로 정확한 렌더링 결과 저장
5. **빌드 성공**: "Build SUCCESS" 메시지 출력, bin-hot 폴더에 새 DLL 생성

### ❌ 작동하지 않는 것
**핵심 문제**: 소스 코드를 변경해도 핫 리로드 후 화면에 반영되지 않음

#### 구체적인 증상
```
1. GraphicsManager.cs의 색상을 변경: 파란색(0,0,1) → 빨간색(1,0,0)
2. EngineWatcher가 변경 감지: "Detected change: GraphicsManager.cs"
3. 빌드 실행: "Build SUCCESS"
4. 새 DLL 생성: bin-hot/20260213_201914/IronRose.Rendering.dll (타임스탬프 일치)
5. 엔진 리로드: "HOT RELOAD COMPLETE"
6. 결과 화면: 여전히 파란색 (변경 전 색상 유지) ❌
```

---

## 시도한 해결책

### 1. ✅ Veldrid ALC 타입 격리 해결
**문제**: Bootstrapper의 Sdl2Window와 Engine ALC의 Sdl2Window 타입 불일치
**해결**: EngineLoader에서 Veldrid 관련 DLL을 기본 ALC에서만 로드
```csharp
if (fileName.StartsWith("Veldrid") || fileName.StartsWith("Silk.NET"))
{
    Console.WriteLine($"[EngineLoader] Skipped (use default ALC): {fileName}");
    continue;
}
```
**결과**: ✅ 윈도우 보존 성공

### 2. ✅ 스크린샷 타이밍 수정
**문제**: 스크린샷이 SwapBuffers() 후에 캡처되어 잘못된 버퍼 읽음
**해결**: RequestScreenshot/CaptureScreenshotInternal 패턴 도입, SwapBuffers() 전 캡처
```csharp
_graphicsDevice.SubmitCommands(_commandList);

if (_pendingScreenshot != null)
{
    CaptureScreenshotInternal(_pendingScreenshot);
    _pendingScreenshot = null;
}

_graphicsDevice.SwapBuffers();
```
**결과**: ✅ 스크린샷이 실제 렌더링 결과를 정확히 캡처

### 3. ❌ --force 플래그 추가
**시도**: `dotnet build --force`
**결과**: 효과 없음, 여전히 색상 변경 안됨

### 4. ❌ obj 폴더 삭제
**시도**: 빌드 전에 src/IronRose.Rendering/obj 삭제
**문제**: project.assets.json 파일 누락으로 빌드 실패
```
error NETSDK1004: 자산 파일 'obj/project.assets.json'을(를) 찾을 수 없습니다.
```
**추가 문제**: obj 폴더 삭제 시 FileSystemWatcher가 obj 폴더 재생성을 감지하여 무한 루프 발생
**결과**: ❌ 포기

### 5. ❌ IntermediateOutputPath 변경
**시도**: `/p:IntermediateOutputPath="bin-hot/obj/{timestamp}/"`
**문제**: 후행 슬래시 오류
```
error : IntermediateOutputPath 후행 슬래시로 끝나야 합니다.
```
**결과**: ❌ 포기

### 6. ❌ AssemblyName 변경
**시도**: `/p:AssemblyName=IronRose.Rendering_{timestamp}`
**문제**: NuGet과 충돌
```
error : Ambiguous project name 'IronRose.Rendering_20260213_200419'.
```
**결과**: ❌ 포기

### 7. ✅❌ dotnet msbuild /t:Rebuild 사용
**시도**: `dotnet msbuild IronRose.sln /t:Rebuild /p:OutputPath="bin-hot/{timestamp}/"`
**결과**:
- ✅ Build SUCCESS
- ✅ 새 DLL 생성 (타임스탬프 일치)
- ✅ 핫 리로드 완료
- ❌ **색상 변경 반영되지 않음!**

---

## 핵심 문제 분석

### 증상
- 소스 파일: `RgbaFloat(1.0f, 0.0f, 0.0f)` (빨간색)
- 빌드된 DLL: bin-hot/20260213_201914/IronRose.Rendering.dll
- 로드 로그: "Preloaded (shadow): IronRose.Rendering"
- 실행 결과: 파란색 (이전 색상)

### 가능한 원인

#### 1. MSBuild 증분 빌드 캐시
**가설**: /t:Rebuild가 clean + build를 수행하지만, 여전히 obj 폴더의 캐시를 사용
**증거**:
- obj 폴더를 수동 삭제한 후 빌드하면 색상이 제대로 반영됨
- 핫 리로드 시에는 obj 폴더가 남아있어 캐시 사용

#### 2. OutputPath 설정 문제
**가설**: msbuild의 /p:OutputPath가 솔루션 레벨에서 제대로 작동하지 않음
**증거**:
```
warning NETSDK1194: 솔루션을 빌드할 때 "--output" 옵션이 지원되지 않습니다.
```

#### 3. 소스 파일 타이밍 문제
**가설**: 파일 변경이 디스크에 flush되기 전에 빌드가 시작됨
**증거**: 불확실

---

## 검증 가능한 사실

### 수동 빌드 시 (obj 삭제 후)
```bash
$ rm -rf src/*/obj
$ dotnet build
$ dotnet run
결과: ✅ 색상 변경 정상 반영
```

### 핫 리로드 시 (obj 유지)
```bash
# 엔진 실행 중
$ Edit GraphicsManager.cs (색상 변경)
$ EngineWatcher 자동 빌드
결과: ❌ 색상 변경 반영 안됨
```

---

## 현재 EngineWatcher 코드

```csharp
// RebuildAndReload() 메서드
var process = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"msbuild IronRose.sln /t:Rebuild /p:Configuration=Debug /p:OutputPath=\"{hotDir}\\\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    }
};
```

**문제**: /t:Rebuild가 실제로는 캐시를 사용하고 있을 가능성

---

## 다음 시도 가능한 해결책

### A. 개별 프로젝트를 clean + build
```csharp
// 1. clean
dotnet clean src/IronRose.Rendering/IronRose.Rendering.csproj
// 2. build
dotnet build src/IronRose.Rendering/IronRose.Rendering.csproj -o "bin-hot/{timestamp}"
```

### B. obj 폴더를 직접 삭제 (재귀 문제 해결 필요)
```csharp
// FileSystemWatcher에서 obj 폴더 변경 무시 (이미 구현됨)
if (e.Name.Contains("\\obj\\")) return;

// RebuildAndReload에서 obj 삭제
Directory.Delete("src/IronRose.Rendering/obj", true);
Directory.Delete("src/IronRose.Engine/obj", true);
// dotnet restore 실행 (project.assets.json 재생성)
dotnet restore
// dotnet build
```

### C. 타임스탬프 기반 복사 후 빌드
```csharp
// 1. src/ 전체를 src-hot/{timestamp}/로 복사
// 2. src-hot/{timestamp}/에서 빌드
// 3. 결과를 bin-hot/{timestamp}/로 복사
```

### D. Roslyn으로 직접 컴파일
```csharp
// GraphicsManager.cs만 Roslyn으로 직접 컴파일
// 기존 IronRose.Rendering.dll에 동적으로 패치
```

---

## 임시 해결 방법 (현재 상태)

### 작동하는 시나리오
1. obj 폴더를 수동으로 삭제
2. dotnet build 실행
3. 엔진 실행
4. **결과**: ✅ 색상 변경 정상 반영

### 작동하지 않는 시나리오
1. 엔진 실행 중
2. GraphicsManager.cs 색상 변경
3. EngineWatcher 자동 빌드 (obj 폴더 유지)
4. 핫 리로드 실행
5. **결과**: ❌ 이전 색상 유지

---

## 결론

**핫 리로드 메커니즘 자체는 완벽하게 작동함**:
- ✅ 파일 변경 감지
- ✅ 자동 빌드 트리거
- ✅ bin-hot 폴더 전략 (파일 잠금 없음)
- ✅ ALC 언로드 및 재로드
- ✅ 윈도우 보존

**증분 빌드 문제**:
- ❌ msbuild가 obj 폴더의 캐시를 사용하여 변경사항을 무시함
- ❌ /t:Rebuild도 완전한 재빌드를 하지 않음

**해결 필요**:
- obj 폴더를 안전하게 삭제하고 restore하는 방법
- 또는 개별 프로젝트를 clean + build하는 방법
- 또는 완전히 다른 접근 (Roslyn 직접 컴파일 등)
