# Phase 5: Unity 에셋 임포터

## 목표
Unity의 .unity (Scene), .prefab, .fbx, .png 파일을 로드할 수 있게 만듭니다.

---

## 작업 항목

### 5.1 YAML 파서 통합 (IronRose.AssetPipeline)

**YamlParser.cs:**
```csharp
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.IO;

namespace IronRose.AssetPipeline
{
    public class UnityYamlParser
    {
        private readonly IDeserializer _deserializer;

        public UnityYamlParser()
        {
            _deserializer = new DeserializerBuilder()
                .WithNamingConvention(PascalCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
        }

        public T Parse<T>(string yamlContent)
        {
            return _deserializer.Deserialize<T>(yamlContent);
        }

        public object Parse(string yamlContent)
        {
            return _deserializer.Deserialize(yamlContent);
        }
    }
}
```

**Unity Asset 구조:**
```csharp
public class UnityAsset
{
    public string guid { get; set; } = string.Empty;
    public long fileID { get; set; }
    public int type { get; set; }
}

public class UnityPrefab
{
    public Dictionary<string, object> GameObject { get; set; } = new();
    public Dictionary<string, object> Transform { get; set; } = new();
    public Dictionary<string, object>[] MonoBehaviour { get; set; } = Array.Empty<Dictionary<string, object>>();
}
```

---

### 5.2 .prefab 로더

**PrefabImporter.cs:**
```csharp
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace IronRose.AssetPipeline
{
    public class PrefabImporter
    {
        private readonly UnityYamlParser _yamlParser;
        private readonly AssetDatabase _assetDatabase;

        public PrefabImporter(AssetDatabase assetDatabase)
        {
            _yamlParser = new UnityYamlParser();
            _assetDatabase = assetDatabase;
        }

        public GameObject LoadPrefab(string prefabPath)
        {
            if (!File.Exists(prefabPath))
            {
                Debug.LogError($"Prefab not found: {prefabPath}");
                return null!;
            }

            string yamlContent = File.ReadAllText(prefabPath);

            // Unity YAML은 여러 문서로 구성됨 (--- 구분자)
            var documents = yamlContent.Split(new[] { "---" }, StringSplitOptions.RemoveEmptyEntries);

            GameObject rootObject = null!;
            Dictionary<long, Component> componentCache = new();

            foreach (var doc in documents)
            {
                if (doc.Contains("GameObject:"))
                {
                    rootObject = ParseGameObject(doc);
                }
                else if (doc.Contains("Transform:"))
                {
                    var transform = ParseTransform(doc);
                    if (rootObject != null && transform != null)
                    {
                        rootObject.transform.position = transform.position;
                        rootObject.transform.rotation = transform.rotation;
                        rootObject.transform.localScale = transform.localScale;
                    }
                }
                // MeshRenderer, MeshFilter 등 추가 파싱...
            }

            return rootObject;
        }

        private GameObject ParseGameObject(string yaml)
        {
            // 간단한 파싱 (실제로는 더 복잡)
            var go = new GameObject("Prefab");
            // YAML에서 이름 추출
            return go;
        }

        private TransformData? ParseTransform(string yaml)
        {
            // Transform 데이터 파싱
            return new TransformData
            {
                position = Vector3.zero,
                rotation = Quaternion.identity,
                localScale = Vector3.one
            };
        }
    }

    internal class TransformData
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 localScale;
    }
}
```

---

### 5.3 .fbx 메시 로더 (AssimpNet)

**MeshImporter.cs:**
```csharp
using Assimp;
using System.IO;
using UnityEngine;

namespace IronRose.AssetPipeline
{
    public class MeshImporter
    {
        public Mesh Import(string fbxPath)
        {
            if (!File.Exists(fbxPath))
            {
                Debug.LogError($"FBX file not found: {fbxPath}");
                return null!;
            }

            var context = new AssimpContext();
            var scene = context.ImportFile(fbxPath,
                PostProcessSteps.Triangulate |
                PostProcessSteps.GenerateNormals |
                PostProcessSteps.FlipUVs
            );

            if (scene == null || scene.MeshCount == 0)
            {
                Debug.LogError($"Failed to load FBX: {fbxPath}");
                return null!;
            }

            // 첫 번째 메시만 로드 (간단한 구현)
            var assimpMesh = scene.Meshes[0];
            var mesh = new Mesh();

            // Vertex 데이터 변환
            var vertices = new Vertex[assimpMesh.VertexCount];
            for (int i = 0; i < assimpMesh.VertexCount; i++)
            {
                var pos = assimpMesh.Vertices[i];
                var normal = assimpMesh.Normals[i];
                var uv = assimpMesh.TextureCoordinateChannels[0].Count > 0
                    ? assimpMesh.TextureCoordinateChannels[0][i]
                    : new Assimp.Vector3D(0, 0, 0);

                vertices[i] = new Vertex
                {
                    Position = new Vector3(pos.X, pos.Y, pos.Z),
                    Normal = new Vector3(normal.X, normal.Y, normal.Z),
                    UV = new Vector2(uv.X, uv.Y)
                };
            }

            // Index 데이터 변환
            var indices = new List<uint>();
            foreach (var face in assimpMesh.Faces)
            {
                if (face.IndexCount == 3)
                {
                    indices.Add((uint)face.Indices[0]);
                    indices.Add((uint)face.Indices[1]);
                    indices.Add((uint)face.Indices[2]);
                }
            }

            mesh.vertices = vertices;
            mesh.indices = indices.ToArray();

            Debug.Log($"Imported mesh: {assimpMesh.VertexCount} vertices, {indices.Count / 3} triangles");

            return mesh;
        }
    }
}
```

---

### 5.4 .png 텍스처 로더 (StbImageSharp)

**TextureImporter.cs:**
```csharp
using StbImageSharp;
using System.IO;
using Veldrid;

namespace IronRose.AssetPipeline
{
    public class Texture2D
    {
        public int width { get; internal set; }
        public int height { get; internal set; }
        public byte[] data { get; internal set; } = Array.Empty<byte>();

        internal Veldrid.Texture? veldridTexture { get; set; }
    }

    public class TextureImporter
    {
        public Texture2D Import(string pngPath)
        {
            if (!File.Exists(pngPath))
            {
                UnityEngine.Debug.LogError($"Texture not found: {pngPath}");
                return null!;
            }

            // StbImage로 이미지 로드
            using var stream = File.OpenRead(pngPath);
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            var texture = new Texture2D
            {
                width = image.Width,
                height = image.Height,
                data = image.Data
            };

            UnityEngine.Debug.Log($"Imported texture: {image.Width}x{image.Height}");

            return texture;
        }

        public void UploadToGPU(Texture2D texture, GraphicsDevice device)
        {
            var textureDesc = TextureDescription.Texture2D(
                (uint)texture.width,
                (uint)texture.height,
                1, 1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled
            );

            texture.veldridTexture = device.ResourceFactory.CreateTexture(textureDesc);
            device.UpdateTexture(
                texture.veldridTexture,
                texture.data,
                0, 0, 0,
                (uint)texture.width,
                (uint)texture.height,
                1,
                0, 0
            );
        }
    }
}
```

---

### 5.5 AssetDatabase (GUID 매핑)

**AssetDatabase.cs:**
```csharp
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IronRose.AssetPipeline
{
    public class AssetDatabase
    {
        private Dictionary<string, string> _guidToPath = new();
        private Dictionary<string, object> _loadedAssets = new();

        public void ScanAssets(string projectPath)
        {
            var metaFiles = Directory.GetFiles(projectPath, "*.meta", SearchOption.AllDirectories);

            foreach (var metaFile in metaFiles)
            {
                var guid = ExtractGuidFromMeta(metaFile);
                var assetPath = metaFile.Replace(".meta", "");

                if (!string.IsNullOrEmpty(guid))
                {
                    _guidToPath[guid] = assetPath;
                }
            }

            Debug.Log($"Scanned {_guidToPath.Count} assets");
        }

        private string ExtractGuidFromMeta(string metaPath)
        {
            var lines = File.ReadAllLines(metaPath);
            foreach (var line in lines)
            {
                if (line.StartsWith("guid:"))
                {
                    return line.Replace("guid:", "").Trim();
                }
            }
            return string.Empty;
        }

        public string GetPathFromGuid(string guid)
        {
            return _guidToPath.TryGetValue(guid, out var path) ? path : string.Empty;
        }

        public T Load<T>(string path) where T : class
        {
            if (_loadedAssets.TryGetValue(path, out var cached))
            {
                return (T)cached;
            }

            // 타입에 따라 적절한 임포터 사용
            object asset = null!;

            if (path.EndsWith(".fbx"))
            {
                var importer = new MeshImporter();
                asset = importer.Import(path);
            }
            else if (path.EndsWith(".png"))
            {
                var importer = new TextureImporter();
                asset = importer.Import(path);
            }
            // ... 다른 타입들

            if (asset != null)
            {
                _loadedAssets[path] = asset;
            }

            return (T)asset;
        }
    }
}
```

---

## 검증 기준

✅ Unity에서 만든 Cube.prefab을 IronRose에서 로드하여 렌더링
✅ FBX 모델 + 텍스처 적용 가능
✅ .meta 파일에서 GUID 추출 및 매핑 정상 작동

---

## 테스트 시나리오

1. Unity에서 간단한 큐브 Prefab 생성
2. IronRose로 Prefab 파일 복사
3. `AssetDatabase.Load<GameObject>("Cube.prefab")` 호출
4. 로드된 GameObject가 화면에 렌더링됨

---

## 예상 소요 시간
**4-5일**

---

## 다음 단계
→ [Phase 6: 물리 엔진 통합](Phase6_PhysicsEngine.md)
