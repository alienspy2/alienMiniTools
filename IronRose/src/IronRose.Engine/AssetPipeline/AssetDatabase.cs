using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoseEngine;

namespace IronRose.AssetPipeline
{
    public class AssetDatabase
    {
        private readonly Dictionary<string, string> _guidToPath = new();
        private readonly Dictionary<string, object> _loadedAssets = new();
        private readonly MeshImporter _meshImporter = new();
        private readonly TextureImporter _textureImporter = new();
        private PrefabImporter? _prefabImporter;

        public int AssetCount => _guidToPath.Count;

        public void ScanAssets(string projectPath)
        {
            _guidToPath.Clear();

            if (!Directory.Exists(projectPath))
            {
                Debug.LogWarning($"Asset directory not found: {projectPath}");
                return;
            }

            // 에셋 파일 확장자
            string[] assetExtensions = [".glb", ".gltf", ".fbx", ".obj", ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".prefab"];

            foreach (var ext in assetExtensions)
            {
                var files = Directory.GetFiles(projectPath, $"*{ext}", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var meta = RoseMetadata.LoadOrCreate(file);
                    if (!string.IsNullOrEmpty(meta.guid))
                    {
                        _guidToPath[meta.guid] = file;
                    }
                }
            }

            Debug.Log($"[AssetDatabase] Scanned {_guidToPath.Count} assets in {projectPath}");
        }

        public string GetPathFromGuid(string guid)
        {
            return _guidToPath.TryGetValue(guid, out var path) ? path : string.Empty;
        }

        public string? GetGuidFromPath(string path)
        {
            foreach (var kvp in _guidToPath)
            {
                if (kvp.Value == path)
                    return kvp.Key;
            }
            return null;
        }

        public T? Load<T>(string path) where T : class
        {
            // 캐시된 MeshImportResult에서 Mesh 또는 Material 추출
            if (_loadedAssets.TryGetValue(path, out var cached))
            {
                if (cached is MeshImportResult result)
                {
                    if (typeof(T) == typeof(Mesh)) return result.Mesh as T;
                    if (typeof(T) == typeof(Material)) return result.Materials.FirstOrDefault() as T;
                }
                return cached as T;
            }

            var meta = RoseMetadata.LoadOrCreate(path);
            var importerType = meta.importer.TryGetValue("type", out var typeVal)
                ? typeVal?.ToString() ?? ""
                : "";

            object? asset = importerType switch
            {
                "MeshImporter" => ImportMesh(path, meta),
                "TextureImporter" => _textureImporter.Import(path),
                "PrefabImporter" => ImportPrefab(path),
                _ => null,
            };

            if (asset != null)
            {
                _loadedAssets[path] = asset;
            }

            // MeshImportResult에서 요청 타입 추출
            if (asset is MeshImportResult meshResult)
            {
                if (typeof(T) == typeof(Mesh)) return meshResult.Mesh as T;
                if (typeof(T) == typeof(Material)) return meshResult.Materials.FirstOrDefault() as T;
            }

            return asset as T;
        }

        public void Unload(string path)
        {
            if (_loadedAssets.TryGetValue(path, out var asset))
            {
                if (asset is IDisposable disposable)
                    disposable.Dispose();
                _loadedAssets.Remove(path);
            }
        }

        public void UnloadAll()
        {
            foreach (var asset in _loadedAssets.Values)
            {
                if (asset is IDisposable disposable)
                    disposable.Dispose();
            }
            _loadedAssets.Clear();
        }

        private MeshImportResult? ImportMesh(string path, RoseMetadata meta)
        {
            float scale = 1.0f;
            bool generateNormals = true;
            bool flipUVs = true;
            bool triangulate = true;

            if (meta.importer.TryGetValue("scale", out var scaleVal))
                scale = Convert.ToSingle(scaleVal);
            if (meta.importer.TryGetValue("generate_normals", out var gnVal) && gnVal is bool gn)
                generateNormals = gn;
            if (meta.importer.TryGetValue("flip_uvs", out var fuVal) && fuVal is bool fu)
                flipUVs = fu;
            if (meta.importer.TryGetValue("triangulate", out var triVal) && triVal is bool tri)
                triangulate = tri;

            return _meshImporter.Import(path, scale, generateNormals, flipUVs, triangulate);
        }

        private GameObject? ImportPrefab(string path)
        {
            _prefabImporter ??= new PrefabImporter(this);
            return _prefabImporter.LoadPrefab(path);
        }
    }
}
