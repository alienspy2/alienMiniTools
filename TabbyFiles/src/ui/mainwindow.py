import gi
gi.require_version('Gtk', '3.0')
from gi.repository import Gtk, Gdk, GdkPixbuf
from src.ui.sidebar import Sidebar
from src.ui.filelist import FileListView
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

        # Right side (Address Bar + File List)
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

        # File List
        self.file_list = FileListView()
        right_box.pack_start(self.file_list, True, True, 0)

        # Connect signals
        self.sidebar.connect("path-selected", self.on_sidebar_path_selected)
        self.file_list.connect("path-changed", self.on_path_changed)
        self.file_list.connect("add-shortcut-requested", self.on_add_shortcut)

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

    def on_add_shortcut(self, widget, path):
        self.sidebar.add_path_to_shortcuts(path)

    def navigate_up(self, button):
        import os
        current_path = self.file_list.get_current_path()
        parent = os.path.dirname(current_path)
        if parent and parent != current_path:
            self.file_list.set_path(parent)
