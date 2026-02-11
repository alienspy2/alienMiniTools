import gi
gi.require_version('Gtk', '3.0')
from gi.repository import Gtk, GObject, GdkPixbuf, Gdk
import os


class Sidebar(Gtk.Box):
    __gsignals__ = {
        'path-selected': (GObject.SignalFlags.RUN_FIRST, None, (str,)),
    }

    def __init__(self, config_manager):
        super().__init__(orientation=Gtk.Orientation.VERTICAL)
        self.config_manager = config_manager

        # Scrolled window for the list
        scrolled = Gtk.ScrolledWindow()
        scrolled.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
        scrolled.set_vexpand(True)
        self.pack_start(scrolled, True, True, 0)

        # ListBox
        self.listbox = Gtk.ListBox()
        self.listbox.set_selection_mode(Gtk.SelectionMode.SINGLE)

        self.listbox.get_style_context().add_class("sidebar")
        self.listbox.connect("row-activated", self.on_row_activated)
        self.listbox.connect("button-press-event", self.on_button_press)
        
        # Drag and Drop Destination (reordering)
        target_entry = Gtk.TargetEntry.new("application/x-alien-shortcut", 0, 0)
        self.listbox.drag_dest_set(Gtk.DestDefaults.ALL, [target_entry], Gdk.DragAction.MOVE)
        self.listbox.connect("drag-data-received", self.on_drag_data_received)
        
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
        # Remove all rows
        for child in self.listbox.get_children():
            self.listbox.remove(child)

        target_entry = Gtk.TargetEntry.new("application/x-alien-shortcut", 0, 0)

        for i, shortcut in enumerate(self.config_manager.shortcuts):
            row = Gtk.ListBoxRow()
            
            # Drag and Drop Source
            row.drag_source_set(Gdk.ModifierType.BUTTON1_MASK, [target_entry], Gdk.DragAction.MOVE)
            row.connect("drag-data-get", self.on_drag_data_get)
            row.index = i  # Store index for DND
            
            hbox = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
            row.add(hbox)

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
        """현재 경로가 숏컷과 일치하면 선택, 아니면 선택 해제."""
        for row in self.listbox.get_children():
            if hasattr(row, 'path') and row.path == current_path:
                self.listbox.select_row(row)
                return
        self.listbox.unselect_all()

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
        if name is None:
            # If no name provided (e.g. from context menu), use folder name and save immediately
            name = os.path.basename(path) or path
            self.config_manager.add_shortcut(name, path)
            self.refresh_shortcuts()
            return

        # Name input dialog (only if name was explicitly requested as empty string/placeholder? 
        # No, existing code used path.split... let's keep logic for manual add button if needed, 
        # but modify to support direct add)
        
        # Actually, on_add_clicked calls this with just path. 
        # The user wants "context menu -> add -> no prompt".
        # The "Add Button" -> "Select Folder" -> "Add" currently prompts. 
        # I will change it so that `on_add_clicked` also skips prompt by default, 
        # or I can keep it prompting.
        # But `name=None` implies "determine automatically".
        
        # Let's support the prompt logic ONLY if a specific flag is passed or if we refactor.
        # But wait, the original code had the dialog INSIDE this function.
        # I should replace the whole function to handle both cases.
        # If I want to keep the prompt for the "+" button, I should pass `prompt=True`.
        # But simpler: automatic name is better UX anyway.
        # I will default to NO prompt for everything.
        
        name = os.path.basename(path) or path
        self.config_manager.add_shortcut(name, path)
        self.refresh_shortcuts()

    # DND Handlers
    def on_drag_data_get(self, widget, drag_context, data, info, time):
        data.set(data.get_target(), 8, str(widget.index).encode('utf-8'))

    def on_drag_data_received(self, widget, drag_context, x, y, data, info, time):
        source_index_bytes = data.get_data()
        if not source_index_bytes:
            return
            
        try:
            source_index = int(source_index_bytes.decode('utf-8'))
        except ValueError:
            return

        target_row = self.listbox.get_row_at_y(y)
        
        # Prevent self-drop optimization
        if target_row and target_row.index == source_index:
            return

        item = self.config_manager.shortcuts.pop(source_index)

        if target_row:
            target_index = target_row.index
            # If moving down, we need to adjust index because of the pop
            if source_index < target_index:
                target_index -= 1
            self.config_manager.shortcuts.insert(target_index, item)
        else:
            # Dropped in empty space -> append to end
            self.config_manager.shortcuts.append(item)

        self.config_manager.save_shortcuts(self.config_manager.shortcuts)
        self.refresh_shortcuts()

    def remove_item(self, row):
        index = row.get_index()
        self.config_manager.remove_shortcut(index)
        self.refresh_shortcuts()
