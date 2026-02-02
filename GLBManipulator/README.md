# GLBManipulator

3D 파일을 처리하는 CLI 도구입니다.

## 기능

- **다양한 입력 형식**: GLB, GLTF, OBJ 파일 입력 지원
- **폴리곤 간소화**: MeshOptimizer를 사용하여 UV(텍스처 좌표)를 유지하면서 메시 단순화
- **텍스처 추출**: GLB 내장 텍스처를 PNG/JPG 파일로 추출
- **OBJ 변환**: Wavefront OBJ 포맷으로 변환
- **GLB 변환**: OBJ를 GLB 포맷으로 변환
- **요소 제거**: 애니메이션, 스킨 등 메시 외 요소 제거

## 설치

### 요구사항
- .NET 8.0 SDK

### 빌드
```bash
dotnet build -c Release
```

빌드된 실행 파일은 `bin/Release/net8.0/` 디렉토리에 생성됩니다.

## 사용법

```
GLBManipulator <input> [옵션]
```

### 지원 입력 형식
- `.glb` - GLB (Binary glTF)
- `.gltf` - glTF
- `.obj` - Wavefront OBJ

### 옵션

| 옵션 | 설명 |
|------|------|
| `-o, --output <file>` | 출력 GLB 파일 경로 |
| `-s, --simplify <count>` | 목표 폴리곤(삼각형) 수로 간소화 |
| `--strip` | 메시 외 요소(애니메이션, 스킨 등) 제거 |
| `-t, --extract-textures <dir>` | 텍스처를 PNG로 추출 |
| `--to-obj <file>` | Wavefront OBJ로 변환 |
| `-i, --info` | 파일 정보 출력 |
| `-h, --help` | 도움말 출력 |

### 예시

```bash
# GLB 파일 정보 확인
GLBManipulator model.glb -i

# OBJ 파일 정보 확인
GLBManipulator model.obj -i

# 1000 폴리곤으로 간소화
GLBManipulator model.glb -s 1000 -o simplified.glb

# OBJ 파일 간소화 후 OBJ로 저장
GLBManipulator model.obj -s 1000 --to-obj simplified.obj

# OBJ를 GLB로 변환
GLBManipulator model.obj -o converted.glb

# 텍스처 추출
GLBManipulator model.glb -t ./textures/

# OBJ로 변환
GLBManipulator model.glb --to-obj model.obj

# 조합 사용 (요소 제거 + 간소화 + 저장)
GLBManipulator model.glb --strip -s 500 -o output.glb
```

## 폴리곤 간소화 알고리즘

[MeshOptimizer](https://github.com/zeux/meshoptimizer) 라이브러리의 `meshopt_simplifyWithAttributes` 함수를 사용합니다.

- **QEM (Quadric Error Metrics)** 기반 알고리즘
- UV 좌표를 attribute로 전달하여 텍스처 매핑 보존
- 경계 엣지 고정(Lock Border) 옵션으로 메시 경계 유지

## 프로젝트 구조

```
GLBManipulator/
├── Program.cs              # CLI 엔트리 포인트
├── Core/
│   ├── MeshData.cs         # 메시 데이터 구조체
│   ├── GlbReader.cs        # GLB 읽기 (SharpGLTF)
│   ├── GlbWriter.cs        # GLB 쓰기
│   ├── ObjReader.cs        # OBJ 읽기 (AssimpNet)
│   ├── MeshSimplifier.cs   # 폴리곤 간소화
│   └── ObjWriter.cs        # OBJ 출력
├── Native/
│   └── MeshOptimizer.cs    # P/Invoke 선언
└── libs/
    └── meshoptimizer.dll   # MeshOptimizer 네이티브 DLL
```

## 의존성

| 패키지 | 버전 | 용도 |
|--------|------|------|
| SharpGLTF.Core | 1.0.6 | GLB 파싱 |
| SharpGLTF.Toolkit | 1.0.6 | GLB 생성 |
| AssimpNet | 4.1.0 | OBJ 파싱 |
| meshoptimizer.dll | - | 메시 간소화 (네이티브) |

## 라이선스

MIT License
