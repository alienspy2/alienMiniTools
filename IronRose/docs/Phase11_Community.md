# Phase 11: 커뮤니티 & 오픈소스

## 목표
IronRose를 오픈소스로 공개하고 커뮤니티를 구축합니다.

---

## 작업 항목

### 11.1 GitHub 공개

**MIT 라이선스 적용 (LICENSE):**
```
MIT License

Copyright (c) 2026 IronRose Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

**README.md:**
```markdown
# 🌹 IronRose

> **AI-Native Game Engine - From Prompt to Play**

IronRose는 AI(LLM)와의 협업을 최우선으로 설계된 .NET 10 기반 게임 엔진입니다.
Unity API 호환성을 유지하면서도 **런타임 코드 생성 및 핫 리로딩**에 특화되어 있습니다.

## ✨ 주요 기능

- 🤖 **AI 코드 생성**: 자연어 프롬프트로 게임 오브젝트 생성
- 🔥 **핫 리로딩**: 게임 중단 없이 코드 수정 즉시 반영
- 🎮 **Unity 호환**: 기존 Unity 스크립트를 그대로 실행
- 🚀 **가볍고 빠름**: 무거운 에디터 없이 순수 런타임만
- 🌐 **크로스 플랫폼**: Windows, Linux 지원

## 🚀 빠른 시작

### 요구사항
- .NET 10 SDK
- Vulkan 지원 GPU

### 설치
```bash
git clone https://github.com/yourusername/IronRose.git
cd IronRose
dotnet build
dotnet run --project src/IronRose.Engine
```

### 첫 번째 스크립트
```csharp
using UnityEngine;

public class HelloWorld : MonoBehaviour
{
    void Start()
    {
        Debug.Log("Hello, IronRose!");
    }
}
```

## 📖 문서
- [API Reference](https://ironrose.dev/docs)
- [Unity 마이그레이션 가이드](docs/UnityMigration.md)
- [성능 최적화](docs/BestPractices.md)

## 🎬 데모 영상
[![IronRose Demo](thumbnail.png)](https://youtube.com/watch?v=...)

## 🤝 기여하기
[CONTRIBUTING.md](CONTRIBUTING.md)를 참고해주세요!

## 📜 라이선스
MIT License - [LICENSE](LICENSE) 참고

## 💬 커뮤니티
- [Discord](https://discord.gg/ironrose)
- [Twitter](https://twitter.com/ironrose_engine)
- [Reddit](https://reddit.com/r/ironrose)

---

**Iron for Strength, Rose for Beauty** 🌹
```

**CONTRIBUTING.md:**
```markdown
# Contributing to IronRose

IronRose에 기여해주셔서 감사합니다! 🎉

## 개발 환경 설정

1. Repository Fork
2. Clone your fork:
   ```bash
   git clone https://github.com/yourusername/IronRose.git
   ```
3. 의존성 설치:
   ```bash
   dotnet restore
   ```
4. 빌드 및 테스트:
   ```bash
   dotnet build
   dotnet test
   ```

## 코드 스타일

- C# 코딩 컨벤션 준수
- UTF-8 with BOM 사용 (.cs 파일)
- 들여쓰기: 스페이스 4칸
- Pull Request 전 `dotnet format` 실행

## Pull Request 프로세스

1. 새 브랜치 생성:
   ```bash
   git checkout -b feature/your-feature-name
   ```
2. 변경사항 커밋:
   ```bash
   git commit -m "Add: your feature description"
   ```
3. Push:
   ```bash
   git push origin feature/your-feature-name
   ```
4. GitHub에서 Pull Request 생성

## 커밋 메시지 컨벤션

- `Add:` 새 기능 추가
- `Fix:` 버그 수정
- `Update:` 기존 기능 개선
- `Refactor:` 코드 리팩토링
- `Docs:` 문서 변경
- `Test:` 테스트 추가/수정

## 이슈 보고

버그를 발견하셨나요? [GitHub Issues](https://github.com/yourusername/IronRose/issues)에 보고해주세요!

### 버그 리포트 템플릿
- **환경**: OS, .NET 버전, GPU
- **재현 방법**: 1. 2. 3. ...
- **예상 동작**:
- **실제 동작**:
- **스크린샷**:
```

**Issue 템플릿 (.github/ISSUE_TEMPLATE/bug_report.md):**
```markdown
---
name: Bug Report
about: 버그를 발견하셨나요?
title: "[BUG] "
labels: bug
assignees: ''
---

**버그 설명**
무엇이 잘못되었나요?

**재현 방법**
1. Go to '...'
2. Click on '....'
3. See error

**예상 동작**
어떻게 동작해야 하나요?

**스크린샷**
가능하면 스크린샷을 첨부해주세요.

**환경**
 - OS: [e.g. Windows 11]
 - .NET Version: [e.g. .NET 10]
 - GPU: [e.g. NVIDIA RTX 3060]
 - IronRose Version: [e.g. 0.1.0]

**추가 정보**
```

---

### 11.2 커뮤니티 구축

**Discord 서버 개설:**

**채널 구조:**
```
📢 공지사항
  - #announcements
  - #updates

💬 일반
  - #general
  - #showcase (사용자 프로젝트)
  - #off-topic

🛠️ 개발
  - #help (질문)
  - #bug-reports
  - #feature-requests
  - #contributions

📚 리소스
  - #tutorials
  - #documentation
  - #ai-prompts (AI 프롬프트 공유)
```

**Discord 봇 (선택사항):**
- GitHub 커밋 알림
- Issue/PR 알림
- 환영 메시지

**Reddit 커뮤니티:**
- r/IronRose 생성
- 주간 개발 업데이트 포스팅
- Q&A 세션

**Twitter 계정:**
- @IronRose_Engine
- 개발 진행 상황 트윗
- 커뮤니티 프로젝트 리트윗

---

### 11.3 플러그인 생태계

**NuGet 패키지 배포:**

**IronRose.Core.nupkg:**
```xml
<?xml version="1.0"?>
<package>
  <metadata>
    <id>IronRose.Core</id>
    <version>0.1.0</version>
    <authors>IronRose Contributors</authors>
    <description>
      IronRose Game Engine - AI-Native .NET 10 Game Engine
    </description>
    <projectUrl>https://github.com/yourusername/IronRose</projectUrl>
    <license type="expression">MIT</license>
    <tags>game-engine gamedev unity ai dotnet</tags>
    <dependencies>
      <group targetFramework="net10.0">
        <dependency id="Veldrid" version="4.9.0" />
        <dependency id="Silk.NET.SDL" version="2.21.0" />
      </group>
    </dependencies>
  </metadata>
</package>
```

**플러그인 템플릿:**
```csharp
// IronRose.Plugin.Example/ExamplePlugin.cs
using IronRose.Engine;

namespace IronRose.Plugin.Example
{
    public class ExamplePlugin : IPlugin
    {
        public string Name => "Example Plugin";
        public string Version => "1.0.0";

        public void Initialize()
        {
            Debug.Log($"[{Name}] Initialized!");
        }

        public void Update(float deltaTime)
        {
            // 플러그인 업데이트 로직
        }
    }
}
```

**AI 프롬프트 템플릿 공유 플랫폼:**

**prompts/templates/player_controller.toml:**
```toml
[template]
name = "Player Controller"
description = "WASD로 움직이는 플레이어 컨트롤러"
author = "IronRose Team"
tags = ["player", "movement", "input"]

[prompt]
system = "Unity C# 스크립트를 생성해주세요."
user = """
플레이어 컨트롤러를 만들어주세요:
- WASD로 이동
- 속도: {speed}
- 점프 높이: {jumpHeight}
"""

[parameters]
speed = { type = "float", default = 5.0 }
jumpHeight = { type = "float", default = 2.0 }
```

---

## 검증 기준

✅ GitHub 저장소 공개 (MIT 라이선스)
✅ Discord 서버 개설 및 50명 이상 참여
✅ Reddit 커뮤니티 생성
✅ NuGet 패키지 배포 (IronRose.Core)
✅ 첫 번째 외부 기여자의 PR 머지

---

## 마케팅 전략

### 런칭 포스트 작성
- **Hacker News**: "Show HN: IronRose - AI-Native Game Engine in .NET 10"
- **Reddit**: r/gamedev, r/csharp, r/dotnet
- **Twitter**: #gamedev #dotnet #ai
- **YouTube**: 데모 영상

### 주간 업데이트
- 개발 블로그 포스팅
- Discord 공지
- Twitter 스레드

---

## 성장 목표 (6개월)

| 지표 | 목표 |
|------|------|
| GitHub Stars | 1,000+ |
| Discord 멤버 | 500+ |
| NuGet 다운로드 | 5,000+ |
| YouTube 조회수 | 50,000+ |
| 외부 기여자 | 20+ |

---

## 예상 소요 시간
**3-4일** (초기 설정)
**지속적** (커뮤니티 관리)

---

## 🎉 축하합니다!

IronRose 1.0 릴리스 준비가 완료되었습니다!

**다음 단계:**
- 지속적인 버그 수정
- 커뮤니티 피드백 반영
- 새로운 기능 추가 (Roadmap 2.0)

**Iron for Strength, Rose for Beauty** 🌹
