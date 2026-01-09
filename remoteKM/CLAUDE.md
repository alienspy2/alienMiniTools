# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build
dotnet build remoteKM.sln

# Run Server
dotnet run --project src/RemoteKM.Server

# Run Client
dotnet run --project src/RemoteKM.Client

# Verbose mode (server only)
dotnet run --project src/RemoteKM.Server -- --verbose
```

Requires .NET SDK 10 and Windows (uses Windows Forms and Win32 APIs).

## Architecture

RemoteKM is a Windows-only TCP-based keyboard/mouse relay with clipboard sync and file transfer. Both apps run as system tray applications and require Administrator privileges.

### Client (`RemoteKM.Client`)
- **ClientAppContext**: Main application context, manages tray icon, settings, and coordinates all services
- **HookService**: Low-level Windows hooks (WH_KEYBOARD_LL, WH_MOUSE_LL) to capture input when active
- **TcpSender**: Manages TCP connection to server, sends input events and handles bidirectional clipboard/file transfer
- **HotKeyWindow**: Registers global hotkeys for capture toggle (uses RegisterHotKey API and raw input for mouse)
- **ClipboardSyncService**: Monitors local clipboard changes, applies remote clipboard updates

### Server (`RemoteKM.Server`)
- **TrayApplicationContext**: Main application context with settings UI and server lifecycle
- **TcpListenerService**: Accepts client connections, receives input events and clipboard data
- **InputPlayer**: Replays received keyboard/mouse events using SendInput API
- **ClipboardSyncService/PasteMonitor**: Same as client for bidirectional sync

### Protocol (InputModels)
Messages use a simple binary protocol with `MessageKind` header:
- `Keyboard/Mouse`: Input event data
- `Control`: CaptureStart/CaptureStop signals
- `Clipboard`: Text, FileList, or FileTransfer data

### Key Behaviors
- Edge trigger: Client monitors cursor at screen edges to auto-start capture
- Mouse events sent as relative deltas; cursor recentered during capture
- File transfer: Copy files in Explorer, Ctrl+V in destination Explorer folder triggers transfer via Ctrl+V hook
- Power events: Server restarts listener on resume from sleep; sends CaptureStop on suspend
