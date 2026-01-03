# 구현 계획 (MiniTCPTunnel)

## 1) 목표/범위
- SSH remote tunneling 기능만 분리한 TCP 터널 구현
- 서버는 공인 IP에서 제어 채널 + 외부 공개 포트 운영
- 클라이언트는 서버에 접속해 내부 LAN 대상 호스트로 프록시
- Ed25519로 상호 식별, ECDH 공유 비밀 + HKDF로 세션 키/논스 파생
- 모든 프레임 LZ4 압축 후 ChaCha20-Poly1305(IETF) 암호화
- 서버는 동시에 1개의 클라이언트만 허용
- 클라이언트는 다중 터널 구성/관리 + GUI + 트레이 아이콘 제공

---

## 2) 기술 스택/라이브러리

### 공통
- .NET 10 / C#
- 비동기 I/O: `System.Net.Sockets`, `NetworkStream`, `System.IO.Pipelines`(선택)
- 로깅/DI: `Microsoft.Extensions.Logging`, `Microsoft.Extensions.DependencyInjection`
- 설정: `Microsoft.Extensions.Configuration` + `Json`

### 암호/압축
- Ed25519 + X25519 + ChaCha20-Poly1305: `NSec.Cryptography`
  - 이유: Ed25519, X25519, ChaCha20-Poly1305 지원 + 크로스플랫폼
- HKDF: `System.Security.Cryptography` (HKDF-SHA256)
- LZ4: `K4os.Compression.LZ4`

### GUI
- Avalonia UI: `Avalonia`, `Avalonia.Desktop`, `Avalonia.ReactiveUI`
- MVVM: `CommunityToolkit.Mvvm`
- 트레이 아이콘: `Avalonia.Controls`의 `TrayIcon`

---

## 3) 아키텍처 개요

### 솔루션 구조
- `src/Shared`
  - 프로토콜, 프레이밍, 압축/암호, DTO, 설정 모델, 공통 유틸
- `src/Server`
  - 제어 채널 수락, 포트 리스너 관리, 연결 매칭, 통계 수집
- `src/Client`
  - 제어 채널 유지, 터널 관리, 데이터 채널 생성, GUI(MVVM)

### 설계 원칙
- 단일 책임: 네트워크, 프로토콜, UI 분리
- 비동기 파이프라인: 네트워크 I/O는 `async/await` + `CancellationToken`
- 제어 채널과 데이터 채널 분리 (멀티플렉싱 대신 “1-데이터-채널/1-외부-연결”)

---

## 4) 프로토콜 설계

### 4.1 채널 구분
- Control Channel: 클라이언트가 서버로 항상 유지하는 제어 연결
- Data Channel: 외부 접속 1건당 클라이언트가 서버로 별도 연결 생성

### 4.2 핸드셰이크/키 교환
1) 양측은 Ed25519 identity key 보유 (서버는 허용 클라이언트 키 목록 유지)
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
| len (u32 LE) | ciphertext (len bytes) |
```
- `ciphertext = AEAD_Encrypt(key, nonce, compressed_plaintext, aad)`
- `compressed_plaintext = LZ4(plaintext)`
- `plaintext`:
```
| type (u8) | flags (u8) | stream_id (u32) | payload (n bytes) |
```
- `nonce = nonce_base (4 bytes) + counter (8 bytes LE)`
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
1) 설정 로드 (control_port, allowed_client_keys, listen_ports)
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
- `ControlSessionManager`
  - 단일 클라이언트만 허용, 인증 후 세션 유지
- `TunnelRegistry`
  - 열린 public port <-> tunnel_id 매핑
- `PublicListener`
  - 포트별 `TcpListener` 관리
- `ConnectionPairer`
  - `conn_id` 기준으로 외부 연결과 Data Channel 매칭
- `MetricsHub`
  - tunnel별 활성 연결 수 집계

### 서버 설정 예시
```json
{
  "controlPort": 9000,
  "allowedClientKeys": ["base64-ed25519-pubkey"],
  "tunnels": []
}
```

---

## 7) 클라이언트 구성/동작 상세

### 핵심 컴포넌트
- `ControlClient`
  - 핸드셰이크, heartbeat, 재연결
- `TunnelManager`
  - 다중 터널 생성/삭제/상태 관리
- `DataChannelFactory`
  - 요청받은 `conn_id`에 대해 서버로 새 연결 생성
- `LocalProxy`
  - `tunnel_id`에 대응하는 로컬 호스트/포트로 연결
- `UiStateStore`
  - UI 상태와 백엔드 상태 동기화

### 클라이언트 설정 예시
```json
{
  "server": {
    "host": "example.com",
    "controlPort": 9000,
    "serverPubKey": "base64-ed25519-pubkey"
  },
  "tunnels": [
    { "id": "web", "remotePort": 8080, "localHost": "192.168.0.10", "localPort": 80, "enabled": true }
  ]
}
```

---

## 8) GUI/UX 계획
- Avalonia MVVM 구조
- 메인 화면: 터널 리스트 + 상태(연결 수, 활성/비활성)
- 터널 추가/수정/삭제 다이얼로그
- 시스템 트레이 아이콘:
  - 최소화 시 숨김
  - 트레이 메뉴에서 빠른 시작/중지

---

## 9) 단계별 구현 순서

### Phase 1: 스캐폴딩
- 솔루션/프로젝트 생성 (Server/Client/Shared)
- 공통 로깅/설정 로딩 기반 추가

### Phase 2: Shared 프로토콜/암호화
- 프레임 인코더/디코더
- LZ4 압축 유틸
- NSec 기반 Ed25519/X25519/ChaCha20-Poly1305 구현
- HKDF 세션 키 파생 로직

### Phase 3: Server Core
- Control Channel 수락 + 인증
- Tunnel open/close 처리
- Public listener + connection pairing
- 간단한 CLI 실행 옵션 제공

### Phase 4: Client Core
- Control Channel 연결 + 재접속
- TunnelManager 구현
- Data Channel 생성 + 로컬 프록시 연결

### Phase 5: GUI
- 터널 목록/상태 표시
- 추가/삭제 UI
- 트레이 아이콘 동작

### Phase 6: 검증/테스트
- 로컬 테스트 시나리오 문서화
- 기본 통합 테스트 (하나의 터널로 외부 접속 시도)

---

## 10) 실행 예시 흐름 (로컬)
```
dotnet build
dotnet run --project src/Server -- --controlPort 9000
dotnet run --project src/Client -- --config client.json
```

---

## 11) 리스크/결정 포인트
- `NSec.Cryptography` 의존성 (native 바이너리 포함 여부 확인)
- Data Channel 방식(단순) vs 멀티플렉싱(복잡)
- 서버 단일 클라이언트 제한 처리 로직 (강제 종료 정책)

