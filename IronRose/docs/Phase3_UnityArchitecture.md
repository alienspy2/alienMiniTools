# Phase 3: Unity Architecture 구현

## 목표
Unity의 GameObject/Component 아키텍처를 **있는 그대로** 구현합니다.
Shim(껍데기)이 아닌 실제 동작하는 엔진 구조입니다.

---

## 설계 철학

> **"Keep It Simple, Stupid (KISS)"**
>
> - ECS 변환 레이어 없음
> - 내부/외부 구조 분리 없음
> - Unity 아키텍처 그대로 구현
> - 성능 문제는 나중에 병목이 실제로 발생하면 최적화

> **플러그인 기반 핫 리로드**
>
> - 엔진은 안정적 기반으로 유지
> - 플러그인/LiveCode로 기능을 확장하고 핫 리로드
> - AI Digest로 검증된 코드를 엔진에 통합

---

## 작업 항목

### 3.1 기본 수학 타입 (IronRose.Engine)

**UnityEngine/Vector3.cs:**
```csharp
using System;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 zero => new(0, 0, 0);
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 up => new(0, 1, 0);
        public static Vector3 forward => new(0, 0, 1);
        public static Vector3 right => new(1, 0, 0);

        public float magnitude => MathF.Sqrt(x * x + y * y + z * z);
        public Vector3 normalized
        {
            get
            {
                float mag = magnitude;
                return mag > 0.00001f ? this / mag : zero;
            }
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) =>
            new(a.x + b.x, a.y + b.y, a.z + b.z);

        public static Vector3 operator -(Vector3 a, Vector3 b) =>
            new(a.x - b.x, a.y - b.y, a.z - b.z);

        public static Vector3 operator *(Vector3 a, float d) =>
            new(a.x * d, a.y * d, a.z * d);

        public static Vector3 operator /(Vector3 a, float d) =>
            new(a.x / d, a.y / d, a.z / d);

        public static float Dot(Vector3 a, Vector3 b) =>
            a.x * b.x + a.y * b.y + a.z * b.z;

        public static Vector3 Cross(Vector3 a, Vector3 b) =>
            new(
                a.y * b.z - a.z * b.y,
                a.z * b.x - a.x * b.z,
                a.x * b.y - a.y * b.x
            );

        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2})";
    }
}
```

**UnityEngine/Quaternion.cs:**
```csharp
using System;

namespace UnityEngine
{
    public struct Quaternion
    {
        public float x, y, z, w;

        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static Quaternion identity => new(0, 0, 0, 1);

        public static Quaternion Euler(float x, float y, float z)
        {
            // 간단한 오일러 각도 변환 (실제로는 더 복잡)
            float cx = MathF.Cos(x * 0.5f * MathF.PI / 180f);
            float sx = MathF.Sin(x * 0.5f * MathF.PI / 180f);
            float cy = MathF.Cos(y * 0.5f * MathF.PI / 180f);
            float sy = MathF.Sin(y * 0.5f * MathF.PI / 180f);
            float cz = MathF.Cos(z * 0.5f * MathF.PI / 180f);
            float sz = MathF.Sin(z * 0.5f * MathF.PI / 180f);

            return new Quaternion(
                sx * cy * cz - cx * sy * sz,
                cx * sy * cz + sx * cy * sz,
                cx * cy * sz - sx * sy * cz,
                cx * cy * cz + sx * sy * sz
            );
        }

        public static Quaternion operator *(Quaternion a, Quaternion b)
        {
            return new Quaternion(
                a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
                a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x,
                a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z
            );
        }

        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2}, {w:F2})";
    }
}
```

**UnityEngine/Color.cs:**
```csharp
namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;

        public Color(float r, float g, float b, float a = 1.0f)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static Color white => new(1, 1, 1, 1);
        public static Color black => new(0, 0, 0, 1);
        public static Color red => new(1, 0, 0, 1);
        public static Color green => new(0, 1, 0, 1);
        public static Color blue => new(0, 0, 1, 1);
    }
}
```

---

### 3.2 GameObject & Component 시스템

**Component.cs:**
```csharp
namespace UnityEngine
{
    public class Component
    {
        public GameObject gameObject { get; internal set; } = null!;
        public Transform transform { get; internal set; } = null!;
    }
}
```

**GameObject.cs:**
```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class GameObject
    {
        public string name;
        public Transform transform { get; private set; } = null!;
        private List<Component> _components = new();

        public GameObject(string name = "GameObject")
        {
            this.name = name;
            this.transform = AddComponent<Transform>();
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T();
            component.gameObject = this;
            component.transform = this.transform;
            _components.Add(component);

            // MonoBehaviour면 자동으로 씬에 등록
            if (component is MonoBehaviour mb)
            {
                SceneManager.RegisterBehaviour(mb);
            }

            return component;
        }

        public T? GetComponent<T>() where T : Component
        {
            return _components.OfType<T>().FirstOrDefault();
        }

        public T[] GetComponents<T>() where T : Component
        {
            return _components.OfType<T>().ToArray();
        }

        public static GameObject CreatePrimitive(PrimitiveType type)
        {
            var go = new GameObject($"Primitive_{type}");
            // TODO: Phase 4에서 MeshRenderer 추가
            return go;
        }
    }

    public enum PrimitiveType
    {
        Cube,
        Sphere,
        Cylinder,
        Plane
    }
}
```

**Transform.cs:**
```csharp
namespace UnityEngine
{
    public class Transform : Component
    {
        public Vector3 position = Vector3.zero;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;

        public void Translate(Vector3 translation)
        {
            position += translation;
        }

        public void Rotate(float x, float y, float z)
        {
            rotation *= Quaternion.Euler(x, y, z);
        }

        public void Rotate(Vector3 eulerAngles)
        {
            rotation *= Quaternion.Euler(eulerAngles.x, eulerAngles.y, eulerAngles.z);
        }
    }
}
```

---

### 3.3 MonoBehaviour 라이프사이클

**MonoBehaviour.cs:**
```csharp
namespace UnityEngine
{
    public class MonoBehaviour : Component
    {
        public virtual void Awake() { }
        public virtual void Start() { }
        public virtual void Update() { }
        public virtual void LateUpdate() { }
        public virtual void OnDestroy() { }
    }
}
```

---

### 3.4 씬 관리 및 업데이트 루프 (IronRose.Engine)

**SceneManager.cs:**
```csharp
using System.Collections.Generic;

namespace UnityEngine
{
    public static class SceneManager
    {
        private static List<MonoBehaviour> _behaviours = new();

        public static void RegisterBehaviour(MonoBehaviour behaviour)
        {
            _behaviours.Add(behaviour);
            behaviour.Awake();
            behaviour.Start();
        }

        public static void Update(float deltaTime)
        {
            Time.deltaTime = deltaTime;
            Time.time += deltaTime;

            // Update 호출
            foreach (var behaviour in _behaviours)
            {
                behaviour.Update();
            }

            // LateUpdate 호출
            foreach (var behaviour in _behaviours)
            {
                behaviour.LateUpdate();
            }
        }

        public static void Clear()
        {
            foreach (var behaviour in _behaviours)
            {
                behaviour.OnDestroy();
            }
            _behaviours.Clear();
        }
    }
}
```

**Time.cs:**
```csharp
namespace UnityEngine
{
    public static class Time
    {
        public static float deltaTime { get; internal set; }
        public static float time { get; internal set; }
        public static int frameCount { get; internal set; }
    }
}
```

---

### 3.5 디버그 유틸리티

**Debug.cs:**
```csharp
using System;

namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object message)
        {
            Console.WriteLine($"[LOG] {message}");
        }

        public static void LogWarning(object message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[WARN] {message}");
            Console.ResetColor();
        }

        public static void LogError(object message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] {message}");
            Console.ResetColor();
        }
    }
}
```

---

## 검증 기준

✅ Unity 스타일 스크립트 작성 가능:

```csharp
using UnityEngine;

public class RotatingCube : MonoBehaviour
{
    void Update()
    {
        transform.Rotate(0, Time.deltaTime * 45, 0);

        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"Rotation: {transform.rotation}");
        }
    }
}
```

✅ GameObject 생성 및 Component 추가 동작
✅ MonoBehaviour 라이프사이클 메서드 호출
✅ Time.deltaTime 정상 작동

---

## 예상 소요 시간
**4-5일**

---

## 다음 단계
→ [Phase 4: 기본 렌더링 파이프라인](Phase4_BasicRendering.md)
