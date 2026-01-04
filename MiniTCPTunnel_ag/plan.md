# 구현 계획 (MiniTCPTunnel)

## 1) 목표/범위
- SSH remote tunneling 기능만 분리한 TCP 터널 구현
- 서버는 공인 IP에서 제어 채널 + 외부 공개 포트 운영
- 클라이언트는 서버에 접속해 내부 LAN 대상 호스트로 프록시
- Ed25519로 상호 식별, X25519 공유 비밀 + HKDF로 세션 키/논스 파생
- 모든 프레임 LZ4 압축 후 ChaCha20-Poly1305(IETF) 암호화
- 서버는 동시에 1개의 클라이언트만 허용
- 보안 인증: 클라이언트에서 Identity Key(Ed25519)를 생성하고, 공개키를 서버에 수동으로 등록(White-list)해야 접속 가능하다.
- 클라이언트는 다중 터널 구성/관리 + GUI + 트레이 아이콘 제공

---

## 2) 기술 스택/라이브러리

### 공통
- Python 3.12+
- 비동기 I/O: `asyncio`
- 로깅: `logging` (표준 라이브러리)
- 설정: `pydantic-settings` (추천) 또는 `json`, `yaml`
- 패키지 관리: `pip`, `venv`

### 암호/압축
- Ed25519: `PyNaCl` (libsodium의 파이썬 바인딩)
- X25519 + ChaCha20-Poly1305 + HKDF: `cryptography`
- LZ4: `lz4`

### GUI
- PySide6 (Qt for Python): 크로스플랫폼 GUI 프레임워크
- 구동: `asyncio`와 Qt 이벤트 루프 통합 (`qasync` 라이브러리 활용)
- 트레이 아이콘: `QSystemTrayIcon`

---

## 3) 아키텍처 개요

### 프로젝트 구조
- `mini_tcp_tunnel/`
  - `shared/`: 프로토콜, 프레이밍, 압축/암호, 공통 유틸, 설정 모델
  - `server/`: 제어 채널 수락, 포트 리스너 관리, 연결 매칭, 통계 수집
  - `client/`: 제어 채널 유지, 터널 관리, 데이터 채널 생성
    - `ui/`: PySide6 기반 GUI 소스 (Widgets/QSS)
  - `main_server.py`: 서버 실행 진입점
  - `main_client.py`: 클라이언트 실행 진입점

### 설계 원칙
- 단일 책임: 네트워크, 프로토콜, UI 분리
- 비동기 파이프라인: 모든 I/O는 `asyncio` 기반 비차단 모드
- 제어 채널과 데이터 채널 분리 (멀티플렉싱 대신 “1-데이터-채널/1-외부-연결”)

---

## 4) 프로토콜 설계

### 4.1 채널 구분
- Control Channel: 클라이언트가 서버로 항상 유지하는 제어 연결
- Data Channel: 외부 접속 1건당 클라이언트가 서버로 별도 연결 생성

### 4.2 핸드셰이크/키 교환
1) 양측은 Ed25519 identity key 보유
   - 클라이언트: 최초 실행 시 또는 요청 시 자신의 키 쌍을 생성
   - 서버: 허용된 클라이언트의 Ed25519 공개키 목록을 설정 파일 등에 수동으로 등록하여 유지
2) 연결 시 `Hello` 교환
   - `protocol_version`
   - `role` (server/client)
   - `identity_public_key (Ed25519)`
   - `ephemeral_public_key (X25519)`
   - `nonce`
   - `signature = Sign(identity_priv, transcript)`
3) 상대 공개키/서명 검증
4) X25519 ECDH로 공유 비밀 생성
5) `HKDF-SHA256`으로 세션 키/논스 파생
   - `key_c2s`, `key_s2c`, `nonce_base_c2s`, `nonce_base_s2c`
6) 이후 모든 프레임은 LZ4 압축 후 ChaCha20-Poly1305로 암호화

### 4.3 프레임 형식 (공통)
```
| len (u32 BE) | ciphertext (len bytes) |
```
- `ciphertext = AEAD_Encrypt(key, nonce, compressed_plaintext, aad)`
- `compressed_plaintext = LZ4(plaintext)`
- `plaintext`:
```
| type (u8) | flags (u8) | stream_id (u32 BE) | payload (n bytes) |
```
- `nonce = nonce_base (4 bytes) + counter (8 bytes BE)`
- `counter`는 채널/방향별 64-bit 증가

### 4.4 메시지 타입 (Control)
- `Hello`, `AuthOk`, `AuthFail`
- `OpenTunnel` (client -> server)
- `CloseTunnel` (client -> server)
- `TunnelStatus` (server -> client)
- `IncomingConn` (server -> client)
  - payload: `tunnel_id`, `conn_id`
- `DataConnReady` (client -> server)
  - payload: `conn_id`
- `Heartbeat` (양방향)
- `Error`

### 4.5 데이터 채널 흐름
- Data Channel은 동일한 프레임 포맷 사용
- 데이터는 `Data` 프레임으로 송수신

---

## 5) 실행 흐름

### 5.1 서버 시작
1) 설정 로드 (port, allowed_keys, etc)
2) Control Listener 시작
3) 클라이언트 접속 시 핸드셰이크/인증
4) 인증 성공 후 tunnel 요청 대기

### 5.2 클라이언트 시작
1) 설정 로드 (server_addr, tunnels, identity_key)
2) Control Channel 연결 + 핸드셰이크
3) `OpenTunnel`로 서버에 포트 오픈 요청
4) Heartbeat 루프 시작

### 5.3 외부 접속 발생
1) 서버의 public port에 외부 클라이언트 접속
2) 서버가 `IncomingConn(tunnel_id, conn_id)` 전송
3) 클라이언트가 `Data Channel` 새 연결 생성
4) `DataConnReady(conn_id)` 전송
5) 서버가 외부 연결과 Data Channel을 매칭
6) 양방향 데이터 프록시 (Data 프레임 송수신)

### 5.4 Heartbeat/재접속
- 일정 주기 (예: 30초)마다 Heartbeat
- 응답 타임아웃 시 Control Channel 재연결
- 재연결 시 기존 터널 자동 복구

---

## 6) 서버 구성/동작 상세

### 핵심 컴포넌트
- `ControlSessionManager`: 단일 클라이언트 인증 및 세션 유지
- `TunnelRegistry`: 열린 public port <-> tunnel_id 매핑 및 `TcpListener` 관리
- `ConnectionPairer`: `conn_id` 기준으로 외부 연결과 Data Channel 매칭
- `Metrics`: 터널별 통계 및 실시간 연결 수 집계

---

## 7) 클라이언트 구성/동작 상세

### 핵심 컴포넌트
- `ControlClient`: 핸드셰이크, heartbeat, 재연결 로직
- `TunnelManager`: 다중 터널 설정 관리
- `LocalProxy`: `tunnel_id` 대응 로컬 호스트 연결 및 데이터 릴레이
- `StateStore`: UI 표시를 위한 실시간 상태 객체

---

## 8) GUI/UX 계획
- PySide6 기반 현대적 디자인
- 메인 화면: 터널 리스트(카드 형태), 연결 상태, 실시간 접속자 수
- 트레이 아이콘 (System Tray):
  - 닫기 시 트레이로 최소화
  - 메뉴: Show/Hide, Quit, 빠른 터널 제어

---

## 9) 단계별 구현 순서

### Phase 1: 개발 환경 준비
- Python 3.12 venv 생성 및 의존성 (`PySide6`, `cryptography`, `PyNaCl`, `lz4`, `qasync`) 설치
- 기본 패키지 구조 생성

### Phase 2: Shared 프로토콜
- 프레임 인코더/디코더 (`asyncio.StreamReader/Writer` util)
- 암호화/압축 래퍼 (ChaCha20-Poly1305, LZ4)
- 키 교환 로직 (X25519 + HKDF)

### Phase 3: Server
- Control Channel 핸들러 및 인증
- 인증 후 포트 리스닝 및 연결 페어링 로직

### Phase 4: Client Core
- Control Client 및 Heartbeat/Retry
- Local 호스트로의 프록시 릴레이

### Phase 5: GUI (PySide6)
- QSystemTrayIcon 연동
- 터널 상태 대시보드 및 설정 관리 화면

### Phase 6: 통합 및 검증
- 로컬 루프백 테스트 및 배포 패키징 (PyInstaller 등) 고려

---

## 10) 실행 예시 (로컬)
```bash
# 서버
python main_server.py --port 9000

# 클라이언트
python main_client.py --config config.json
```

---

## 11) 리스크/결정 포인트
- **Qt/Asyncio 통합:** `qasync` 등을 활용해 UI 프리징 없이 네트워크 I/O 동시 처리
- **배포:** Python 인터프리터 없이 실행 가능한 단일 파일 빌드 (PyInstaller, Nuitka 등)
- **성능:** Python의 Global Interpreter Lock(GIL) 영향 확인 (I/O 위주라 영향은 적을 것으로 예상)

