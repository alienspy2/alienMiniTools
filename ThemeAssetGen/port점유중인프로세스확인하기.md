# 포트 점유 중인 프로세스 확인 및 종료 방법 (Windows)

특정 포트(예: 7860)를 사용 중인 프로세스를 찾아내고, 어떤 명령어로 실행되었는지 확인한 뒤 종료하는 방법입니다.

## 1. 포트를 사용하는 프로세스 ID (PID) 찾기

터미널(PowerShell 또는 CMD)에서 아래 명령어를 실행합니다. `7860` 부분을 원하는 포트 번호로 변경하세요.

```powershell
netstat -ano | findstr :7860
```

**출력 예시:**
```text
  TCP    0.0.0.0:7860           0.0.0.0:0              LISTENING       22712
```
여기서 맨 오른쪽의 숫자(`22712`)가 **PID**입니다.

## 2. 해당 PID의 프로세스 정보 확인

### 간단한 정보 (프로세스 이름 등)
```powershell
tasklist /FI "PID eq 22712"
```

### 상세 실행 명령어 확인 (PowerShell)
어떤 스크립트나 인자로 실행되었는지 정확히 보려면 아래 명령어를 사용합니다. (`22712`를 위에서 찾은 PID로 변경)

```powershell
Get-CimInstance Win32_Process -Filter "ProcessId = 22712" | Select-Object -ExpandProperty CommandLine
```

## 3. 프로세스 강제 종료

확인한 프로세스를 종료하려면 `taskkill` 명령어를 사용합니다.

```powershell
taskkill /F /PID 22712
```
* `/F`: 강제 종료
* `/PID`: 종료할 프로세스 ID 지정
