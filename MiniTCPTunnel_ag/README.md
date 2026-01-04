# MiniTCPTunnel 사용 가이드

**MiniTCPTunnel**은 SSH의 Remote Port Forwarding 기능(역방향 터널링)만을 경량화하여 구현한 보안 TCP 터널링 도구입니다.

공인 IP를 가진 **서버**와, 내부망(NAT 뒤)에 있는 **클라이언트**를 연결하여, 외부에서 클라이언트 내부의 서비스(웹, SSH 등)에 접근할 수 있게 해줍니다.

---

## 1. 사전 준비

### 환경
- **Python 3.12+** 설치 필요
- Windows, Linux, macOS 지원

### 설치 (서버/클라이언트 공통)
모든 머신에서 소스코드를 다운로드한 후, 다음 절차를 따르세요.

1. **가상환경 생성 및 의존성 설치**
   ```bash
   # (방법 A) pip venv 사용 시
   python -m venv venv
   # Windows:
   .\venv\Scripts\activate
   # Linux/Mac:
   source venv/bin/activate
   
   pip install -r requirements.txt
   ```
   
   ```bash
   # (방법 B) Conda 사용 시
   conda create -n tunnel python=3.12
   conda activate tunnel
   pip install -r requirements.txt
   ```

---

## 2. 서버 설정 및 실행 (공인 IP 머신)

서버는 클라이언트의 접속을 대기하고, 터널 요청이 오면 특정 포트를 열어줍니다.

### 실행 방법
```bash
python main_server.py --port 9000
```
- `--port`: 클라이언트가 접속할 제어 채널 포트 (기본값: 9000)

### 클라이언트 키 등록
보안을 위해 **허용된 클라이언트 키**만 접속 가능합니다.
서버 실행 디렉토리의 `allowed_clients.txt` 파일에 클라이언트의 **Public Key (Hex string)**를 한 줄에 하나씩 추가해야 합니다.

```text
# allowed_clients.txt 예시
# #으로 시작하는 줄은 주석입니다.
c309d858927822e5c7d98cd1deeb79fd3c32dd7e4cff081637d6d170d363f991
```

> **참고:** 클라이언트 키는 아래 [클라이언트 설정] 단계에서 생성하고 확인할 수 있습니다.

---

## 3. 클라이언트 설정 및 실행 (내부망 머신)

클라이언트는 서버에 접속하여 터널을 생성합니다.

### 1) 내 키(Identity Key) 확인
클라이언트를 최초 1회 실행하거나, 제공된 스크립트를 통해 내 고유 키를 확인합니다.

```bash
# Windows
show_client_key.bat

# 또는 직접 실행
python generate_client_key.py
```
출력된 **CLIENT PUBLIC KEY**를 복사하여, **서버의 `allowed_clients.txt`에 추가**해주세요.

### 2) 접속 설정 (`client_config.json`)
`client_config.json` 파일을 열어 서버 주소와 터널 정보를 수정합니다.

```json
{
  "server_host": "203.0.113.10",    <-- 서버의 IP 주소로 변경
  "server_port": 9000,              <-- 서버 포트
  "tunnels": [
    {
      "id": "web-server",
      "remote_port": 8080,          <-- 서버에서 열릴 포트 (외부 접속용)
      "local_host": "127.0.0.1",    <-- 전달할 내부 목적지 IP
      "local_port": 80,             <-- 전달할 내부 목적지 포트
      "auto_start": true            <-- 접속 시 자동 실행 여부
    }
  ]
}
```

> **주의:** 최초 실행 시 `identity_private_key_hex` 필드가 자동으로 생성됩니다. 이 키를 삭제하면## 4. 클라이언트 (Client) 사용법

### 4.1. 실행
```bash
python main_client.py
```
클라이언트 GUI가 실행됩니다.

### 4.2. GUI 기능 및 사용 가이드
MiniTCPTunnel 클라이언트는 직관적인 GUI를 제공합니다.

1.  **서버 연결 (Connection)**:
    *   상단의 `Host`와 `Port`에 서버 정보를 입력합니다.
    *   `Connect` 버튼을 누르면 서버에 접속합니다.
    *   연결이 성공하면 상태 바에 `Connected`가 표시됩니다.
    *   **자동 재접속**: 네트워크가 끊기면 30초 카운트다운 후 자동으로 재접속을 시도합니다.

2.  **터널 관리 (Tunnels)**:
    *   **Add Tunnel**: 새로운 터널 설정을 추가합니다. (로컬 포트 -> 서버 포트 매핑)
    *   **Edit/Delete**: 생성된 터널 카드의 `Edit` 또는 `Del` 버튼을 눌러 수정/삭제할 수 있습니다.
    *   **Enable/Disable (체크박스)**: 각 터널 카드의 왼쪽 체크박스를 통해 해당 터널을 활성화(사용)할지 결정합니다.
    *   **Apply to Server**: 설정 변경 후 상단의 `Apply to Server` 버튼을 눌러야 서버에 반영됩니다. 
        *   체크된 터널은 서버에서 포트가 열립니다.
        *   체크 해제된 터널은 서버에서 포트가 닫힙니다.

3.  **트레이 아이콘 (System Tray)**:
    *   앱을 닫으면(`X` 버튼) 종료되지 않고 트레이로 최소화됩니다.
    *   트레이 아이콘을 더블 클릭하면 창이 다시 열립니다.
    *   완전 종료하려면 트레이 아이콘 우클릭 -> `Exit`를 선택하세요.

---

## 5. 문제 해결 (Troubleshooting)

### Q1. "Failed to open tunnel ... [WinError 10013]" 오류가 뜹니다.
*   **원인**: 서버 혹은 클라이언트 PC에서 해당 포트(예: 8081)가 이미 사용 중이거나, 권한이 없습니다.
*   **해결**:
    *   다른 프로그램이 해당 포트를 사용 중인지 확인하세요.
    *   해당 포트를 변경하여 다시 `Add` 및 `Apply` 하세요.

### Q2. 다른 PC에서 터널 서버로 접속이 안 됩니다. (Timeout / Connection Refused)
*   **원인**: **Windows 방화벽**이 외부 접속을 차단하고 있을 확률이 높습니다.
*   **해결**:
    *   서버 PC(`main_server.py` 실행 PC)에서 방화벽 포트를 열어야 합니다.
    *   동봉된 `open_firewall_ports.bat` 파일을 **관리자 권한**으로 실행하면 기본 포트(9000, 8080~8090)가 허용됩니다.
    *   또는 제어판 > Windows Defender 방화벽 > 고급 설정 > 인바운드 규칙에서 해당 포트(예: 8081)를 허용(Allow)하세요.

### Q3. "Bad signature" 오류
*   **원인**: 클라이언트의 Public Key가 서버의 `allowed_clients.txt`에 등록되지 않았습니다.
*   **해결**:
    *   클라이언트에서 `show_client_key.bat`를 실행하여 키를 복사합니다.
    *   서버의 `allowed_clients.txt`에 붙여넣고 서버를 재시작하세요.

---

## 6. 테스트 (Test Echo Server)
동봉된 `tests/test_http_echo` 폴더에는 테스트용 웹 서버가 포함되어 있습니다.

1.  **Echo Server 실행**: `cd tests/test_http_echo` -> `python server.py` (8000번 포트)
2.  **Tunnel Client 설정**: Local Host `localhost`, Local Port `8000`, Remote Port `8081` -> `Apply`
3.  **접속 테스트**: 브라우저에서 `http://<Tunnel_Server_IP>:8081` 접속
4.  채팅창이 뜨면 성공입니다.

---

## 7. 라이선스
MIT License
