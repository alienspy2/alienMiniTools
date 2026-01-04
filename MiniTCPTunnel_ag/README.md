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

> **주의:** 최초 실행 시 `identity_private_key_hex` 필드가 자동으로 생성됩니다. 이 키를 삭제하면 서버 등록을 다시 해야 합니다.

### 3) 실행
```bash
python main_client.py
```
- 실행하면 GUI 창이 뜨고(트레이 아이콘 포함), 설정된 서버로 자동 접속을 시도합니다.
- `auto_start`가 켜져 있다면 터널이 바로 활성화됩니다.
- 카드 목록의 **"Start/Stop"** 버튼으로 수동 제어도 가능합니다.

---

## 4. 구조 요약

1. **Client** 실행 및 Server 접속 (9000번 포트)
2. **Client**가 "내 로컬 80번을 Server의 8080번으로 연결해줘"라고 요청
3. **Server**가 8080번 포트 리스닝 시작
4. **외부 사용자**가 `ServerIP:8080` 접속
5. 트래픽 경로: `User` -> `Server(8080)` -> `Secure Tunnel` -> `Client` -> `Local Service(80)`

---

## 5. 문제 해결

- **접속 거부 (Connection Refused)**
  - 서버 방화벽에서 9000번 포트가 열려있는지 확인하세요.
- **인증 실패 (Auth Fail / Handshake Failed)**
  - 클라이언트 키가 서버의 `allowed_clients.txt`에 정확히 등록되었는지 확인하세요.
  - 리스트 갱신 후 서버를 재시작했는지 확인하세요.
- **터널 오픈 실패**
  - 서버에서 `remote_port` (예: 8080)가 이미 사용 중이거나 방화벽에 막혀있지 않은지 확인하세요.
