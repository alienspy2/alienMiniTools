import gi
gi.require_version('Gtk', '3.0')
from gi.repository import Gtk, Gdk, GObject, GdkPixbuf, GLib, Pango
import os
import stat
import time
import subprocess
import shutil
from pathlib import Path
from urllib.parse import quote, unquote, urlparse
import threading
from src.utils.archive import compress_7z


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
        col_name.set_cell_data_func(icon_renderer, self._alternate_row_bg)

        name_renderer = Gtk.CellRendererText()
        name_renderer.set_property("ellipsize", Pango.EllipsizeMode.END)
        col_name.pack_start(name_renderer, True)
        col_name.add_attribute(name_renderer, "text", self.COL_NAME)
        col_name.set_cell_data_func(name_renderer, self._alternate_row_bg)
        self.treeview.append_column(col_name)

        # Size
        col_size = Gtk.TreeViewColumn("크기")
        col_size.set_min_width(100)
        col_size.set_sort_column_id(self.COL_SIZE)
        size_renderer = Gtk.CellRendererText()
        size_renderer.set_property("xalign", 1.0)
        col_size.pack_start(size_renderer, True)
        col_size.add_attribute(size_renderer, "text", self.COL_SIZE)
        col_size.set_cell_data_func(size_renderer, self._alternate_row_bg)
        self.treeview.append_column(col_size)

        # Type
        col_type = Gtk.TreeViewColumn("유형")
        col_type.set_min_width(100)
        col_type.set_sort_column_id(self.COL_TYPE)
        type_renderer = Gtk.CellRendererText()
        col_type.pack_start(type_renderer, True)
        col_type.add_attribute(type_renderer, "text", self.COL_TYPE)
        col_type.set_cell_data_func(type_renderer, self._alternate_row_bg)
        self.treeview.append_column(col_type)

        # Date Modified
        col_date = Gtk.TreeViewColumn("수정한 날짜")
        col_date.set_min_width(150)
        col_date.set_sort_column_id(self.COL_DATE)
        date_renderer = Gtk.CellRendererText()
        col_date.pack_start(date_renderer, True)
        col_date.add_attribute(date_renderer, "text", self.COL_DATE)
        col_date.set_cell_data_func(date_renderer, self._alternate_row_bg)
        self.treeview.append_column(col_date)

        # Load icons
        self._folder_icon = self._load_icon("src/resources/icons/folder.svg", 16)
        self._file_icon = self._load_icon("src/resources/icons/file.svg", 16)

    def _alternate_row_bg(self, column, cell, model, iter_, data):
        """홀짝 행에 따라 배경색을 번갈아 적용합니다."""
        path = model.get_path(iter_)
        row_index = path.get_indices()[0]
        if row_index % 2 == 1:
            cell.set_property('cell-background', '#272520')
        else:
            cell.set_property('cell-background', None)

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
            if path_info:
                tree_path = path_info[0]
                selection = self.treeview.get_selection()
                if not selection.path_is_selected(tree_path):
                    selection.unselect_all()
                    selection.select_path(tree_path)
                model = self.treeview.get_model()
                iter_ = model.get_iter(tree_path)
                target_path = model.get_value(iter_, self.COL_FULL_PATH)
                self.show_context_menu(event, target_path, is_on_item=True)
            else:
                self.treeview.get_selection().unselect_all()
                self.show_context_menu(event, self.current_path, is_on_item=False)
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

    def _get_selected_paths(self):
        selection = self.treeview.get_selection()
        model, paths = selection.get_selected_rows()
        result = []
        for path in paths:
            iter_ = model.get_iter(path)
            result.append(model.get_value(iter_, self.COL_FULL_PATH))
        return result

    def show_context_menu(self, event, target_path, is_on_item=True):
        menu = Gtk.Menu()
        is_dir = os.path.isdir(target_path)

        if is_on_item:
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

            # 편집 서브메뉴
            edit_submenu = Gtk.Menu()

            copy_item = self._make_menu_item("복사", "src/resources/icons/copy.svg")
            copy_item.connect("activate", lambda w: self._copy_selected())
            edit_submenu.append(copy_item)

            dup_item = self._make_menu_item("복제", "src/resources/icons/duplicate.svg")
            dup_item.connect("activate", lambda w: self._duplicate_selected())
            edit_submenu.append(dup_item)

            del_item = self._make_menu_item("삭제", "src/resources/icons/delete.svg")
            del_item.connect("activate", lambda w: self._delete_selected())
            edit_submenu.append(del_item)

            if shutil.which("7z"):
                compress_item = self._make_menu_item("7z 압축", "src/resources/icons/compress.svg")
                compress_item.connect("activate", lambda w: self._compress_7z(self._get_selected_paths()))
                edit_submenu.append(compress_item)

                if not is_dir and self._is_archive(target_path):
                    extract_item = self._make_menu_item("압축 풀기", "src/resources/icons/extract.svg")
                    extract_item.connect("activate", lambda w: self._extract_archive(target_path))
                    edit_submenu.append(extract_item)

            edit_item = Gtk.MenuItem(label="편집")
            edit_item.set_submenu(edit_submenu)
            menu.append(edit_item)

            # 열기 submenu
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

            if not is_dir and self._is_archive(target_path):
                archiver = self._find_archive_app()
                if archiver:
                    open_item = self._make_menu_item("압축파일 열기", "src/resources/icons/compress.svg")
                    open_item.connect(
                        "activate", lambda w, a=archiver, p=target_path: subprocess.Popen([a, p])
                    )
                    open_with_submenu.append(open_item)
                    has_open_with = True

            if has_open_with:
                open_with_item = Gtk.MenuItem(label="열기")
                open_with_item.set_submenu(open_with_submenu)
                menu.append(open_with_item)

            # Ask Gemma
            gemma_item = self._make_menu_item("Ask Gemma", "src/resources/icons/gemma.svg")
            gemma_item.connect(
                "activate", lambda w: self.open_external_app("gemma", target_path)
            )
            menu.append(gemma_item)

            # Open Terminal (integrated)
            terminal_dir = target_path if is_dir else str(Path(target_path).parent)
            terminal_item = self._make_menu_item("Open Terminal", "src/resources/icons/terminal.svg")
            terminal_item.connect(
                "activate", lambda w: self.emit('open-terminal-requested', terminal_dir)
            )
            menu.append(terminal_item)

        else:
            # 빈 공간 우클릭 — 편집 서브메뉴
            edit_submenu = Gtk.Menu()

            paste_item = self._make_menu_item("붙여넣기", "src/resources/icons/paste.svg")
            paste_item.connect("activate", lambda w: self._paste_files())
            if not self._has_clipboard_files():
                paste_item.set_sensitive(False)
            edit_submenu.append(paste_item)

            if shutil.which("7z"):
                compress_item = self._make_menu_item("7z 압축", "src/resources/icons/compress.svg")
                compress_item.connect("activate", lambda w: self._compress_7z([self.current_path]))
                edit_submenu.append(compress_item)

            edit_item = Gtk.MenuItem(label="편집")
            edit_item.set_submenu(edit_submenu)
            menu.append(edit_item)

            # 열기 submenu (현재 폴더 대상)
            open_with_submenu = Gtk.Menu()
            has_open_with = False

            if shutil.which("nemo"):
                nemo_item = self._make_menu_item("Nemo", "src/resources/icons/nemo.svg")
                nemo_item.connect(
                    "activate", lambda w: self.open_external_app("nemo", self.current_path)
                )
                open_with_submenu.append(nemo_item)
                has_open_with = True

            if shutil.which("tabby"):
                tabby_item = self._make_menu_item("Tabby", "src/resources/icons/tabby.svg")
                tabby_item.connect(
                    "activate", lambda w: self.open_external_app("tabby", self.current_path)
                )
                open_with_submenu.append(tabby_item)
                has_open_with = True

            if shutil.which("antigravity"):
                ag_item = self._make_menu_item("Antigravity", "src/resources/icons/antigravity.svg")
                ag_item.connect(
                    "activate", lambda w: self.open_external_app("antigravity", self.current_path)
                )
                open_with_submenu.append(ag_item)
                has_open_with = True

            if shutil.which("xed"):
                xed_item = self._make_menu_item("Xed", "src/resources/icons/xed.svg")
                xed_item.connect(
                    "activate", lambda w: self.open_external_app("xed", self.current_path)
                )
                open_with_submenu.append(xed_item)
                has_open_with = True

            if has_open_with:
                open_with_item = Gtk.MenuItem(label="열기")
                open_with_item.set_submenu(open_with_submenu)
                menu.append(open_with_item)

            # Ask Gemma
            gemma_item = self._make_menu_item("Ask Gemma", "src/resources/icons/gemma.svg")
            gemma_item.connect(
                "activate", lambda w: self.open_external_app("gemma", self.current_path)
            )
            menu.append(gemma_item)

            # Open Terminal
            terminal_item = self._make_menu_item("Open Terminal", "src/resources/icons/terminal.svg")
            terminal_item.connect(
                "activate", lambda w: self.emit('open-terminal-requested', self.current_path)
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

    def _get_clipboard(self):
        return Gtk.Clipboard.get(Gdk.SELECTION_CLIPBOARD)

    def _copy_selected(self):
        paths = self._get_selected_paths()
        if not paths:
            print("[복사] 선택된 항목 없음")
            return

        uris = "\n".join("file://" + quote(p, safe="/:@") for p in paths)
        content = f"copy\n{uris}"
        print(f"[복사] {len(paths)}개 파일 복사 시도")
        for p in paths:
            print(f"  -> {p}")
        print(f"[복사] 클립보드 데이터: {content!r}")

        # xclip을 사용 (PyGObject set_with_data 콜백 문제 우회)
        xclip_path = shutil.which("xclip")
        if xclip_path:
            print(f"[복사] xclip 발견: {xclip_path}")
            try:
                proc = subprocess.Popen(
                    ["xclip", "-selection", "clipboard",
                     "-t", "x-special/gnome-copied-files", "-i"],
                    stdin=subprocess.PIPE,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                )
                proc.stdin.write(content.encode("utf-8"))
                proc.stdin.close()
                print("[복사] xclip 성공")
                return
            except Exception as e:
                print(f"[복사] xclip 예외: {e}")

        # xclip 없으면 xsel 시도
        xsel_path = shutil.which("xsel")
        if xsel_path:
            print(f"[복사] xsel 발견: {xsel_path}")
            try:
                proc = subprocess.Popen(
                    ["xsel", "--clipboard", "--input"],
                    stdin=subprocess.PIPE,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                )
                proc.stdin.write(content.encode("utf-8"))
                proc.stdin.close()
                print("[복사] xsel 성공")
                return
            except Exception as e:
                print(f"[복사] xsel 예외: {e}")

        # 최후 수단: GTK 클립보드 (text/plain 으로라도 저장)
        print("[복사] xclip/xsel 없음, GTK 텍스트 폴백 사용")
        clipboard = self._get_clipboard()
        clipboard.set_text(content, -1)
        clipboard.store()
        print("[복사] GTK set_text 완료")

    def _read_clipboard_files(self):
        clipboard = self._get_clipboard()
        atom = Gdk.Atom.intern("x-special/gnome-copied-files", False)
        selection = clipboard.wait_for_contents(atom)
        print(f"[붙여넣기] wait_for_contents 결과: {selection is not None}")
        if not selection:
            # xclip으로 복사한 경우 text/plain에서도 시도
            print("[붙여넣기] gnome-copied-files 없음, text/plain 시도")
            text = clipboard.wait_for_text()
            if text and text.startswith("copy\nfile://"):
                print(f"[붙여넣기] text/plain에서 복사 데이터 발견")
                data = text
            else:
                print(f"[붙여넣기] 클립보드에 파일 데이터 없음 (text={text!r})")
                return None, []
        else:
            data = selection.get_data().decode("utf-8", errors="replace")
        print(f"[붙여넣기] 클립보드 데이터: {data!r}")
        lines = data.strip().split("\n")
        if len(lines) < 2:
            print("[붙여넣기] 데이터 라인 부족")
            return None, []
        action = lines[0]  # "copy" or "cut"
        paths = []
        for uri in lines[1:]:
            uri = uri.strip()
            if uri.startswith("file://"):
                paths.append(unquote(urlparse(uri).path))
        print(f"[붙여넣기] action={action}, paths={paths}")
        return action, paths

    def _has_clipboard_files(self):
        clipboard = self._get_clipboard()
        atom = Gdk.Atom.intern("x-special/gnome-copied-files", False)
        has_gnome = clipboard.wait_is_target_available(atom)
        if has_gnome:
            print("[클립보드] gnome-copied-files 타겟 있음")
            return True
        # xclip text 폴백 확인
        text = clipboard.wait_for_text()
        has_text = bool(text and text.startswith("copy\nfile://"))
        print(f"[클립보드] gnome-copied-files={has_gnome}, text폴백={has_text}")
        return has_text

    def _paste_files(self):
        action, sources = self._read_clipboard_files()
        print(f"[붙여넣기] action={action}, sources={sources}")
        if not sources:
            print("[붙여넣기] 소스 없음, 중단")
            return
        for src in sources:
            if not os.path.exists(src):
                continue
            name = os.path.basename(src)
            dest = os.path.join(self.current_path, name)
            dest = self._get_unique_path(dest)
            try:
                if os.path.isdir(src):
                    shutil.copytree(src, dest)
                else:
                    shutil.copy2(src, dest)
            except Exception as e:
                print(f"붙여넣기 오류 {src}: {e}")
        if action == "cut":
            for src in sources:
                try:
                    if os.path.isdir(src):
                        shutil.rmtree(src)
                    elif os.path.exists(src):
                        os.remove(src)
                except Exception as e:
                    print(f"잘라내기 삭제 오류 {src}: {e}")
            self._get_clipboard().clear()
        self._populate(self.current_path)

    def _duplicate_selected(self):
        for src in self._get_selected_paths():
            dest = self._get_unique_path(src)
            try:
                if os.path.isdir(src):
                    shutil.copytree(src, dest)
                else:
                    shutil.copy2(src, dest)
            except Exception as e:
                print(f"복제 오류 {src}: {e}")
        self._populate(self.current_path)

    def _delete_selected(self):
        paths = self._get_selected_paths()
        if not paths:
            return
        names = "\n".join(os.path.basename(p) for p in paths)
        dialog = Gtk.MessageDialog(
            transient_for=self.get_toplevel(),
            flags=Gtk.DialogFlags.MODAL,
            message_type=Gtk.MessageType.QUESTION,
            buttons=Gtk.ButtonsType.YES_NO,
            text=f"{len(paths)}개 항목을 삭제하시겠습니까?",
        )
        dialog.format_secondary_text(names)
        response = dialog.run()
        dialog.destroy()
        if response == Gtk.ResponseType.YES:
            for p in paths:
                try:
                    result = subprocess.run(["gio", "trash", p], capture_output=True)
                    if result.returncode != 0:
                        if os.path.isdir(p):
                            shutil.rmtree(p)
                        else:
                            os.remove(p)
                except Exception as e:
                    print(f"삭제 오류 {p}: {e}")
            self._populate(self.current_path)

    def _compress_7z(self, paths):
        if not paths:
            return
        # 출력 파일명: 첫 번째 항목 이름 기반
        first_name = os.path.splitext(os.path.basename(paths[0]))[0]
        output = os.path.join(self.current_path, first_name + ".7z")
        # 중복 방지
        output = self._get_unique_path(output)

        print(f"[7z 압축] 대상: {paths}")
        print(f"[7z 압축] 출력: {output}")

        def _run():
            success, msg = compress_7z(paths, output)
            GLib.idle_add(self._on_compress_done, success, msg)

        threading.Thread(target=_run, daemon=True).start()

    def _on_compress_done(self, success, msg):
        self._populate(self.current_path)
        if not success:
            dialog = Gtk.MessageDialog(
                transient_for=self.get_toplevel(),
                flags=Gtk.DialogFlags.MODAL,
                message_type=Gtk.MessageType.ERROR,
                buttons=Gtk.ButtonsType.OK,
                text="압축 실패",
            )
            dialog.format_secondary_text(msg)
            dialog.run()
            dialog.destroy()
        return False

    _ARCHIVE_EXTS = {'.7z', '.zip', '.tar', '.gz', '.bz2', '.xz', '.rar', '.tar.gz', '.tar.bz2', '.tar.xz', '.tgz'}
    _ARCHIVE_APPS = ["file-roller", "engrampa", "ark", "xarchiver"]

    def _find_archive_app(self):
        for app in self._ARCHIVE_APPS:
            if shutil.which(app):
                return app
        return None

    def _is_archive(self, path):
        name = os.path.basename(path).lower()
        for ext in self._ARCHIVE_EXTS:
            if name.endswith(ext):
                return True
        return False

    def _extract_archive(self, archive_path):
        base_name = os.path.splitext(os.path.basename(archive_path))[0]
        # .tar.gz 같은 이중 확장자 처리
        if base_name.endswith('.tar'):
            base_name = base_name[:-4]
        dest_dir = os.path.join(self.current_path, base_name)
        dest_dir = self._get_unique_path(dest_dir)

        print(f"[압축 해제] 대상: {archive_path}")
        print(f"[압축 해제] 출력: {dest_dir}")

        def _run():
            os.makedirs(dest_dir, exist_ok=True)
            try:
                result = subprocess.run(
                    ["7z", "x", archive_path, f"-o{dest_dir}", "-y"],
                    capture_output=True, text=True, timeout=300,
                )
                success = result.returncode == 0
                msg = dest_dir if success else result.stderr
            except subprocess.TimeoutExpired:
                success, msg = False, "압축 해제 시간 초과 (5분)"
            except Exception as e:
                success, msg = False, str(e)
            GLib.idle_add(self._on_extract_done, success, msg)

        threading.Thread(target=_run, daemon=True).start()

    def _on_extract_done(self, success, msg):
        self._populate(self.current_path)
        if not success:
            dialog = Gtk.MessageDialog(
                transient_for=self.get_toplevel(),
                flags=Gtk.DialogFlags.MODAL,
                message_type=Gtk.MessageType.ERROR,
                buttons=Gtk.ButtonsType.OK,
                text="압축 해제 실패",
            )
            dialog.format_secondary_text(msg)
            dialog.run()
            dialog.destroy()
        return False

    def _get_unique_path(self, path):
        if not os.path.exists(path):
            return path
        if os.path.isdir(path):
            base, ext = path, ""
        else:
            base, ext = os.path.splitext(path)
        counter = 1
        while True:
            suffix = " (복사)" if counter == 1 else f" (복사 {counter})"
            new_path = f"{base}{suffix}{ext}"
            if not os.path.exists(new_path):
                return new_path
            counter += 1

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
