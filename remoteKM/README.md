﻿# RemoteKM

Windows-only keyboard/mouse relay over TCP. Run both apps as Administrator.

TCP 기반 윈도우 전용 키보드/마우스 릴레이입니다. 두 앱 모두 관리자 권한으로 실행하세요.

## Build (SDK 10)

```
dotnet build remoteKM.sln
```

## Run

Server:

```
dotnet run --project src/RemoteKM.Server
```

Client:

```
dotnet run --project src/RemoteKM.Client
```

## Usage

Server:

- A tray icon appears. Open the menu and choose "Settings..." to set host/port.
- Applying settings restarts the listener on the new endpoint.
- Use "Quit" from the tray menu to exit.

Client:

- A tray icon appears. Use "Settings" to set host/port, hotkey, and capture edge.
- Default hotkey is `Alt+Oem3` (toggle capture).
- "Toggle Capture" from the tray menu also works.
- When capture is on, local input is swallowed and forwarded to the server.
- Mouse moves are sent as relative deltas, and the client recenters the cursor during capture.
- File transfer: copy files in Explorer on the source machine, then press Ctrl+V in an Explorer folder on the destination. A transfer progress popup appears.
- File transfer mechanism: the file list syncs via clipboard, and pressing Ctrl+V in Explorer starts a transfer into the active folder.
- Notes: the Ctrl+V hook works only in Explorer. Run both apps as Administrator for reliable file transfer.

## 빌드 (SDK 10)

```
dotnet build remoteKM.sln
```

## 실행

서버:

```
dotnet run --project src/RemoteKM.Server
```

클라이언트:

```
dotnet run --project src/RemoteKM.Client
```

## 사용 방법

서버:

- 트레이 아이콘이 나타납니다. 메뉴에서 "Settings..."를 열어 호스트/포트를 설정하세요.
- 설정을 적용하면 새 엔드포인트로 리스너가 재시작됩니다.
- 트레이 메뉴의 "Quit"으로 종료합니다.

클라이언트:

- 트레이 아이콘이 나타납니다. "Settings"에서 호스트/포트, 핫키, 캡처 에지를 설정하세요.
- 기본 핫키는 `Alt+Oem3`이며 캡처 토글용입니다.
- 트레이 메뉴의 "Toggle Capture"로도 토글할 수 있습니다.
- 캡처가 켜지면 로컬 입력이 차단되고 서버로 전달됩니다.
- 마우스 이동은 상대 델타로 전송되며, 캡처 중에는 커서를 중앙으로 되돌립니다.
- 파일 전송: 원본 PC의 탐색기에서 파일을 복사한 뒤, 대상 PC의 탐색기 폴더에서 Ctrl+V를 누르세요. 전송 진행 팝업이 표시됩니다.
- 파일 전송 동작: 클립보드로 파일 목록이 동기화되며 탐색기에서 Ctrl+V를 누르면 활성 폴더로 전송이 시작됩니다.
- 주의: Ctrl+V 훅은 탐색기에서만 동작합니다. 파일 전송은 관리자 권한 실행이 필요합니다.
