import asyncio
from PySide6.QtWidgets import (
    QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, 
    QLabel, QScrollArea, QSystemTrayIcon, QMenu
)
from PySide6.QtGui import QIcon, QAction
from PySide6.QtCore import Slot, Qt

from .widgets import TunnelCard
from .state import AppState, TunnelViewModel
from ..core import ControlClient, TunnelConfig # Core imports

class MainWindow(QMainWindow):
    def __init__(self, client: ControlClient, app_state: AppState):
        super().__init__()
        self.client = client
        self.app_state = app_state
        self.cards = {} # tid -> TunnelCard
        
        self.init_ui()
        self.init_tray()
        self.refresh_tunnels()
        
        # Bind Client Callbacks
        # Note: These are called from asyncio loop, usually thread-safe in qasync?
        # qasync runs on MainThread, so direct UI updates are safe!
        self.client.on_status_change = self.handle_global_status
        self.client.on_tunnel_status_change = self.handle_tunnel_status

    def init_ui(self):
        self.setWindowTitle("MiniTCPTunnel")
        self.resize(400, 600)
        self.setStyleSheet("background-color: #1E1E1E; color: #FFFFFF;")

        central_widget = QWidget()
        self.setCentralWidget(central_widget)
        main_layout = QVBoxLayout(central_widget)
        main_layout.setContentsMargins(20, 20, 20, 20)
        main_layout.setSpacing(15)

        # Title
        title_lbl = QLabel("Active Tunnels")
        title_lbl.setStyleSheet("font-size: 20px; font-weight: bold; color: #FFFFFF;")
        main_layout.addWidget(title_lbl)

        # Connection Status
        self.lbl_conn_status = QLabel("Disconnected")
        self.lbl_conn_status.setStyleSheet("color: #888888; font-size: 13px; margin-bottom: 10px;")
        main_layout.addWidget(self.lbl_conn_status)

        # Scroll Area for Cards
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setStyleSheet("background-color: transparent; border: none;")
        
        self.scroll_content = QWidget()
        self.scroll_content.setStyleSheet("background-color: transparent;") 
        self.scroll_layout = QVBoxLayout(self.scroll_content)
        self.scroll_layout.setAlignment(Qt.AlignTop)
        self.scroll_layout.setSpacing(10)
        
        scroll.setWidget(self.scroll_content)
        main_layout.addWidget(scroll)

    def init_tray(self):
        self.tray = QSystemTrayIcon(self)
        # TODO: Set Icon
        # self.tray.setIcon(QIcon("path/to/icon.png")) 
        self.tray.setToolTip("MiniTCPTunnel")
        
        menu = QMenu()
        show_action = QAction("Show", self)
        show_action.triggered.connect(self.show)
        menu.addAction(show_action)
        
        quit_action = QAction("Quit", self)
        quit_action.triggered.connect(self.close_app)
        menu.addAction(quit_action)
        
        self.tray.setContextMenu(menu)
        self.tray.show()

    def refresh_tunnels(self):
        # Clear existing
        # (For simpler prototype, assume static list from state or rebuild)
        # Assuming app_state.tunnels is populated
        
        for vm in self.app_state.tunnels:
            if vm.tid not in self.cards:
                card = TunnelCard(vm)
                card.request_toggle.connect(self.on_tunnel_toggle)
                self.scroll_layout.addWidget(card)
                self.cards[vm.tid] = card

    @Slot(str)
    def on_tunnel_toggle(self, tid):
        # Find VM
        vm = next((t for t in self.app_state.tunnels if t.tid == tid), None)
        if not vm: return
        
        # Logic: If stopped -> Add to client & Request
        # If active -> Stop (Not implemented in Client yet, but let's assume we can remove)
        
        if vm.status == "Stopped":
            # Create Config
            cfg = TunnelConfig(vm.tid, vm.remote_port, vm.local_host, vm.local_port)
            self.client.add_tunnel(cfg)
            
            # If client connected, request immediately
            if self.client.is_connected:
                asyncio.create_task(self.client.request_open_tunnel(cfg))
                
        # Update UI optimistically or wait for callback
        # Callback will handle it.

    def handle_global_status(self, status):
        self.lbl_conn_status.setText(f"Server: {status}")
        # Color code?

    def handle_tunnel_status(self, tid, status):
        if tid in self.cards:
            # Update VM
            vm = self.cards[tid].tunnel_vm
            vm.status = status
            self.cards[tid].update_state()

    def closeEvent(self, event):
        # Minimize to tray instead of quitting
        if self.tray.isVisible():
            self.hide()
            event.ignore()
        else:
            event.accept()

    def close_app(self):
        self.tray.hide()
        # Stop loop?
        # We need to signal main to stop.
        # For qasync, closing main window usually doesn't stop loop unless configured.
        # We can just exit sys.
        import sys
        sys.exit(0)
