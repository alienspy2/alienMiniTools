# MMMarkDown

로컬 마크다운 파일을 마인드맵으로 관리하는 웹 애플리케이션입니다.

## 개요

MMMarkDown은 마크다운 문서들을 시각적인 마인드맵 형태로 연결하고 탐색할 수 있게 해주는 로컬 전용 도구입니다.

### 주요 기능

- **마인드맵 시각화**: 마크다운 파일들을 노드로 표현하여 계층적 구조로 관리
- **실시간 편집**: 노드 생성, 이동, 삭제 등 마인드맵 조작 기능
- **VS Code 연동**: 노드를 더블클릭하거나 단축키로 VS Code에서 마크다운 파일 편집
- **자동 요약**: Ollama를 활용한 마크다운 파일 자동 요약 (한국어 지원)
- **워크스페이스 관리**: 여러 문서 폴더를 워크스페이스로 전환하며 작업 가능
- **트레이 모드**: Windows에서 백그라운드 실행 지원
- **싱글 인스턴스**: 중복 실행 방지 및 기존 인스턴스로 포커스 전환

### 파일 구조

- `MDDoc/`: 마크다운 파일들이 저장되는 기본 폴더
- `*.mmm`: 노드 연결 구조와 요약 정보를 담은 JSON 상태 파일

## Run

기본 실행:

```bash
python mmm_app.py
```

Windows 백그라운드(트레이) 실행:

```bash
run.bat
```

브라우저에서 `http://127.0.0.1:23005` 열기.

## Tray mode (Windows)

Install dependencies:

```bash
pip install pystray pillow
```

Run in the background with a tray icon:

```bash
python mmm_app.py --tray
```

The tray menu provides `Open` and `Quit`. `run.bat` now launches tray mode via `pythonw`.

`run.bat` uses the `n8n` conda environment. If you use another env, run `python mmm_app.py --tray` in that env.

## Stop (Windows)

백그라운드 프로세스 종료:

```bash
kill.bat
```

PowerShell에서 직접 실행:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File kill.ps1
```

## Auto reload

The server auto-reloads when Python files change. Disable with `--no-reload` or set `MMM_RELOAD=0`.

## Usage

- Click a node to select it.
- `Insert` creates a child node.
- `Tab` creates a sibling node.
- `Shift` + `ArrowUp` moves the node up.
- `Shift` + `ArrowDown` moves the node down.
- `Ctrl` + `X` cuts the node.
- `Ctrl` + `V` pastes the node under the selected node.
- `F` focuses the selected node.
- `Delete` removes the selected node and its descendants (including Markdown files).
- Middle mouse drag pans the map.
- `Ctrl` + `Wheel` zooms in/out.
- Minimap buttons provide `+`, `-`, and `Fit` zoom controls.
- Click the minimap to move the viewport.
- `F2` or `Enter` opens the selected node in VS Code.
- The node name becomes the Markdown filename (the app will append `.md` if missing).
- App start 시 `MDDoc`에 이미 있는 `.md` 파일은 자동으로 노드로 추가됩니다 (루트 노드에 연결).
- Workspace는 Tools 패널에서 변경 가능하며, 마지막으로 선택한 경로가 다음 실행에 자동 적용됩니다.

VS Code integration uses the `code` CLI. If it is missing, open VS Code and run the "Shell Command: Install 'code' command in PATH" command.

## Summary automation

When a Markdown file changes, the server asynchronously summarizes it using local Ollama.

Defaults:
- Model: `gemma3:4b`
- Prompt: `아래 마크다운을 한국어로 간결하게 요약해줘. 3~5문장으로 작성하고, '요약:'이나 "Here's a 3-sentence summary..." 같은 머리말은 붙이지 말고 내용만 출력해.`

Environment overrides:
- `OLLAMA_MODEL=your-model`
- `SUMMARY_PROMPT="Your prompt"`
- `SUMMARY_POLL_SECONDS=3`
- `OLLAMA_ENABLED=0` to disable summaries

## State file

The map state is stored in a `.mmm` JSON file (default: `mindmap.mmm`). Override with:

```bash
python mmm_app.py --state other-file.mmm
```

Or set `MMM_FILE` to a path.
