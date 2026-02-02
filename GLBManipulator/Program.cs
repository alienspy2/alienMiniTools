using GLBManipulator.Core;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
        {
            PrintHelp();
            return 0;
        }

        string? inputPath = null;
        string? outputPath = null;
        int? simplifyCount = null;
        bool strip = false;
        string? extractTexturesDir = null;
        string? toObjPath = null;
        bool showInfo = false;

        // 인자 파싱
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o":
                case "--output":
                    if (i + 1 < args.Length) outputPath = args[++i];
                    break;
                case "-s":
                case "--simplify":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int count))
                        simplifyCount = count;
                    break;
                case "--strip":
                    strip = true;
                    break;
                case "-t":
                case "--extract-textures":
                    if (i + 1 < args.Length) extractTexturesDir = args[++i];
                    break;
                case "--to-obj":
                    if (i + 1 < args.Length) toObjPath = args[++i];
                    break;
                case "-i":
                case "--info":
                    showInfo = true;
                    break;
                default:
                    if (!args[i].StartsWith("-") && inputPath == null)
                        inputPath = args[i];
                    break;
            }
        }

        if (string.IsNullOrEmpty(inputPath))
        {
            Console.Error.WriteLine("오류: 입력 파일을 지정하세요.");
            PrintHelp();
            return 1;
        }

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"파일을 찾을 수 없습니다: {inputPath}");
            return 1;
        }

        try
        {
            Console.WriteLine($"로딩: {inputPath}");

            string ext = Path.GetExtension(inputPath).ToLowerInvariant();
            GlbData glbData;

            if (ext == ".obj")
            {
                glbData = ObjReader.Load(inputPath);
            }
            else if (ext == ".glb" || ext == ".gltf")
            {
                glbData = GlbReader.Load(inputPath);
            }
            else
            {
                Console.Error.WriteLine($"지원하지 않는 파일 형식: {ext}");
                Console.Error.WriteLine("지원 형식: .glb, .gltf, .obj");
                return 1;
            }

            Console.WriteLine($"메시 {glbData.Meshes.Count}개, 텍스처 {glbData.Textures.Count}개 로드됨");

            // 정보 출력
            if (showInfo)
            {
                PrintInfo(glbData);
            }

            // 텍스처 추출
            if (!string.IsNullOrEmpty(extractTexturesDir))
            {
                ExtractTextures(glbData, extractTexturesDir);
            }

            // 요소 제거
            if (strip)
            {
                Console.WriteLine("메시 외 요소 제거됨 (출력 시 메시만 포함)");
            }

            // 폴리곤 간소화
            if (simplifyCount.HasValue)
            {
                int targetPerMesh = simplifyCount.Value / Math.Max(1, glbData.Meshes.Count);
                for (int i = 0; i < glbData.Meshes.Count; i++)
                {
                    Console.WriteLine($"메시 {i} 간소화 중...");
                    glbData.Meshes[i] = MeshSimplifier.Simplify(glbData.Meshes[i], targetPerMesh);
                }
            }

            // OBJ 출력
            if (!string.IsNullOrEmpty(toObjPath))
            {
                Console.WriteLine($"OBJ 저장: {toObjPath}");
                ObjWriter.Save(glbData, toObjPath);
            }

            // GLB 출력
            if (!string.IsNullOrEmpty(outputPath))
            {
                Console.WriteLine($"GLB 저장: {outputPath}");
                GlbWriter.Save(glbData, outputPath);
            }

            Console.WriteLine("완료!");
            return 0;
        }
        catch (DllNotFoundException ex)
        {
            Console.Error.WriteLine("meshoptimizer.dll을 찾을 수 없습니다.");
            Console.Error.WriteLine("실행 파일과 같은 디렉토리에 meshoptimizer.dll을 배치하세요.");
            Console.Error.WriteLine($"상세: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"오류: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static void PrintHelp()
    {
        Console.WriteLine(@"GLBManipulator - 3D 파일 처리 CLI 도구

사용법: GLBManipulator <input> [옵션]

지원 입력 형식: .glb, .gltf, .obj

옵션:
  -o, --output <file>          출력 GLB 파일 경로
  -s, --simplify <count>       목표 폴리곤(삼각형) 수로 간소화
  --strip                      메시 외 요소(애니메이션, 스킨 등) 제거
  -t, --extract-textures <dir> 텍스처를 PNG로 추출
  --to-obj <file>              Wavefront OBJ로 변환
  -i, --info                   파일 정보 출력
  -h, --help                   도움말 출력

예시:
  GLBManipulator model.glb -i
  GLBManipulator model.glb -s 1000 -o simplified.glb
  GLBManipulator model.obj -s 1000 --to-obj simplified.obj
  GLBManipulator model.obj -o converted.glb
  GLBManipulator model.glb -t ./textures/
  GLBManipulator model.glb --to-obj model.obj
  GLBManipulator model.glb --strip -s 500 -o output.glb
");
    }

    static void PrintInfo(GlbData glbData)
    {
        Console.WriteLine("\n=== 3D 모델 정보 ===");
        int totalTriangles = 0;
        int totalVertices = 0;

        for (int i = 0; i < glbData.Meshes.Count; i++)
        {
            var mesh = glbData.Meshes[i];
            Console.WriteLine($"  메시 {i}: {mesh.TriangleCount} 삼각형, {mesh.VertexCount} 정점");
            Console.WriteLine($"    - 노말: {(mesh.Normals.Count > 0 ? "있음" : "없음")}");
            Console.WriteLine($"    - UV: {(mesh.TexCoords.Count > 0 ? "있음" : "없음")}");
            totalTriangles += mesh.TriangleCount;
            totalVertices += mesh.VertexCount;
        }

        Console.WriteLine($"\n  총합: {totalTriangles} 삼각형, {totalVertices} 정점");
        Console.WriteLine($"  텍스처: {glbData.Textures.Count}개");

        for (int i = 0; i < glbData.Textures.Count; i++)
        {
            var tex = glbData.Textures[i];
            Console.WriteLine($"    - {tex.Name}: {tex.Data.Length} bytes ({tex.MimeType})");
        }
        Console.WriteLine();
    }

    static void ExtractTextures(GlbData glbData, string dirPath)
    {
        if (!Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }

        for (int i = 0; i < glbData.Textures.Count; i++)
        {
            var tex = glbData.Textures[i];
            string ext = tex.MimeType switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/webp" => ".webp",
                _ => ".bin"
            };

            string fileName = string.IsNullOrEmpty(tex.Name) ? $"texture_{i}" : tex.Name;
            if (!fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                fileName += ext;
            }

            string filePath = Path.Combine(dirPath, fileName);
            File.WriteAllBytes(filePath, tex.Data);
            Console.WriteLine($"텍스처 추출: {filePath}");
        }
    }
}
