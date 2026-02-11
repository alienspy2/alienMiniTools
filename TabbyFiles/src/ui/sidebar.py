import gi
gi.require_version('Gtk', '3.0')
from gi.repository import Gtk, GObject, GdkPixbuf


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

        for shortcut in self.config_manager.shortcuts:
            row = Gtk.ListBoxRow()
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

    def add_path_to_shortcuts(self, path):
        name = path.split("/")[-1] or path

        # Name input dialog
        dialog = Gtk.Dialog(
            title="바로가기 이름",
            parent=self.get_toplevel(),
            flags=Gtk.DialogFlags.MODAL,
        )
        dialog.add_buttons(
            Gtk.STOCK_CANCEL, Gtk.ResponseType.CANCEL,
            Gtk.STOCK_OK, Gtk.ResponseType.OK,
        )

        content = dialog.get_content_area()
        label = Gtk.Label(label="이름 입력:")
        content.pack_start(label, False, False, 8)
        entry = Gtk.Entry()
        entry.set_text(name)
        entry.connect("activate", lambda w: dialog.response(Gtk.ResponseType.OK))
        content.pack_start(entry, False, False, 8)
        dialog.show_all()

        response = dialog.run()
        text = entry.get_text()
        dialog.destroy()

        if response == Gtk.ResponseType.OK and text:
            self.config_manager.add_shortcut(text, path)
            self.refresh_shortcuts()

    def remove_item(self, row):
        index = row.get_index()
        self.config_manager.remove_shortcut(index)
        self.refresh_shortcuts()
