# MiniTCPTunnel 사용 가이드

MiniTCPTunnel은 SSH의 Remote Port Forwarding과 유사한 동작을 제공하는 보안 TCP 터널입니다.  
공인 IP가 있는 서버에 터널을 열고, NAT 환경의 클라이언트가 로컬 서비스를 서버 포트로 노출할 수 있습니다.

---

## 1. 사전 준비

### 환경
- Python 3.12+
- Windows / Linux / macOS

### 설치(서버/클라이언트 공통)
```bash
# (A) venv
python -m venv venv
# Windows
.\venv\Scripts\activate
# Linux/macOS
source venv/bin/activate
pip install -r requirements.txt
```

```bash
# (B) conda
conda create -n tunnel python=3.12
conda activate tunnel
pip install -r requirements.txt
```

---

## 2. 보안/키 구조 요약

- **서버 identity 키**: `server_identity_key.hex`
  - 서버가 처음 실행될 때 생성/저장됩니다.
  - 재시작해도 동일 공개키가 유지됩니다.
- **클라이언트 identity 키**: `client_config.json`의 `identity_private_key_hex`
  - 클라이언트 최초 실행 시 자동 생성되거나, 스크립트로 생성됩니다.
- **server_pub_key 핀닝**: `client_config.json`의 `server_pub_key`
  - 클라이언트는 서버 공개키가 일치할 때만 연결합니다.
- **allowed_clients.txt**
  - 서버가 허용하는 클라이언트 공개키 목록(한 줄에 하나).
- **단일 클라이언트 제한**
  - 동시에 하나의 제어 채널만 허용합니다.
  - 기존 연결이 살아있으면 추가 연결은 무시됩니다.
- **데이터 채널 HMAC 바인딩**
  - 제어 채널에서 전달된 HMAC 세션 키로 데이터 채널을 검증합니다.

---

## 3. 실제 서버 배포 절차 (중요)

### 3.1 서버 준비 (공인 IP 머신)
1) OS 사용자/권한 준비  
   - 서버 전용 계정을 권장합니다.
2) 방화벽 포트 열기  
   - 제어 포트(예: 9000)와 터널 포트(예: 8080~)를 열어야 합니다.

### 3.2 서버 설치
```bash
git clone <repo-url>
cd MiniTCPTunnel_ag
pip install -r requirements.txt
```

### 3.3 서버 키 고정 및 공개키 확인
서버를 한 번 실행해 `server_identity_key.hex`를 생성하고 공개키를 기록합니다.
```bash
python main_server.py --port 9000
```
로그에 다음과 같이 출력됩니다.
```
Server Identity Public Key (Pin in client config): <SERVER_PUBLIC_KEY_HEX>
```
이 값을 클라이언트의 `client_config.json`에 `server_pub_key`로 입력합니다.

### 3.4 클라이언트 공개키 등록
클라이언트에서 공개키를 생성/확인한 뒤 서버에 등록합니다.
```bash
# 클라이언트에서 실행
python generate_client_key.py
```
출력된 **CLIENT PUBLIC KEY**를 서버의 `allowed_clients.txt`에 한 줄씩 추가합니다.
```text
# allowed_clients.txt 예시
<CLIENT_PUBLIC_KEY_HEX>
```

### 3.5 서버 서비스 실행
운영에서는 백그라운드로 안정적으로 실행하는 것이 좋습니다.

#### 방법 A) 단순 실행(로그 파일)
```bash
python main_server.py --port 9000 > server.log 2>&1
```
`main_server.py`는 stdout으로 로그를 출력하므로 리다이렉션에 적합합니다.

#### 방법 B) systemd (Linux)
```ini
# /etc/systemd/system/minitcptunnel.service
[Unit]
Description=MiniTCP Tunnel Server
After=network.target

[Service]
User=tunnel
WorkingDirectory=/opt/MiniTCPTunnel_ag
ExecStart=/opt/MiniTCPTunnel_ag/venv/bin/python main_server.py --port 9000
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
```
```bash
sudo systemctl daemon-reload
sudo systemctl enable minitcptunnel
sudo systemctl start minitcptunnel
```

#### 방법 C) Windows 서비스 (Task Scheduler/NSSM)
- **Task Scheduler**: “시스템 시작 시 실행”으로 `python main_server.py --port 9000` 등록
- **NSSM**: `nssm install MiniTCPTunnel` 후 실행 파일/인자 설정

### 3.6 운영 체크리스트
- `server_identity_key.hex`는 **절대 삭제하지 말 것** (삭제 시 공개키가 변경됨)
- `allowed_clients.txt` 변경 후에는 서버 재시작 권장
- 로그 파일 용량 관리(로테이션) 필요
- 단일 클라이언트 정책이므로, 운영 중 다른 클라이언트 접속은 무시됨

### 3.7 업데이트 절차
1) 서버 중지  
2) 코드 업데이트 (git pull 등)  
3) `server_identity_key.hex` 유지 확인  
4) 서버 재시작

---

## 4. 클라이언트 설정 및 실행

### 4.1 client_config.json 예시
```json
{
  "server_host": "203.0.113.10",
  "server_port": 9000,
  "server_pub_key": "<SERVER_PUBLIC_KEY_HEX>",
  "tunnels": [
    {
      "id": "web-server",
      "remote_port": 8080,
      "local_host": "127.0.0.1",
      "local_port": 80,
      "auto_start": true
    }
  ]
}
```

### 4.2 실행
```bash
python main_client.py
```
GUI에서 `Connect` → `Apply` 순서로 터널을 적용합니다.

---

## 5. 테스트

### 5.1 Echo 서버 테스트
```bash
cd tests/test_http_echo
python server.py
```
클라이언트에서 터널을 생성한 뒤, 브라우저에서  
`http://<SERVER_IP>:<REMOTE_PORT>` 로 접근해 확인합니다.

### 5.2 E2E 프로세스 테스트
```bash
python tests/test_e2e_process.py
```
Windows에서 `conda run` 실행 시 인코딩 문제가 발생하면,  
환경 Python을 직접 실행하는 방식으로 테스트합니다.

---

## 6. 트러블슈팅

### Q1. "Failed to open tunnel ... [WinError 10013]"
- 원인: 포트 사용 중이거나 권한 문제
- 해결: 다른 포트 사용, 방화벽/권한 확인

### Q2. "Bad signature"
- 원인: `allowed_clients.txt`에 클라이언트 공개키 미등록
- 해결: 클라이언트 공개키를 서버에 등록 후 재시작

### Q3. "Server public key does NOT match expected key!"
- 원인: `server_pub_key`가 서버 공개키와 불일치
- 해결: 서버 로그에서 공개키를 다시 복사해 `client_config.json`에 반영

---

## 7. 라이선스
MIT License
