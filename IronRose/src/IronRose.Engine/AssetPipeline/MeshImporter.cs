using Assimp;
using System.IO;
using RoseEngine;

namespace IronRose.AssetPipeline
{
    public class MeshImportResult
    {
        public RoseEngine.Mesh Mesh { get; set; } = null!;
        public RoseEngine.Material[] Materials { get; set; } = [];
    }

    public class MeshImporter
    {
        public MeshImportResult Import(string meshPath, float scale = 1.0f,
            bool generateNormals = true, bool flipUVs = true, bool triangulate = true)
        {
            if (!File.Exists(meshPath))
            {
                Debug.LogError($"Mesh file not found: {meshPath}");
                return null!;
            }

            var postProcess = PostProcessSteps.None;
            if (triangulate) postProcess |= PostProcessSteps.Triangulate;
            if (generateNormals) postProcess |= PostProcessSteps.GenerateNormals;
            if (flipUVs) postProcess |= PostProcessSteps.FlipUVs;

            var context = new AssimpContext();
            var scene = context.ImportFile(meshPath, postProcess);

            if (scene == null || scene.MeshCount == 0)
            {
                Debug.LogError($"Failed to load mesh: {meshPath}");
                return null!;
            }

            // 모든 메시를 하나로 병합
            // glTF는 오른손 좌표계(+Z = 뷰어 방향)를 사용하지만 Assimp 변환 후
            // X축이 반전되어 좌우 미러링이 발생함. X축 네게이트 + 와인딩 반전으로 보정.
            var allVertices = new List<Vertex>();
            var allIndices = new List<uint>();

            foreach (var assimpMesh in scene.Meshes)
            {
                uint baseVertex = (uint)allVertices.Count;

                bool hasUVs = assimpMesh.TextureCoordinateChannelCount > 0
                    && assimpMesh.TextureCoordinateChannels[0].Count > 0;

                for (int i = 0; i < assimpMesh.VertexCount; i++)
                {
                    var pos = assimpMesh.Vertices[i];
                    var normal = assimpMesh.HasNormals
                        ? assimpMesh.Normals[i]
                        : new Vector3D(0, 1, 0);
                    var uv = hasUVs
                        ? assimpMesh.TextureCoordinateChannels[0][i]
                        : new Vector3D(0, 0, 0);

                    allVertices.Add(new Vertex
                    {
                        Position = new Vector3(-pos.X * scale, pos.Y * scale, pos.Z * scale),
                        Normal = new Vector3(-normal.X, normal.Y, normal.Z),
                        UV = new Vector2(uv.X, uv.Y)
                    });
                }

                // X축 네게이트로 와인딩이 반전되므로 인덱스 순서를 뒤집어 front face 보정
                foreach (var face in assimpMesh.Faces)
                {
                    if (face.IndexCount == 3)
                    {
                        allIndices.Add(baseVertex + (uint)face.Indices[0]);
                        allIndices.Add(baseVertex + (uint)face.Indices[2]);
                        allIndices.Add(baseVertex + (uint)face.Indices[1]);
                    }
                }
            }

            var mesh = new RoseEngine.Mesh
            {
                vertices = allVertices.ToArray(),
                indices = allIndices.ToArray()
            };

            // Material 추출
            var materials = ExtractMaterials(scene, meshPath);

            Debug.Log($"Imported mesh: {meshPath} ({allVertices.Count} vertices, {allIndices.Count / 3} triangles, {materials.Length} materials)");

            return new MeshImportResult { Mesh = mesh, Materials = materials };
        }

        private RoseEngine.Material[] ExtractMaterials(Scene scene, string meshPath)
        {
            if (!scene.HasMaterials)
                return [];

            var meshDir = Path.GetDirectoryName(meshPath) ?? "";
            var materials = new List<RoseEngine.Material>();

            // Assimp이 GLB 임베디드 텍스처를 추출하지 못하는 경우를 위한 폴백
            GlbTextureExtractor.GlbTextures? glbTextures = null;
            if (!scene.HasTextures)
                glbTextures = GlbTextureExtractor.Extract(meshPath);

            int matIndex = 0;
            foreach (var assimpMat in scene.Materials)
            {
                var mat = new RoseEngine.Material();

                // Diffuse color
                if (assimpMat.HasColorDiffuse)
                {
                    var c = assimpMat.ColorDiffuse;
                    mat.color = new Color(c.R, c.G, c.B, c.A);
                }

                // Emissive color
                if (assimpMat.HasColorEmissive)
                {
                    var c = assimpMat.ColorEmissive;
                    mat.emission = new Color(c.R, c.G, c.B, c.A);
                }

                // PBR: metallic & roughness
                if (assimpMat.HasReflectivity)
                    mat.metallic = assimpMat.Reflectivity;
                if (assimpMat.HasShininess)
                    mat.roughness = 1.0f - MathF.Min(assimpMat.Shininess / 1000f, 1.0f);

                // Diffuse texture → mainTexture
                if (assimpMat.HasTextureDiffuse)
                    mat.mainTexture = LoadTexture(scene, assimpMat.TextureDiffuse, meshDir);

                // Assimp 실패 시 GLB 직접 파싱 폴백
                if (mat.mainTexture == null && glbTextures != null)
                    mat.mainTexture = LoadGlbBaseColorTexture(glbTextures, matIndex);

                // Normal map
                if (assimpMat.HasTextureNormal)
                    mat.normalMap = LoadTexture(scene, assimpMat.TextureNormal, meshDir);

                materials.Add(mat);
                matIndex++;
            }

            return materials.ToArray();
        }

        private static Texture2D? LoadGlbBaseColorTexture(GlbTextureExtractor.GlbTextures glbTextures, int materialIndex)
        {
            if (materialIndex >= glbTextures.MaterialBaseColorImageIndex.Count)
                return null;

            int imageIndex = glbTextures.MaterialBaseColorImageIndex[materialIndex];
            if (imageIndex < 0 || imageIndex >= glbTextures.Images.Count)
                return null;

            var imageData = glbTextures.Images[imageIndex];
            if (imageData.Length == 0)
                return null;

            Debug.Log($"[MeshImporter] Loading GLB embedded texture (image[{imageIndex}], {imageData.Length} bytes) for material[{materialIndex}]");
            return Texture2D.LoadFromMemory(imageData);
        }

        private Texture2D? LoadTexture(Scene scene, TextureSlot slot, string meshDir)
        {
            var filePath = slot.FilePath;
            if (string.IsNullOrEmpty(filePath))
                return null;

            try
            {
                // 임베디드 텍스처: "*0", "*1" 형태의 참조
                if (filePath.StartsWith("*") && int.TryParse(filePath.AsSpan(1), out int texIndex))
                {
                    if (scene.HasTextures && texIndex < scene.TextureCount)
                        return LoadEmbeddedTexture(scene.Textures[texIndex]);
                }

                // 외부 파일 텍스처: 메시 파일 기준 상대 경로
                var fullPath = Path.GetFullPath(Path.Combine(meshDir, filePath));
                if (File.Exists(fullPath))
                    return Texture2D.LoadFromFile(fullPath);

                // GLB 등 임베디드 텍스처: filePath가 파일명이지만 실제 파일이 없는 경우
                // 경로에서 숫자 인덱스를 추출하거나 순차 탐색으로 임베디드 텍스처 로드
                if (scene.HasTextures)
                {
                    var result = TryLoadEmbeddedByIndex(scene, filePath);
                    if (result != null)
                        return result;
                }

                Debug.LogWarning($"Texture not found: {fullPath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load texture '{filePath}': {ex.Message}");
            }

            return null;
        }

        private Texture2D? TryLoadEmbeddedByIndex(Scene scene, string filePath)
        {
            // filePath에서 숫자 추출 시도 (예: "texture_0.png" → 0, "2" → 2)
            int index = -1;
            var nameOnly = Path.GetFileNameWithoutExtension(filePath);

            // 끝에서부터 연속 숫자 추출
            int numStart = nameOnly.Length;
            while (numStart > 0 && char.IsDigit(nameOnly[numStart - 1]))
                numStart--;

            if (numStart < nameOnly.Length
                && int.TryParse(nameOnly.AsSpan(numStart), out int parsed)
                && parsed < scene.TextureCount)
            {
                index = parsed;
            }

            if (index >= 0)
            {
                Debug.Log($"Loading embedded texture [{index}] for '{filePath}'");
                return LoadEmbeddedTexture(scene.Textures[index]);
            }

            // 인덱스 추출 실패 시: 첫 번째 임베디드 텍스처를 폴백으로 사용
            if (scene.TextureCount > 0)
            {
                Debug.LogWarning($"Cannot resolve embedded index for '{filePath}', falling back to texture[0]");
                return LoadEmbeddedTexture(scene.Textures[0]);
            }

            return null;
        }

        private static Texture2D LoadEmbeddedTexture(EmbeddedTexture embedded)
        {
            if (embedded.IsCompressed)
                return Texture2D.LoadFromMemory(embedded.CompressedData);

            int w = embedded.Width;
            int h = embedded.Height;
            var data = new byte[w * h * 4];
            var texels = embedded.NonCompressedData;
            for (int i = 0; i < texels.Length; i++)
            {
                data[i * 4 + 0] = texels[i].R;
                data[i * 4 + 1] = texels[i].G;
                data[i * 4 + 2] = texels[i].B;
                data[i * 4 + 3] = texels[i].A;
            }
            return new Texture2D(w, h) { _pixelData = data };
        }
    }
}
