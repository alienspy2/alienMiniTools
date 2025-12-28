# MMMarkDown

Local-only mind map for Markdown files. Each node maps to a single `.md` file in `MDDoc`, and node connections plus summaries live in a `.mmm` JSON file.

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
