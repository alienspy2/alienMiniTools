import gi
gi.require_version('Gtk', '3.0')
from gi.repository import Gtk, Gdk
from src.ui.mainwindow import MainWindow

CSS = b"""
/* Global Dark Theme */
window, dialog {
    background-color: #2b2b2b;
    color: #dddddd;
}

.sidebar {
    background-color: #1e1e1e;
}

.sidebar row {
    padding: 8px 10px;
    color: #dddddd;
}

.sidebar row:selected {
    background-color: #3e3e3e;
    color: #ffffff;
}

.sidebar row:hover {
    background-color: #2e2e2e;
}

.add-button {
    background-color: #3e3e3e;
    color: #ffffff;
    border: none;
    padding: 8px;
    font-size: 14px;
}

.add-button:hover {
    background-color: #4e4e4e;
}

.address-bar {
    background-color: #2b2b2b;
    color: #aaaaaa;
    border: none;
    border-bottom: 1px solid #3e3e3e;
    padding: 8px;
    font-size: 14px;
}

.address-bar:hover {
    background-color: #333333;
}

.up-button {
    background-color: #2b2b2b;
    border: none;
    border-bottom: 1px solid #3e3e3e;
    border-right: 1px solid #3e3e3e;
    padding: 4px 8px;
}

.up-button:hover {
    background-color: #3e3e3e;
}

.filelist-view {
    background-color: #1e1e1e;
    color: #dddddd;
    font-size: 14px;
}

.filelist-view:selected {
    background-color: #3e3e3e;
    color: #ffffff;
}

.filelist-view header button {
    background-color: #2b2b2b;
    color: #aaaaaa;
    border: none;
    border-right: 1px solid #3e3e3e;
    padding: 4px 8px;
}

.dialog-label {
    color: #dddddd;
    font-size: 14px;
    font-weight: bold;
}

.script-view text {
    background-color: #1e1e1e;
    color: #dddddd;
    font-family: monospace;
}

.allow-button {
    background-color: #2e7d32;
    color: white;
    font-weight: bold;
    padding: 8px;
}

.deny-button {
    background-color: #c62828;
    color: white;
    padding: 8px;
}

.generate-button {
    background-color: #3e3e3e;
    color: #ffffff;
    border: none;
    padding: 8px;
    font-size: 14px;
}

.generate-button:hover {
    background-color: #4e4e4e;
}

.paned-handle {
    background-color: #3a3a3a;
}
"""

def main():
    # Apply CSS
    style_provider = Gtk.CssProvider()
    style_provider.load_from_data(CSS)
    Gtk.StyleContext.add_provider_for_screen(
        Gdk.Screen.get_default(),
        style_provider,
        Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION
    )

    # Prefer dark theme
    settings = Gtk.Settings.get_default()
    settings.set_property("gtk-application-prefer-dark-theme", True)

    window = MainWindow()
    window.connect("destroy", Gtk.main_quit)
    window.show_all()

    Gtk.main()

if __name__ == "__main__":
    main()
