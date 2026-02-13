# Phase 4: 기본 렌더링 파이프라인

## 목표
Veldrid를 사용하여 3D 메시를 화면에 그리는 기본 Forward Rendering을 구현합니다.

---

## 작업 항목

### 4.1 메시 렌더링 시스템

**Mesh.cs (IronRose.Engine):**
```csharp
using Veldrid;

namespace UnityEngine
{
    public struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 UV;
    }

    public class Mesh
    {
        public Vertex[] vertices { get; set; } = Array.Empty<Vertex>();
        public uint[] indices { get; set; } = Array.Empty<uint>();

        internal DeviceBuffer? VertexBuffer { get; set; }
        internal DeviceBuffer? IndexBuffer { get; set; }

        public void UploadToGPU(GraphicsDevice device)
        {
            // Vertex Buffer 생성
            VertexBuffer = device.ResourceFactory.CreateBuffer(
                new BufferDescription((uint)(vertices.Length * System.Runtime.InteropServices.Marshal.SizeOf<Vertex>()),
                BufferUsage.VertexBuffer)
            );
            device.UpdateBuffer(VertexBuffer, 0, vertices);

            // Index Buffer 생성
            IndexBuffer = device.ResourceFactory.CreateBuffer(
                new BufferDescription((uint)(indices.Length * sizeof(uint)),
                BufferUsage.IndexBuffer)
            );
            device.UpdateBuffer(IndexBuffer, 0, indices);
        }
    }
}
```

**MeshRenderer.cs:**
```csharp
namespace UnityEngine
{
    public class MeshRenderer : Component
    {
        public Mesh? mesh { get; set; }
        public Material? material { get; set; }
    }
}
```

**Material.cs (간단한 구현):**
```csharp
namespace UnityEngine
{
    public class Material
    {
        public Color color { get; set; } = Color.white;
        // TODO: 텍스처, 셰이더 등 추가
    }
}
```

---

### 4.2 기본 셰이더 (GLSL → SPIR-V)

**Shaders/vertex.glsl:**
```glsl
#version 450

layout(location = 0) in vec3 Position;
layout(location = 1) in vec3 Normal;
layout(location = 2) in vec2 UV;

layout(set = 0, binding = 0) uniform WorldBuffer
{
    mat4 World;
};

layout(set = 0, binding = 1) uniform ViewBuffer
{
    mat4 View;
    mat4 Projection;
};

layout(location = 0) out vec3 fsin_Normal;
layout(location = 1) out vec2 fsin_UV;

void main()
{
    vec4 worldPos = World * vec4(Position, 1.0);
    gl_Position = Projection * View * worldPos;

    fsin_Normal = mat3(World) * Normal;
    fsin_UV = UV;
}
```

**Shaders/fragment.glsl:**
```glsl
#version 450

layout(location = 0) in vec3 fsin_Normal;
layout(location = 1) in vec2 fsin_UV;

layout(location = 0) out vec4 fsout_Color;

layout(set = 0, binding = 2) uniform MaterialBuffer
{
    vec4 Color;
};

void main()
{
    // 간단한 램버트 조명
    vec3 lightDir = normalize(vec3(0.5, 1.0, 0.3));
    float ndotl = max(dot(normalize(fsin_Normal), lightDir), 0.2);

    fsout_Color = vec4(Color.rgb * ndotl, Color.a);
}
```

**ShaderCompiler.cs (IronRose.Rendering):**
```csharp
using Veldrid;
using Veldrid.SPIRV;
using System.IO;
using System.Text;

namespace IronRose.Rendering
{
    public static class ShaderCompiler
    {
        public static (Shader vs, Shader fs) CompileShaders(
            GraphicsDevice device,
            string vertexPath,
            string fragmentPath)
        {
            string vertexCode = File.ReadAllText(vertexPath);
            string fragmentCode = File.ReadAllText(fragmentPath);

            var vertexShaderDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(vertexCode),
                "main"
            );

            var fragmentShaderDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(fragmentCode),
                "main"
            );

            var shaders = device.ResourceFactory.CreateFromSpirv(
                vertexShaderDesc,
                fragmentShaderDesc
            );

            return (shaders[0], shaders[1]);
        }
    }
}
```

---

### 4.3 카메라 시스템

**Camera.cs:**
```csharp
using System;
using System.Numerics;

namespace UnityEngine
{
    public class Camera : Component
    {
        public float fieldOfView = 60f;
        public float nearClipPlane = 0.1f;
        public float farClipPlane = 1000f;
        public float aspectRatio = 16f / 9f;

        public Matrix4x4 GetViewMatrix()
        {
            var pos = transform.position;
            var target = pos + new Vector3(0, 0, 1); // Forward
            var up = new Vector3(0, 1, 0);

            return Matrix4x4.CreateLookAt(
                new System.Numerics.Vector3(pos.x, pos.y, pos.z),
                new System.Numerics.Vector3(target.x, target.y, target.z),
                new System.Numerics.Vector3(up.x, up.y, up.z)
            );
        }

        public Matrix4x4 GetProjectionMatrix()
        {
            return Matrix4x4.CreatePerspectiveFieldOfView(
                fieldOfView * MathF.PI / 180f,
                aspectRatio,
                nearClipPlane,
                farClipPlane
            );
        }

        public static Camera main { get; internal set; } = null!;
    }
}
```

---

### 4.4 큐브 프리미티브 생성

**PrimitiveGenerator.cs (IronRose.Engine):**
```csharp
using System;

namespace UnityEngine
{
    public static class PrimitiveGenerator
    {
        public static Mesh CreateCube()
        {
            var mesh = new Mesh();

            mesh.vertices = new Vertex[]
            {
                // Front face
                new Vertex { Position = new Vector3(-0.5f, -0.5f,  0.5f), Normal = new Vector3(0, 0, 1), UV = new Vector2(0, 0) },
                new Vertex { Position = new Vector3( 0.5f, -0.5f,  0.5f), Normal = new Vector3(0, 0, 1), UV = new Vector2(1, 0) },
                new Vertex { Position = new Vector3( 0.5f,  0.5f,  0.5f), Normal = new Vector3(0, 0, 1), UV = new Vector2(1, 1) },
                new Vertex { Position = new Vector3(-0.5f,  0.5f,  0.5f), Normal = new Vector3(0, 0, 1), UV = new Vector2(0, 1) },
                // ... (나머지 5개 면 추가)
            };

            mesh.indices = new uint[]
            {
                // Front
                0, 1, 2, 0, 2, 3,
                // ... (나머지 5개 면의 인덱스)
            };

            return mesh;
        }

        public static GameObject CreatePrimitiveCube()
        {
            var go = new GameObject("Cube");
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.mesh = CreateCube();
            renderer.material = new Material { color = new Color(1.0f, 0.5f, 0.2f) };
            return go;
        }
    }
}
```

---

### 4.5 렌더링 파이프라인 통합

**RenderSystem.cs (IronRose.Rendering):**
```csharp
using Veldrid;
using UnityEngine;
using System.Numerics;
using System.Collections.Generic;

namespace IronRose.Rendering
{
    public class RenderSystem
    {
        private GraphicsDevice _device;
        private CommandList _commandList;
        private Pipeline _pipeline = null!;
        private ResourceSet _resourceSet = null!;

        private DeviceBuffer _worldBuffer = null!;
        private DeviceBuffer _viewBuffer = null!;
        private DeviceBuffer _materialBuffer = null!;

        public void Initialize(GraphicsDevice device)
        {
            _device = device;
            _commandList = device.ResourceFactory.CreateCommandList();

            CreateBuffers();
            CreatePipeline();
        }

        public void Render(Camera camera, List<MeshRenderer> renderers)
        {
            _commandList.Begin();
            _commandList.SetFramebuffer(_device.SwapchainFramebuffer);
            _commandList.ClearColorTarget(0, new RgbaFloat(0.2f, 0.3f, 0.4f, 1.0f));
            _commandList.ClearDepthStencil(1f);

            _commandList.SetPipeline(_pipeline);

            // View/Projection 업데이트
            UpdateViewBuffer(camera);

            foreach (var renderer in renderers)
            {
                if (renderer.mesh == null) continue;

                // World 행렬 업데이트
                UpdateWorldBuffer(renderer.transform);
                UpdateMaterialBuffer(renderer.material);

                _commandList.SetGraphicsResourceSet(0, _resourceSet);
                _commandList.SetVertexBuffer(0, renderer.mesh.VertexBuffer);
                _commandList.SetIndexBuffer(renderer.mesh.IndexBuffer, IndexFormat.UInt32);
                _commandList.DrawIndexed((uint)renderer.mesh.indices.Length);
            }

            _commandList.End();
            _device.SubmitCommands(_commandList);
            _device.SwapBuffers();
        }

        private void CreateBuffers()
        {
            _worldBuffer = _device.ResourceFactory.CreateBuffer(
                new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic)
            );
            _viewBuffer = _device.ResourceFactory.CreateBuffer(
                new BufferDescription(128, BufferUsage.UniformBuffer | BufferUsage.Dynamic)
            );
            _materialBuffer = _device.ResourceFactory.CreateBuffer(
                new BufferDescription(16, BufferUsage.UniformBuffer | BufferUsage.Dynamic)
            );
        }

        // ... UpdateWorldBuffer, UpdateViewBuffer 등 구현 ...
    }
}
```

---

## 검증 기준

✅ 3D 주황색 큐브가 화면 중앙에 렌더링됨
✅ 카메라를 이동하면 큐브의 시점이 변경됨
✅ 큐브가 회전하는 스크립트 작성 가능:

```csharp
public class RotatingCube : MonoBehaviour
{
    void Update()
    {
        transform.Rotate(0, Time.deltaTime * 45, 0);
    }
}
```

---

## 예상 소요 시간
**5-6일**

---

## 다음 단계
→ [Phase 5: Unity 에셋 임포터](Phase5_AssetImporter.md)
