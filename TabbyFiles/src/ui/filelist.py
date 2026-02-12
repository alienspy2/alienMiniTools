import gi
gi.require_version('Gtk', '3.0')
from gi.repository import Gtk, Gdk, GObject, GdkPixbuf, GLib, Pango
import os
import stat
import time
import subprocess
import shutil
from pathlib import Path


class FileListView(Gtk.Box):
    __gsignals__ = {
        'path-changed': (GObject.SignalFlags.RUN_FIRST, None, (str,)),
        'add-shortcut-requested': (GObject.SignalFlags.RUN_FIRST, None, (str,)),
        'open-terminal-requested': (GObject.SignalFlags.RUN_FIRST, None, (str,)),
    }

    # Column indices
    COL_ICON = 0
    COL_NAME = 1
    COL_SIZE = 2
    COL_TYPE = 3
    COL_DATE = 4
    COL_FULL_PATH = 5
    COL_IS_DIR = 6
    COL_SIZE_RAW = 7
    COL_DATE_RAW = 8

    def __init__(self):
        super().__init__(orientation=Gtk.Orientation.VERTICAL)
        self.current_path = os.path.expanduser("~")

        # ListStore: icon, name, size_str, type, date_str, full_path, is_dir, size_raw, date_raw
        self.store = Gtk.ListStore(
            GdkPixbuf.Pixbuf,  # icon
            str,               # name
            str,               # size display
            str,               # type
            str,               # date display
            str,               # full path
            bool,              # is_dir (for sorting dirs first)
            int,               # raw size (for sorting)
            int,               # raw timestamp (for sorting)
        )

        # Sorted model
        self.sorted_model = Gtk.TreeModelSort(model=self.store)
        self.sorted_model.set_sort_func(self.COL_NAME, self._sort_name, None)
        self.sorted_model.set_sort_func(self.COL_SIZE, self._sort_size, None)
        self.sorted_model.set_sort_func(self.COL_DATE, self._sort_date, None)
        self.sorted_model.set_sort_column_id(self.COL_NAME, Gtk.SortType.ASCENDING)

        # Scrolled Window
        scrolled = Gtk.ScrolledWindow()
        scrolled.set_policy(Gtk.PolicyType.AUTOMATIC, Gtk.PolicyType.AUTOMATIC)
        scrolled.set_vexpand(True)
        scrolled.set_hexpand(True)
        self.pack_start(scrolled, True, True, 0)

        # TreeView
        self.treeview = Gtk.TreeView(model=self.sorted_model)
        self.treeview.set_headers_clickable(True)
        self.treeview.set_enable_search(True)
        self.treeview.set_search_column(self.COL_NAME)
        self.treeview.get_style_context().add_class("filelist-view")
        self.treeview.get_selection().set_mode(Gtk.SelectionMode.MULTIPLE)
        self.treeview.connect("row-activated", self.on_row_activated)
        self.treeview.connect("button-press-event", self.on_button_press)
        self.treeview.connect("key-press-event", self.on_key_press)
        scrolled.add(self.treeview)

        # Columns
        # Name (icon + text)
        col_name = Gtk.TreeViewColumn("이름")
        col_name.set_expand(True)
        col_name.set_min_width(300)
        col_name.set_sort_column_id(self.COL_NAME)

        icon_renderer = Gtk.CellRendererPixbuf()
        col_name.pack_start(icon_renderer, False)
        col_name.add_attribute(icon_renderer, "pixbuf", self.COL_ICON)

        name_renderer = Gtk.CellRendererText()
        name_renderer.set_property("ellipsize", Pango.EllipsizeMode.END)
        col_name.pack_start(name_renderer, True)
        col_name.add_attribute(name_renderer, "text", self.COL_NAME)
        self.treeview.append_column(col_name)

        # Size
        col_size = Gtk.TreeViewColumn("크기")
        col_size.set_min_width(100)
        col_size.set_sort_column_id(self.COL_SIZE)
        size_renderer = Gtk.CellRendererText()
        size_renderer.set_property("xalign", 1.0)
        col_size.pack_start(size_renderer, True)
        col_size.add_attribute(size_renderer, "text", self.COL_SIZE)
        self.treeview.append_column(col_size)

        # Type
        col_type = Gtk.TreeViewColumn("유형")
        col_type.set_min_width(100)
        col_type.set_sort_column_id(self.COL_TYPE)
        type_renderer = Gtk.CellRendererText()
        col_type.pack_start(type_renderer, True)
        col_type.add_attribute(type_renderer, "text", self.COL_TYPE)
        self.treeview.append_column(col_type)

        # Date Modified
        col_date = Gtk.TreeViewColumn("수정한 날짜")
        col_date.set_min_width(150)
        col_date.set_sort_column_id(self.COL_DATE)
        date_renderer = Gtk.CellRendererText()
        col_date.pack_start(date_renderer, True)
        col_date.add_attribute(date_renderer, "text", self.COL_DATE)
        self.treeview.append_column(col_date)

        # Load icons
        self._folder_icon = self._load_icon("src/resources/icons/folder.svg", 16)
        self._file_icon = self._load_icon("src/resources/icons/file.svg", 16)

    def _load_icon(self, path, size):
        try:
            return GdkPixbuf.Pixbuf.new_from_file_at_scale(path, size, size, True)
        except Exception:
            return None

    def _sort_name(self, model, iter1, iter2, data):
        """Sort directories first, then by name."""
        is_dir1 = model.get_value(iter1, self.COL_IS_DIR)
        is_dir2 = model.get_value(iter2, self.COL_IS_DIR)
        if is_dir1 != is_dir2:
            return -1 if is_dir1 else 1
        name1 = model.get_value(iter1, self.COL_NAME).lower()
        name2 = model.get_value(iter2, self.COL_NAME).lower()
        if name1 < name2:
            return -1
        elif name1 > name2:
            return 1
        return 0

    def _sort_size(self, model, iter1, iter2, data):
        is_dir1 = model.get_value(iter1, self.COL_IS_DIR)
        is_dir2 = model.get_value(iter2, self.COL_IS_DIR)
        if is_dir1 != is_dir2:
            return -1 if is_dir1 else 1
        s1 = model.get_value(iter1, self.COL_SIZE_RAW)
        s2 = model.get_value(iter2, self.COL_SIZE_RAW)
        return (s1 > s2) - (s1 < s2)

    def _sort_date(self, model, iter1, iter2, data):
        is_dir1 = model.get_value(iter1, self.COL_IS_DIR)
        is_dir2 = model.get_value(iter2, self.COL_IS_DIR)
        if is_dir1 != is_dir2:
            return -1 if is_dir1 else 1
        d1 = model.get_value(iter1, self.COL_DATE_RAW)
        d2 = model.get_value(iter2, self.COL_DATE_RAW)
        return (d1 > d2) - (d1 < d2)

    def set_path(self, path):
        if not os.path.isdir(path):
            return
        self.current_path = path
        self._populate(path)
        self.emit('path-changed', path)

    def get_current_path(self):
        return self.current_path

    def _populate(self, path):
        self.store.clear()
        try:
            entries = list(os.scandir(path))
        except PermissionError:
            return

        for entry in entries:
            try:
                st = entry.stat(follow_symlinks=False)
                is_dir = entry.is_dir(follow_symlinks=True)
                name = entry.name

                if is_dir:
                    size_str = ""
                    size_raw = 0
                    file_type = "폴더"
                    icon = self._folder_icon
                else:
                    size_raw = st.st_size
                    size_str = self._format_size(size_raw)
                    ext = os.path.splitext(name)[1].lower()
                    file_type = ext[1:].upper() + " 파일" if ext else "파일"
                    icon = self._file_icon

                mtime = int(st.st_mtime)
                date_str = time.strftime("%Y-%m-%d %H:%M", time.localtime(mtime))

                self.store.append([
                    icon, name, size_str, file_type, date_str,
                    entry.path, is_dir, size_raw, mtime
                ])
            except (OSError, PermissionError):
                continue

    def _format_size(self, size):
        for unit in ['B', 'KB', 'MB', 'GB', 'TB']:
            if size < 1024:
                if unit == 'B':
                    return f"{size} {unit}"
                return f"{size:.1f} {unit}"
            size /= 1024
        return f"{size:.1f} PB"

    def on_row_activated(self, treeview, path, column):
        model = treeview.get_model()
        iter_ = model.get_iter(path)
        full_path = model.get_value(iter_, self.COL_FULL_PATH)
        is_dir = model.get_value(iter_, self.COL_IS_DIR)
        if is_dir:
            self.set_path(full_path)

    def on_button_press(self, widget, event):
        if event.button == 3:  # Right click
            path_info = self.treeview.get_path_at_pos(int(event.x), int(event.y))
            target_path = self.current_path
            if path_info:
                tree_path = path_info[0]
                model = self.treeview.get_model()
                iter_ = model.get_iter(tree_path)
                target_path = model.get_value(iter_, self.COL_FULL_PATH)
            self.show_context_menu(event, target_path)
            return True
        return False

    def on_key_press(self, widget, event):
        if event.keyval == Gdk.KEY_BackSpace:
            parent = os.path.dirname(self.current_path)
            if parent != self.current_path:
                self.set_path(parent)
            return True
        return False

    def _get_selected_path(self):
        selection = self.treeview.get_selection()
        model, paths = selection.get_selected_rows()
        if paths:
            iter_ = model.get_iter(paths[0])
            return model.get_value(iter_, self.COL_FULL_PATH)
        return None

    def show_context_menu(self, event, target_path):
        menu = Gtk.Menu()
        is_dir = os.path.isdir(target_path)

        # Add to shortcuts (only for directories)
        if is_dir:
            add_shortcut_item = self._make_menu_item(
                "바로가기에 추가", "src/resources/icons/add.svg"
            )
            add_shortcut_item.connect(
                "activate", lambda w: self.emit('add-shortcut-requested', target_path)
            )
            menu.append(add_shortcut_item)
            menu.append(Gtk.SeparatorMenuItem())

        # Open with submenu
        open_with_submenu = Gtk.Menu()
        has_open_with = False

        if shutil.which("nemo"):
            nemo_item = self._make_menu_item("Nemo", "src/resources/icons/nemo.svg")
            nemo_item.connect(
                "activate", lambda w: self.open_external_app("nemo", target_path)
            )
            open_with_submenu.append(nemo_item)
            has_open_with = True

        if shutil.which("tabby"):
            tabby_item = self._make_menu_item("Tabby", "src/resources/icons/tabby.svg")
            tabby_item.connect(
                "activate", lambda w: self.open_external_app("tabby", target_path)
            )
            open_with_submenu.append(tabby_item)
            has_open_with = True

        if shutil.which("antigravity"):
            ag_item = self._make_menu_item("Antigravity", "src/resources/icons/antigravity.svg")
            ag_item.connect(
                "activate", lambda w: self.open_external_app("antigravity", target_path)
            )
            open_with_submenu.append(ag_item)
            has_open_with = True

        if shutil.which("xed"):
            xed_item = self._make_menu_item("Xed", "src/resources/icons/xed.svg")
            xed_item.connect(
                "activate", lambda w: self.open_external_app("xed", target_path)
            )
            open_with_submenu.append(xed_item)
            has_open_with = True

        if has_open_with:
            open_with_item = Gtk.MenuItem(label="Open with")
            open_with_item.set_submenu(open_with_submenu)
            menu.append(open_with_item)

        # Ask Gemma
        gemma_item = self._make_menu_item("Ask Gemma", "src/resources/icons/gemma.svg")
        gemma_item.connect(
            "activate", lambda w: self.open_external_app("gemma", target_path)
        )
        menu.append(gemma_item)

        # Separator before terminal
        menu.append(Gtk.SeparatorMenuItem())

        # Open Terminal (integrated)
        terminal_dir = target_path if is_dir else str(Path(target_path).parent)
        terminal_item = self._make_menu_item("Open Terminal", "src/resources/icons/terminal.svg")
        terminal_item.connect(
            "activate", lambda w: self.emit('open-terminal-requested', terminal_dir)
        )
        menu.append(terminal_item)

        menu.show_all()
        menu.popup_at_pointer(event)

    def _make_menu_item(self, label, icon_path):
        item = Gtk.MenuItem()
        hbox = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        try:
            pixbuf = GdkPixbuf.Pixbuf.new_from_file_at_scale(icon_path, 16, 16, True)
            icon = Gtk.Image.new_from_pixbuf(pixbuf)
            hbox.pack_start(icon, False, False, 0)
        except Exception:
            pass
        lbl = Gtk.Label(label=label)
        lbl.set_xalign(0)
        hbox.pack_start(lbl, True, True, 0)
        item.add(hbox)
        return item

    def open_external_app(self, app_name, path):
        try:
            if os.path.isdir(path):
                cwd = path
            else:
                cwd = str(Path(path).parent)

            if app_name == "tabby":
                subprocess.Popen(["tabby", "open", cwd], cwd=cwd)
            elif app_name == "nemo":
                subprocess.Popen(["nemo", path])
            elif app_name == "antigravity":
                subprocess.Popen(["antigravity", path], cwd=cwd)
            elif app_name == "xed":
                subprocess.Popen(["xed", path], cwd=cwd)
            elif app_name == "gemma":
                from src.ui.gemma_dialog import GemmaDialog
                dialog = GemmaDialog(cwd, self.get_toplevel())
                dialog.run()
                dialog.destroy()
        except Exception as e:
            print(f"Error opening {app_name}: {e}")
