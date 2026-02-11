import gi
gi.require_version('Gtk', '3.0')
gi.require_version('Vte', '2.91')
from gi.repository import Gtk, Vte, GLib, GObject, Gdk
import os


class TerminalTab(Gtk.Box):
    """개별 터미널 탭 콘텐츠 (VTE Terminal)."""

    def __init__(self, working_directory=None):
        super().__init__(orientation=Gtk.Orientation.VERTICAL)
        self._pid = -1

        if working_directory is None:
            working_directory = os.path.expanduser("~")
        self._working_directory = working_directory

        # VTE Terminal
        self.terminal = Vte.Terminal()
        self.terminal.set_size(80, 12)
        self.terminal.set_font_scale(1.0)
        self.terminal.set_scroll_on_output(True)
        self.terminal.set_scroll_on_keystroke(True)
        self.terminal.set_scrollback_lines(5000)

        # High-contrast dark theme
        self._apply_theme()

        # Scrolled window
        scrolled = Gtk.ScrolledWindow()
        scrolled.set_policy(Gtk.PolicyType.AUTOMATIC, Gtk.PolicyType.AUTOMATIC)
        scrolled.set_vexpand(True)
        scrolled.add(self.terminal)
        self.pack_start(scrolled, True, True, 0)

    def _apply_theme(self):
        bg = Gdk.RGBA()
        bg.parse("#0d0d0d")
        fg = Gdk.RGBA()
        fg.parse("#f0f0f0")

        cursor_color = Gdk.RGBA()
        cursor_color.parse("#ffcc00")
        self.terminal.set_color_cursor(cursor_color)
        self.terminal.set_color_cursor_foreground(bg)

        bold_color = Gdk.RGBA()
        bold_color.parse("#ffffff")
        self.terminal.set_color_bold(bold_color)

        palette_hex = [
            "#1a1a1a", "#ff5555", "#55ff55", "#ffff55",
            "#5599ff", "#ff55ff", "#55ffff", "#dddddd",
            "#666666", "#ff8888", "#88ff88", "#ffff88",
            "#88bbff", "#ff88ff", "#88ffff", "#ffffff",
        ]
        palette = []
        for h in palette_hex:
            c = Gdk.RGBA()
            c.parse(h)
            palette.append(c)
        self.terminal.set_colors(fg, bg, palette)

    def spawn_shell(self):
        """bash 셸을 시작합니다."""
        if self._pid > 0:
            return

        self.terminal.spawn_async(
            Vte.PtyFlags.DEFAULT,
            self._working_directory,
            ["/bin/bash"],
            None,
            GLib.SpawnFlags.DEFAULT,
            None, None, -1, None,
            self._on_spawn_complete, None,
        )

    def _on_spawn_complete(self, terminal, pid, error, user_data):
        if error:
            print(f"Terminal spawn error: {error}")
            return
        self._pid = pid
        self.terminal.connect("child-exited", self._on_child_exited)

    def _on_child_exited(self, terminal, status):
        self._pid = -1

    def close_terminal(self):
        if self._pid > 0:
            try:
                import signal
                os.kill(self._pid, signal.SIGHUP)
            except ProcessLookupError:
                pass
            self._pid = -1


class TerminalPanel(Gtk.Box):
    """Notebook 기반 탭 터미널 패널."""

    __gsignals__ = {
        'terminal-closed': (GObject.SignalFlags.RUN_FIRST, None, ()),
    }

    _tab_counter = 0

    def __init__(self):
        super().__init__(orientation=Gtk.Orientation.VERTICAL)

        # Notebook for tabs
        self.notebook = Gtk.Notebook()
        self.notebook.set_scrollable(True)
        self.notebook.get_style_context().add_class("terminal-notebook")
        self.notebook.set_show_tabs(True)
        self.pack_start(self.notebook, True, True, 0)

    def add_terminal_tab(self, working_directory):
        """새 터미널 탭을 추가합니다."""
        TerminalPanel._tab_counter += 1
        tab_num = TerminalPanel._tab_counter

        # Terminal content
        tab = TerminalTab(working_directory)
        tab.show_all()

        # Tab label with close button
        tab_label = self._create_tab_label(f"Terminal {tab_num}", tab)

        self.notebook.append_page(tab, tab_label)
        self.notebook.set_current_page(self.notebook.get_n_pages() - 1)
        self.notebook.set_tab_reorderable(tab, True)

        # Spawn shell
        tab.spawn_shell()

    def _create_tab_label(self, title, tab_widget):
        """닫기 버튼이 있는 탭 라벨을 생성합니다."""
        hbox = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=4)

        label = Gtk.Label(label=title)
        label.set_xalign(0)
        hbox.pack_start(label, True, True, 0)

        close_btn = Gtk.Button(label="✕")
        close_btn.get_style_context().add_class("terminal-tab-close")
        close_btn.set_relief(Gtk.ReliefStyle.NONE)
        close_btn.connect("clicked", self._on_tab_close, tab_widget)
        hbox.pack_end(close_btn, False, False, 0)

        hbox.show_all()
        return hbox

    def _on_tab_close(self, button, tab_widget):
        """개별 탭 닫기."""
        page_num = self.notebook.page_num(tab_widget)
        if page_num >= 0:
            tab_widget.close_terminal()
            self.notebook.remove_page(page_num)
            tab_widget.destroy()

        # 모든 탭이 닫히면 패널 전체를 닫음
        if self.notebook.get_n_pages() == 0:
            self.emit('terminal-closed')

    def close_all_terminals(self):
        """모든 터미널 탭을 종료합니다."""
        while self.notebook.get_n_pages() > 0:
            tab = self.notebook.get_nth_page(0)
            if isinstance(tab, TerminalTab):
                tab.close_terminal()
            self.notebook.remove_page(0)
            tab.destroy()
