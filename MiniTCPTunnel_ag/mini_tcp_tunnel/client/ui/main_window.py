import asyncio
from PySide6.QtWidgets import (
    QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, 
    QLabel, QScrollArea, QSystemTrayIcon, QMenu, QPushButton, QMessageBox
)
from PySide6.QtGui import QIcon, QAction, QPixmap, QPainter, QColor
from PySide6.QtCore import Slot, Qt

from .widgets import TunnelCard
from .add_dialog import AddTunnelDialog
from .state import AppState, TunnelViewModel
from ..core import ControlClient, TunnelConfig
from ..config_manager import ConfigManager, TunnelDefinition

class MainWindow(QMainWindow):
    def __init__(self, client: ControlClient, app_state: AppState, cfg_mgr: ConfigManager):
        super().__init__()
        self.client = client
        self.app_state = app_state
        self.cfg_mgr = cfg_mgr
        self.cards = {} # tid -> TunnelCard
        
        self.init_ui()
        self.init_tray()
        self.refresh_tunnels()
        
        self.client.on_status_change = self.handle_global_status
        self.client.on_tunnel_status_change = self.handle_tunnel_status
        
        # Initial status update for tray
        self.update_tray_icon(False)

    def init_ui(self):
        self.setWindowTitle("MiniTCPTunnel Client")
        self.resize(400, 600)
        self.setStyleSheet("background-color: #1E1E1E; color: #FFFFFF;")

        central_widget = QWidget()
        self.setCentralWidget(central_widget)
        main_layout = QVBoxLayout(central_widget)
        main_layout.setContentsMargins(20, 20, 20, 20)
        main_layout.setSpacing(15)

        # Header Row: Title + Add Button
        header_layout = QHBoxLayout()
        title_lbl = QLabel("Active Tunnels")
        title_lbl.setStyleSheet("font-size: 20px; font-weight: bold; color: #FFFFFF;")
        
        btn_add = QPushButton("+ Add")
        btn_add.setCursor(Qt.PointingHandCursor)
        btn_add.setFixedSize(60, 30)
        btn_add.setStyleSheet("""
            QPushButton {
                background-color: #007ACC; 
                color: white; 
                border-radius: 5px;
                font-weight: bold;
            }
            QPushButton:hover {
                background-color: #008AD8;
            }
        """)
        btn_add.clicked.connect(self.on_add_click)
        
        header_layout.addWidget(title_lbl)
        header_layout.addStretch()
        header_layout.addWidget(btn_add)
        
        main_layout.addLayout(header_layout)

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

    def generate_tray_icon(self, connected: bool) -> QIcon:
        size = 64
        pixmap = QPixmap(size, size)
        pixmap.fill(Qt.transparent)
        
        painter = QPainter(pixmap)
        painter.setRenderHint(QPainter.Antialiasing)
        
        # Draw Background Circle
        color = QColor("#00FF7F") if connected else QColor("#FF4500")
        painter.setBrush(color)
        painter.setPen(Qt.NoPen)
        painter.drawEllipse(0, 0, size, size)
        
        # Draw 'T' text
        painter.setPen(QColor("#FFFFFF"))
        font = painter.font()
        font.setPixelSize(int(size * 0.6))
        font.setBold(True)
        painter.setFont(font)
        painter.drawText(pixmap.rect(), Qt.AlignCenter, "T")
        
        painter.end()
        return QIcon(pixmap)

    def init_tray(self):
        self.tray = QSystemTrayIcon(self)
        self.tray.setToolTip("MiniTCPTunnel: Disconnected")
        
        # Set initial icon
        self.tray.setIcon(self.generate_tray_icon(False))
        
        menu = QMenu()
        show_action = QAction("Open Window", self)
        show_action.triggered.connect(self.show_window)
        menu.addAction(show_action)
        
        quit_action = QAction("Quit App", self)
        quit_action.triggered.connect(self.close_app)
        menu.addAction(quit_action)
        
        self.tray.setContextMenu(menu)
        self.tray.activated.connect(self.on_tray_activated)
        self.tray.show()

    def update_tray_icon(self, connected: bool):
        self.tray.setIcon(self.generate_tray_icon(connected))
        status = "Connected" if connected else "Disconnected"
        self.tray.setToolTip(f"MiniTCPTunnel: {status}")

    def on_tray_activated(self, reason):
        if reason == QSystemTrayIcon.Trigger:
            self.show_window()

    def show_window(self):
        self.show()
        self.activateWindow()

    def refresh_tunnels(self):
        # Clear/Rebuild is inefficient but simple for now
        # Ideally, diff the list.
        # Let's check for new ones.
        
        existing_ids = set(self.cards.keys())
        current_ids = {vm.tid for vm in self.app_state.tunnels}
        
        # Remove deleted
        for tid in existing_ids - current_ids:
            card = self.cards.pop(tid)
            card.deleteLater() # Remove widget
            
        # Add new
        for vm in self.app_state.tunnels:
            if vm.tid not in self.cards:
                card = TunnelCard(vm)
                card.request_toggle.connect(self.on_tunnel_toggle)
                card.request_delete.connect(self.on_tunnel_delete)
                self.scroll_layout.addWidget(card)
                self.cards[vm.tid] = card
                
    def save_config(self):
        # Sync state to config
        new_defs = []
        for vm in self.app_state.tunnels:
            t_def = TunnelDefinition(
                id=vm.tid,
                remote_port=vm.remote_port,
                local_host=vm.local_host,
                local_port=vm.local_port,
                auto_start=(vm.status == "Active" or vm.status == "Open") # Simple logic: save last active state?
                # Or keep 'auto_start' property in VM too. simplified: default False.
            )
            new_defs.append(t_def)
        
        self.cfg_mgr.config.tunnels = new_defs
        self.cfg_mgr.save()

    @Slot()
    def on_add_click(self):
        dlg = AddTunnelDialog(self)
        if dlg.exec():
            data = dlg.get_data()
            tid = data['id']
            
            # Check duplicate
            if any(t.tid == tid for t in self.app_state.tunnels):
                QMessageBox.warning(self, "Error", f"Tunnel ID '{tid}' already exists.")
                return
            
            vm = TunnelViewModel(tid, data['remote_port'], data['local_host'], data['local_port'])
            self.app_state.tunnels.append(vm)
            self.refresh_tunnels()
            self.save_config()

    @Slot(str)
    def on_tunnel_delete(self, tid):
        # Find VM
        vm = next((t for t in self.app_state.tunnels if t.tid == tid), None)
        if not vm: return
        
        reply = QMessageBox.question(self, "Delete Tunnel", f"Delete tunnel '{tid}'?", QMessageBox.Yes | QMessageBox.No)
        if reply == QMessageBox.No: return
        
        # Stop if active
        if vm.status in ["Active", "Open", "Requested"]:
            asyncio.create_task(self.stop_tunnel(vm))

        # Remove from state
        self.app_state.tunnels.remove(vm)
        self.refresh_tunnels()
        self.save_config()

    @Slot(str)
    def on_tunnel_toggle(self, tid):
        vm = next((t for t in self.app_state.tunnels if t.tid == tid), None)
        if not vm: return
        
        if vm.status == "Stopped":
            cfg = TunnelConfig(vm.tid, vm.remote_port, vm.local_host, vm.local_port)
            self.client.add_tunnel(cfg)
            if self.client.is_connected:
                asyncio.create_task(self.client.request_open_tunnel(cfg))
        else:
            # Stop Request
            asyncio.create_task(self.stop_tunnel(vm))

    async def stop_tunnel(self, vm):
        cfg = TunnelConfig(vm.tid, vm.remote_port, vm.local_host, vm.local_port)
        if self.client.is_connected:
            await self.client.request_close_tunnel(cfg)
        else:
            # Just update local status if disconnected
            vm.status = "Stopped"
            if vm.tid in self.cards: self.cards[vm.tid].update_state()

    def handle_global_status(self, status):
        self.lbl_conn_status.setText(f"Server: {status}")
        is_connected = (status == "Connected")
        self.update_tray_icon(is_connected)

    def handle_tunnel_status(self, tid, status):
        if tid in self.cards:
            vm = self.cards[tid].tunnel_vm
            # Map status strings
            vm.status = status
            self.cards[tid].update_state()

    def closeEvent(self, event):
        # Minimize to tray
        if self.tray.isVisible():
            self.hide()
            event.ignore()
        else:
            event.accept()

    def close_app(self):
        self.tray.hide()
        import sys
        sys.exit(0)
