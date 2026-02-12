import gi
gi.require_version('Gtk', '3.0')
from gi.repository import Gtk, GObject, GdkPixbuf, Gdk
import os
import datetime

# Debug log file
DEBUG_LOG = "/tmp/tabbyfiles_drag_debug.log"

def log_debug(message):
    """디버그 로그 작성"""
    timestamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S.%f")[:-3]
    with open(DEBUG_LOG, "a", encoding="utf-8") as f:
        f.write(f"[{timestamp}] {message}\n")
        f.flush()


class Sidebar(Gtk.Box):
    __gsignals__ = {
        'path-selected': (GObject.SignalFlags.RUN_FIRST, None, (str,)),
    }

    def __init__(self, config_manager):
        super().__init__(orientation=Gtk.Orientation.VERTICAL)
        self.config_manager = config_manager
        self.drag_hover_row = None  # Track which row is being hovered during drag

        # Clear debug log on start
        with open(DEBUG_LOG, "w", encoding="utf-8") as f:
            f.write("=== TabbyFiles Drag & Drop Debug Log ===\n")
        log_debug("Sidebar initialized")

        # Scrolled window for the list
        scrolled = Gtk.ScrolledWindow()
        scrolled.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
        scrolled.set_vexpand(True)
        self.pack_start(scrolled, True, True, 0)

        # ListBox
        self.listbox = Gtk.ListBox()
        self.listbox.set_selection_mode(Gtk.SelectionMode.NONE)  # Disable selection to allow drag

        self.listbox.get_style_context().add_class("sidebar")
        self.listbox.connect("row-activated", self.on_row_activated)
        self.listbox.connect("button-press-event", self.on_button_press)

        # Drag and Drop Destination (reordering)
        target_entry = Gtk.TargetEntry.new("application/x-alien-shortcut", 0, 0)
        self.listbox.drag_dest_set(Gtk.DestDefaults.ALL, [target_entry], Gdk.DragAction.MOVE)
        self.listbox.connect("drag-data-received", self.on_drag_data_received)
        self.listbox.connect("drag-motion", self.on_drag_motion)
        self.listbox.connect("drag-leave", self.on_drag_leave)

        scrolled.add(self.listbox)

        # Add button
        self.add_btn = Gtk.Button()
        try:
            pixbuf = GdkPixbuf.Pixbuf.new_from_file_at_scale(
                "src/resources/icons/add.svg", 24, 24, True
            )
            self.add_btn.set_image(Gtk.Image.new_from_pixbuf(pixbuf))
        except Exception:
            self.add_btn.set_label("+")
        self.add_btn.get_style_context().add_class("add-button")
        self.add_btn.set_size_request(-1, 40)
        self.add_btn.connect("clicked", self.on_add_clicked)
        self.pack_start(self.add_btn, False, False, 0)

        self.refresh_shortcuts()

    def refresh_shortcuts(self):
        log_debug(f"refresh_shortcuts: {len(self.config_manager.shortcuts)} shortcuts")
        # Remove all rows
        for child in self.listbox.get_children():
            self.listbox.remove(child)

        target_entry = Gtk.TargetEntry.new("application/x-alien-shortcut", 0, 0)

        for i, shortcut in enumerate(self.config_manager.shortcuts):
            row = Gtk.ListBoxRow()
            row.set_can_focus(False)  # Prevent row from taking focus
            row.index = i  # Store index for DND
            log_debug(f"  Row {i}: {shortcut['name']} (path={shortcut['path']})")

            # EventBox to capture events properly
            event_box = Gtk.EventBox()
            event_box.set_above_child(False)
            row.add(event_box)

            hbox = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            event_box.add(hbox)

            # Drag and Drop Source - set on EventBox instead of Row
            event_box.drag_source_set(Gdk.ModifierType.BUTTON1_MASK, [target_entry], Gdk.DragAction.MOVE)
            event_box.connect("drag-begin", self.on_drag_begin, row)
            event_box.connect("drag-data-get", self.on_drag_data_get, row)
            event_box.connect("drag-end", self.on_drag_end, row)
            event_box.row = row  # Store reference to row

            # Folder icon
            try:
                pixbuf = GdkPixbuf.Pixbuf.new_from_file_at_scale(
                    "src/resources/icons/folder.svg", 16, 16, True
                )
                icon = Gtk.Image.new_from_pixbuf(pixbuf)
                hbox.pack_start(icon, False, False, 0)
            except Exception:
                pass

            label = Gtk.Label(label=shortcut["name"])
            label.set_xalign(0)
            hbox.pack_start(label, True, True, 0)

            # Store path
            row.path = shortcut["path"]
            row.show_all()
            self.listbox.add(row)

    def on_row_activated(self, listbox, row):
        if row and hasattr(row, 'path'):
            self.emit('path-selected', row.path)

    def update_selection(self, current_path):
        """현재 경로가 숏컷과 일치하면 하이라이트 표시 (selection mode가 NONE이므로 CSS로 처리)"""
        for row in self.listbox.get_children():
            if hasattr(row, 'path') and row.path == current_path:
                row.get_style_context().add_class("selected-shortcut")
            else:
                row.get_style_context().remove_class("selected-shortcut")

    def on_button_press(self, widget, event):
        if event.button == 3:  # Right click
            row = self.listbox.get_row_at_y(int(event.y))
            self.show_context_menu(event, row)
            return True
        return False

    def show_context_menu(self, event, row):
        menu = Gtk.Menu()

        add_item = Gtk.MenuItem(label="바로가기 추가")
        add_item.connect("activate", lambda w: self.on_add_clicked(None))
        menu.append(add_item)

        if row:
            remove_item = Gtk.MenuItem(label="바로가기 삭제")
            remove_item.connect("activate", lambda w: self.remove_item(row))
            menu.append(remove_item)

        menu.show_all()
        menu.popup_at_pointer(event)

    def on_add_clicked(self, button):
        dialog = Gtk.FileChooserDialog(
            title="폴더 선택",
            parent=self.get_toplevel(),
            action=Gtk.FileChooserAction.SELECT_FOLDER,
        )
        dialog.add_buttons(
            Gtk.STOCK_CANCEL, Gtk.ResponseType.CANCEL,
            Gtk.STOCK_OPEN, Gtk.ResponseType.OK,
        )
        response = dialog.run()
        path = dialog.get_filename()
        dialog.destroy()

        if response == Gtk.ResponseType.OK and path:
            self.add_path_to_shortcuts(path)

    def add_path_to_shortcuts(self, path, name=None):
        """바로가기에 경로를 추가합니다. name이 제공되지 않으면 폴더명을 사용합니다."""
        if name is None:
            name = os.path.basename(path) or path
        self.config_manager.add_shortcut(name, path)
        self.refresh_shortcuts()

    # DND Handlers
    def on_drag_begin(self, widget, drag_context, row):
        log_debug(f"on_drag_begin: Starting drag from row index={row.index}")

    def on_drag_end(self, widget, drag_context, row):
        log_debug(f"on_drag_end: Drag ended for row index={row.index}")

    def on_drag_data_get(self, widget, drag_context, data, info, time, row):
        log_debug(f"on_drag_data_get: row.index={row.index}")
        data.set(data.get_target(), 8, str(row.index).encode('utf-8'))

    def on_drag_motion(self, widget, drag_context, x, y, time):
        """드래그 중 마우스가 움직일 때 드롭 위치 표시"""
        target_row = self.listbox.get_row_at_y(y)
        target_index = target_row.index if target_row and hasattr(target_row, 'index') else None
        log_debug(f"on_drag_motion: x={x}, y={y}, target_index={target_index}")

        # Clear previous highlight
        if self.drag_hover_row and self.drag_hover_row != target_row:
            self.drag_hover_row.get_style_context().remove_class("drag-hover-top")
            self.drag_hover_row.get_style_context().remove_class("drag-hover-bottom")

        if target_row:
            # Determine if we should show indicator above or below the row
            row_alloc = target_row.get_allocation()
            row_center = row_alloc.y + row_alloc.height / 2

            # Remove both classes first
            target_row.get_style_context().remove_class("drag-hover-top")
            target_row.get_style_context().remove_class("drag-hover-bottom")

            # Add appropriate class based on position
            if y < row_center:
                target_row.get_style_context().add_class("drag-hover-top")
                log_debug(f"  -> Added drag-hover-top to row {target_index}")
            else:
                target_row.get_style_context().add_class("drag-hover-bottom")
                log_debug(f"  -> Added drag-hover-bottom to row {target_index}")

            self.drag_hover_row = target_row

        # Don't interfere with default drag handling
        return False

    def on_drag_leave(self, widget, drag_context, time):
        """드래그가 영역을 벗어날 때 하이라이트 제거"""
        log_debug("on_drag_leave: clearing highlight")
        if self.drag_hover_row:
            self.drag_hover_row.get_style_context().remove_class("drag-hover-top")
            self.drag_hover_row.get_style_context().remove_class("drag-hover-bottom")
            self.drag_hover_row = None

    def on_drag_data_received(self, widget, drag_context, x, y, data, info, time):
        log_debug(f"on_drag_data_received: x={x}, y={y}")

        # Clear drag hover highlight
        if self.drag_hover_row:
            self.drag_hover_row.get_style_context().remove_class("drag-hover-top")
            self.drag_hover_row.get_style_context().remove_class("drag-hover-bottom")
            self.drag_hover_row = None

        source_index_bytes = data.get_data()
        if not source_index_bytes:
            log_debug("  ERROR: No data received")
            return

        try:
            source_index = int(source_index_bytes.decode('utf-8'))
            log_debug(f"  source_index={source_index}")
        except ValueError as e:
            log_debug(f"  ERROR: Failed to decode source index: {e}")
            return

        target_row = self.listbox.get_row_at_y(y)
        target_index = target_row.index if target_row and hasattr(target_row, 'index') else None
        log_debug(f"  target_row exists={target_row is not None}, target_index={target_index}")

        # Prevent self-drop optimization
        if target_row and hasattr(target_row, 'index') and target_row.index == source_index:
            log_debug("  SKIPPED: Self-drop detected")
            return

        item = self.config_manager.shortcuts.pop(source_index)
        log_debug(f"  Popped item: {item['name']}")

        if target_row and hasattr(target_row, 'index'):
            target_index = target_row.index

            # Determine if we should insert above or below based on mouse position
            row_alloc = target_row.get_allocation()
            row_center = row_alloc.y + row_alloc.height / 2
            insert_below = (y >= row_center)

            log_debug(f"  row_center={row_center}, y={y}, insert_below={insert_below}")

            if insert_below:
                target_index += 1
                log_debug(f"  Adjusted target_index to {target_index} (inserting below)")

            # If moving down, we need to adjust index because of the pop
            if source_index < target_index:
                target_index -= 1

            log_debug(f"  Final insert index: {target_index}")
            self.config_manager.shortcuts.insert(target_index, item)
        else:
            log_debug("  Appending to end")
            self.config_manager.shortcuts.append(item)

        self.config_manager.save_shortcuts(self.config_manager.shortcuts)
        log_debug("  Saved shortcuts, calling refresh")
        self.refresh_shortcuts()
        log_debug("  DONE")

    def remove_item(self, row):
        index = row.get_index()
        self.config_manager.remove_shortcut(index)
        self.refresh_shortcuts()
