LiveCode에서 검증된 스크립트를 Demo 프로젝트(src/IronRose.Demo/)로 이동합니다.

## 절차

1. `LiveCode/` 디렉토리의 .cs 파일을 확인합니다.
2. 인자가 있으면 해당 파일만, 없으면 모든 .cs 파일을 대상으로 합니다.
3. 각 파일을 `src/IronRose.Demo/`로 복사합니다 (동일 이름 파일은 덮어씀).
4. `dotnet build`로 빌드를 검증합니다.
5. 빌드 성공 시 `LiveCode/`에서 해당 파일을 삭제합니다.
6. 빌드 실패 시 `src/IronRose.Demo/`에서 복사한 파일을 제거하고 원래 상태로 복원합니다.
7. 이동된 파일 목록을 보고합니다.

## 규칙

- LiveCode/에 .cs 파일이 없으면 "LiveCode에 digest할 파일이 없습니다"라고 알립니다.
- 빌드 실패 시 반드시 원래 상태로 복원합니다.
