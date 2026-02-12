# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TabbyFiles is a GTK 3 desktop file manager written in Python 3.10+. It features file browsing with shortcuts, an integrated VTE terminal with tabs, and an AI-powered Bash script generator via local Ollama (Gemma 3 4B model). The UI language is Korean.

## Commands

```bash
# Run the application
./run.sh                    # Linux
python main.py              # Direct (requires activated venv)

# Set up / sync dependencies
uv sync

# Regenerate SVG icons
python generate_icons.py
```

There are no test or lint configurations in this project.

## Architecture

The app follows a signal-based component architecture on top of GTK 3.

**Entry point:** `main.py` — Loads GTK, applies global CSS dark theme, creates `MainWindow`, starts the GTK main loop.

**Configuration:** `src/config.py` — `ConfigManager` persists sidebar shortcuts to `~/.config/alien_file_manager/shortcuts.json`.

**UI components** (`src/ui/`):
- **MainWindow** (`mainwindow.py`) — Orchestrator. Lays out Sidebar | Content using `Gtk.Paned`. Routes signals between child components.
- **FileListView** (`filelist.py`) — TreeView displaying directory contents with sorting (directories first). Provides right-click context menu for external app launching (Nemo, Tabby, Xed, Antigravity), shortcut management, and "Ask Gemma" AI dialog.
- **Sidebar** (`sidebar.py`) — ListBox of user shortcuts with drag-and-drop reordering and add/remove via context menu.
- **TerminalPanel** (`terminal.py`) — Tabbed VTE terminal container. Each `TerminalTab` spawns a Bash shell with dark theme and 5000-line scrollback.
- **GemmaDialog** (`gemma_dialog.py`) — Modal dialog that takes a task description, calls Ollama to generate a Bash script, and executes it. Uses daemon threads + `GLib.idle_add()` for thread-safe GTK updates.

**Utilities** (`src/utils/`):
- **OllamaClient** (`ollama_client.py`) — REST client for Ollama at `localhost:11434`. System prompt constrains output to pure Bash code.

### Signal flow

```
Sidebar --path-selected--> MainWindow --> FileListView (navigate)
FileListView --path-changed--> MainWindow --> AddressBar, TerminalPanel (cd)
FileListView --add-shortcut-requested--> MainWindow --> Sidebar
FileListView --open-terminal-requested--> MainWindow --> TerminalPanel
TerminalPanel --terminal-closed--> MainWindow (hide panel)
```

## Key Dependencies

- **PyGObject** (GTK 3 bindings) — all UI
- **VTE 2.91** — integrated terminal (system package, not pip)
- **requests** — Ollama HTTP API
- **Ollama** running locally — required only for the "Ask Gemma" feature

## Conventions

- Korean is used for commit messages, UI strings, and comments
- All icons are programmatically generated SVGs in `src/resources/icons/`
- Threading for async operations uses Python `threading` + `GLib.idle_add()`/`GLib.timeout_add()` for GTK thread safety
- External app integration assumes Linux desktop apps (Nemo, Xed, Tabby) are installed
