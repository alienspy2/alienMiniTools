import gi
gi.require_version('Gtk', '3.0')
from gi.repository import Gtk, Gdk, GdkPixbuf, GLib
import os
import sys
from src.ui.sidebar import Sidebar
from src.ui.filelist import FileListView
from src.ui.terminal import TerminalPanel
from src.config import ConfigManager


class AddressBar(Gtk.Entry):
    def __init__(self):
        super().__init__()
        self.set_editable(False)
        self.set_can_focus(False)
        self.get_style_context().add_class("address-bar")
        self.set_hexpand(True)
        self.connect("button-release-event", self.on_click)

    def on_click(self, widget, event):
        if event.button == 1:
            clipboard = Gtk.Clipboard.get(Gdk.SELECTION_CLIPBOARD)
            clipboard.set_text(self.get_text(), -1)
            self.select_region(0, -1)
            # Show tooltip
            self.set_tooltip_text("주소가 복사되었습니다!")


class MainWindow(Gtk.Window):
    def __init__(self):
        super().__init__(title="에일리언 파일 매니저")
        self.set_default_size(1000, 700)

        # Set window icon
        try:
            self.set_icon_from_file("src/resources/icons/app_icon.svg")
        except Exception:
            pass

        # Load Config
        self.config_manager = ConfigManager()

        # Main container: Paned (horizontal splitter)
        self.paned = Gtk.Paned(orientation=Gtk.Orientation.HORIZONTAL)
        self.add(self.paned)

        # Sidebar
        self.sidebar = Sidebar(self.config_manager)
        self.sidebar.set_size_request(150, -1)
        self.paned.pack1(self.sidebar, resize=False, shrink=False)

        # Right side (Address Bar + File List + Terminal)
        right_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
        self.paned.pack2(right_box, resize=True, shrink=False)

        # Top bar (Up Button + Address Bar)
        top_bar = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL)
        right_box.pack_start(top_bar, False, False, 0)

        # Up Button
        self.up_button = Gtk.Button()
        try:
            pixbuf = GdkPixbuf.Pixbuf.new_from_file_at_scale(
                "src/resources/icons/up.svg", 20, 20, True
            )
            self.up_button.set_image(Gtk.Image.new_from_pixbuf(pixbuf))
        except Exception:
            self.up_button.set_label("↑")
        self.up_button.get_style_context().add_class("up-button")
        self.up_button.set_size_request(40, -1)
        self.up_button.connect("clicked", self.navigate_up)
        top_bar.pack_start(self.up_button, False, False, 0)

        # Address Bar
        self.address_bar = AddressBar()
        top_bar.pack_start(self.address_bar, True, True, 0)

        # Vertical Paned: File List (top) + Terminal (bottom)
        self.content_paned = Gtk.Paned(orientation=Gtk.Orientation.VERTICAL)
        right_box.pack_start(self.content_paned, True, True, 0)

        # File List
        self.file_list = FileListView()
        self.content_paned.pack1(self.file_list, resize=True, shrink=False)

        # Terminal Panel placeholder (created on demand)
        self.terminal_panel = None

        # Connect signals
        self.sidebar.connect("path-selected", self.on_sidebar_path_selected)
        self.file_list.connect("path-changed", self.on_path_changed)
        self.file_list.connect("add-shortcut-requested", self.on_add_shortcut)
        self.file_list.connect("open-terminal-requested", self.on_open_terminal)

        # (test)reload — F5로 앱 재시작
        self.connect("key-press-event", self._on_key_press)

        # Set splitter position
        self.paned.set_position(150)

        # Set default path
        if self.config_manager.shortcuts:
            first_path = self.config_manager.shortcuts[0]["path"]
            self.file_list.set_path(first_path)
            self.address_bar.set_text(first_path)

    def on_sidebar_path_selected(self, widget, path):
        self.file_list.set_path(path)

    def on_path_changed(self, widget, path):
        self.address_bar.set_text(path)
        self.sidebar.update_selection(path)

    def on_add_shortcut(self, widget, path):
        self.sidebar.add_path_to_shortcuts(path)

    def on_open_terminal(self, widget, path):
        """컨텍스트 메뉴에서 Open Terminal 요청 시 새 터미널 탭을 추가합니다."""
        current_dir = path

        if self.terminal_panel is None:
            # 패널이 없으면 새로 생성
            self.terminal_panel = TerminalPanel()
            self.terminal_panel.set_size_request(-1, 200)
            self.terminal_panel.connect("terminal-closed", self.on_terminal_closed)
            self.content_paned.pack2(self.terminal_panel, resize=False, shrink=False)
            self.terminal_panel.show_all()

            # Set paned position
            def _set_position():
                alloc = self.content_paned.get_allocation()
                if alloc.height > 250:
                    self.content_paned.set_position(alloc.height - 200)
                return False
            GLib.idle_add(_set_position)

        # 새 탭 추가
        self.terminal_panel.add_terminal_tab(current_dir)

    def on_terminal_closed(self, widget):
        """모든 탭이 닫힌 후 패널을 완전히 제거합니다."""
        if self.terminal_panel is not None:
            self.terminal_panel.close_all_terminals()
            self.terminal_panel.destroy()
            self.terminal_panel = None

    def _on_key_press(self, widget, event):
        if event.keyval == Gdk.KEY_F5:
            print("(test)reload: 앱을 재시작합니다...")
            os.execv(sys.executable, [sys.executable] + sys.argv)
            return True
        return False

    def navigate_up(self, button):
        current_path = self.file_list.get_current_path()
        parent = os.path.dirname(current_path)
        if parent and parent != current_path:
            self.file_list.set_path(parent)

