import asyncio
from PySide6.QtWidgets import (
    QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, 
    QLabel, QScrollArea, QSystemTrayIcon, QMenu, QPushButton, QMessageBox, QLineEdit
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

        # 1. Connection Settings Group
        conn_layout = QHBoxLayout()
        
        self.inp_host = QLineEdit(self.app_state.server_host)
        self.inp_host.setPlaceholderText("Server Host")
        
        self.inp_port = QLineEdit(str(self.app_state.server_port))
        self.inp_port.setPlaceholderText("Port")
        self.inp_port.setFixedWidth(60)
        
        self.btn_connect = QPushButton("Connect")
        self.btn_connect.setCursor(Qt.PointingHandCursor)
        self.btn_connect.setCheckable(True)
        self.btn_connect.setStyleSheet("""
            QPushButton { background-color: #2D2D2D; border: 1px solid #555; border-radius: 4px; color: white; }
            QPushButton:checked { background-color: #007ACC; border-color: #007ACC; }
        """)
        self.btn_connect.clicked.connect(self.on_connect_toggle)
        
        conn_layout.addWidget(QLabel("Server:"))
        conn_layout.addWidget(self.inp_host)
        conn_layout.addWidget(self.inp_port)
        conn_layout.addWidget(self.btn_connect)
        
        main_layout.addLayout(conn_layout)

        # 2. Tunnels Header
        header_layout = QHBoxLayout()
        title_lbl = QLabel("Active Tunnels")
        title_lbl.setStyleSheet("font-size: 16px; font-weight: bold; color: #FFFFFF;")
        
        # 현재 UI 상태(터널 목록/활성 여부)를 서버에 반영하는 버튼
        btn_apply = QPushButton("Apply")
        btn_apply.setCursor(Qt.PointingHandCursor)
        btn_apply.setFixedHeight(30)
        btn_apply.setToolTip("Apply current tunnels to server")
        btn_apply.setStyleSheet("""
            QPushButton {
                background-color: #3A3A3A;
                color: white;
                border-radius: 6px;
                padding: 0 10px;
                font-weight: bold;
            }
            QPushButton:hover { background-color: #4A4A4A; }
        """)
        btn_apply.clicked.connect(self.on_apply_click)

        btn_add = QPushButton("+")
        btn_add.setCursor(Qt.PointingHandCursor)
        btn_add.setFixedSize(30, 30)
        btn_add.setToolTip("Add Tunnel")
        btn_add.setStyleSheet("""
            QPushButton {
                background-color: #444; 
                color: white; 
                border-radius: 15px;
                font-weight: bold; font-size: 18px;
            }
            QPushButton:hover { background-color: #666; }
        """)
        btn_add.clicked.connect(self.on_add_click)
        
        header_layout.addWidget(title_lbl)
        header_layout.addStretch()
        header_layout.addWidget(btn_apply)
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
        # Remove old cards
        for tid, card in list(self.cards.items()):
            # If tunnel removed from state
            if not any(t.tid == tid for t in self.app_state.tunnels):
                self.scroll_layout.removeWidget(card)
                card.deleteLater()
                del self.cards[tid]
            else:
                card.update_state()

        # Add new
        for vm in self.app_state.tunnels:
            if vm.tid not in self.cards:
                card = TunnelCard(vm)
                card.request_enabled_change.connect(self.on_tunnel_enabled_change)
                card.request_edit.connect(self.on_tunnel_edit)
                card.request_delete.connect(self.on_tunnel_delete)
                # My scroll_layout has stretch at end.
                # insertWidget at count-1
                self.scroll_layout.insertWidget(self.scroll_layout.count()-1, card)
                self.cards[vm.tid] = card
                
    @Slot()
    def on_apply_click(self):
        # [1] UI에 있는 터널 정보를 기반으로 "원하는 상태" 구성 리스트를 만든다.
        #     이 리스트는 서버에 적용할 목표 상태(열기/닫기)를 판단하는 기준이 된다.
        configs = []
        for vm in self.app_state.tunnels:
            cfg = TunnelConfig(vm.tid, vm.remote_port, vm.local_host, vm.local_port, enabled=vm.enabled)
            configs.append(cfg)
            # ControlClient 쪽 레지스트리(알고 있는 터널 목록)를 갱신한다.
            # 서버에서 INCOMING_CONN을 받을 때 이 레지스트리를 참조한다.
            self.client.add_tunnel(cfg)
        
        # [2] 서버에 실제 적용할 수 있는지(연결 상태) 확인한다.
        #     연결이 없으면 요청을 보낼 수 없으므로 사용자에게 안내하고 상태를 보정한다.
        if not self.client.is_connected or not self.client.codec:
            QMessageBox.information(self, "Not Connected", "서버에 연결된 뒤 적용해 주세요.")
            # 연결되지 않은 상태에서는 모든 터널이 실제로 열릴 수 없으므로 UI 상태를 Stopped로 맞춘다.
            for vm in self.app_state.tunnels:
                vm.status = "Stopped"
                if vm.tid in self.cards:
                    self.cards[vm.tid].update_state()
            return

        # [3] 연결된 상태에서는 "요청을 보냈음"을 UI에 즉시 반영한다.
        #     서버가 TUNNEL_STATUS를 보내지 않기 때문에, 일단 Requested/Stopped로 표시한다.
        for vm in self.app_state.tunnels:
            vm.status = "Requested" if vm.enabled else "Stopped"
            if vm.tid in self.cards:
                self.cards[vm.tid].update_state()

        # [4] 실제 동기화 요청을 서버로 보낸다(비동기).
        #     ControlClient가 Open/Close 요청을 전송한다.
        asyncio.create_task(self.client.sync_tunnels(configs))

    @Slot(str, bool)
    def on_tunnel_enabled_change(self, tid, enabled):
        vm = next((t for t in self.app_state.tunnels if t.tid == tid), None)
        if vm:
            vm.enabled = enabled
            self.save_config()

    @Slot(str)
    def on_tunnel_edit(self, tid):
        # Find VM
        vm = next((t for t in self.app_state.tunnels if t.tid == tid), None)
        if not vm: return
        # Edit logic: No need to stop first. Just edit Config.
        # If user wants to apply, they hit "Apply".

        # Existing Data
        data = {
            "id": vm.tid,
            "remote_port": vm.remote_port,
            "local_host": vm.local_host,
            "local_port": vm.local_port
        }
        
        dlg = AddTunnelDialog(self, current_data=data)
        if dlg.exec():
            new_data = dlg.get_data()
            new_tid = new_data['id']
            
            # If ID changed, check dup
            if new_tid != tid and any(t.tid == new_tid for t in self.app_state.tunnels):
                 QMessageBox.warning(self, "Error", f"Tunnel ID '{new_tid}' already exists.")
                 return

            # Update VM
            # Ideally we remove old VM and add new one if ID changed to keep things clean
            # because 'tid' is key in 'cards' and 'client.tunnels'.
            
            if new_tid != tid:
                # ID Changed: Remove old, Add new
                self.app_state.tunnels.remove(vm)
                self.refresh_tunnels() # This removes old card
                
                new_vm = TunnelViewModel(new_tid, new_data['remote_port'], new_data['local_host'], new_data['local_port'])
                self.app_state.tunnels.append(new_vm)
                self.refresh_tunnels() # Adds new card
                
                # Update Client Tunnel Map if used? 
                # ControlClient keeps 'tunnels' map by ID. We must update it.
                # MainWindow doesn't directly touch ControlClient internal map usually, 
                # but 'add_tunnel' does. And 'request_open' uses it.
                # We should probably have 'remove_tunnel' in client.
                # Currently client.tunnels has old ID.
                # Let's clean it up.
                if tid in self.client.tunnels:
                    del self.client.tunnels[tid]
                
            else:
                # ID Same, just update fields
                vm.remote_port = new_data['remote_port']
                vm.local_host = new_data['local_host']
                vm.local_port = new_data['local_port']
                # Card update (text)
                if tid in self.cards:
                    # Update labels manually or refresh?
                    # Refresh is safer to redraw label.
                    # But refresh_tunnels won't redraw existing card unless we remove it or update it.
                    # Let's just remove and re-add card logic via refresh or call update_info on card.
                    # Simpler: remove from cards map (not state), call refresh.
                    card = self.cards.pop(tid)
                    card.deleteLater()
                    self.refresh_tunnels()

            self.save_config()

    def save_config(self):
        # Sync state to config
        self.cfg_mgr.config.server_host = self.inp_host.text().strip()
        try:
            self.cfg_mgr.config.server_port = int(self.inp_port.text().strip())
        except:
            pass
            
        new_defs = []
        for vm in self.app_state.tunnels:
            t_def = TunnelDefinition(
                id=vm.tid,
                remote_port=vm.remote_port,
                local_host=vm.local_host,
                local_port=vm.local_port,
                auto_start=vm.enabled  # Use enabled flag for auto_start
            )
            new_defs.append(t_def)
        
        self.cfg_mgr.config.tunnels = new_defs
        self.cfg_mgr.save()

    @Slot()
    def on_connect_toggle(self):
        if self.btn_connect.isChecked():
            # Connect
            host = self.inp_host.text().strip()
            try:
                port = int(self.inp_port.text().strip())
            except ValueError:
                QMessageBox.warning(self, "Error", "Invalid Port")
                self.btn_connect.setChecked(False)
                return

            # Update State & Client
            self.app_state.server_host = host
            self.app_state.server_port = port
            self.client.server_host = host
            self.client.server_port = port
            
            # Save Config immediately
            self.save_config()

            # Trigger Connect
            # Since client.connect() is async and we are in UI slot, we use create_task
            # But wait, main_client might be trying to auto-connect? 
            # If we allow manual control, we should handle it here.
            self.inp_host.setEnabled(False)
            self.inp_port.setEnabled(False)
            self.btn_connect.setText("Disconnecting..." if self.client.is_connected else "Connecting...") 
            # Wait, button text logic is tricky with async. Let status handler set text?
            
            asyncio.create_task(self.do_connect())
        else:
            # Disconnect
            self.inp_host.setEnabled(True)
            self.inp_port.setEnabled(True)
            self.btn_connect.setText("Connect")
            
            asyncio.create_task(self.client.disconnect()) # Need to implement disconnect in ControlClient properly logic

    async def do_connect(self):
        await self.client.connect()
        # Connection is managed in background. 
        # Tunnels will be auto-requested by ControlClient._perform_connect upon success.
        # Wait, MainWindow manual logic 'auto_start_tunnels' logic:
        # MainWindow sends 'add_tunnel' -> 'request_open' manually for auto-start entries?
        # ControlClient._perform_connect executes: for t in self.tunnels.values(): request_open...
        # So if we added tunnels to ControlClient BEFORE connecting, they will auto-open.
        # Check if we did that.
        # main_client.py loads state and adds tunnels to client. So yes.
        # MainWindow.auto_start_tunnels() logic was: "asyncio.create_task(self.on_tunnel_toggle(tid))"
        # which calls 'client.add_tunnel' & 'request_open'.
        # If they are already added, we just need request_open.
        # ControlClient now does 'Request Existing Tunnels' on connect.
        # So we just need to ensure `client.add_tunnel` is called for all config tunnels initially.
        # It is done in main_client startup.
        # So we mostly don't need manual auto_start here anymore, IF the 'status' of VM is set correctly.
        # But VM status is UI side. ControlClient has 'TunnelConfig.status'.
        # Let's ensure User Intention (Auto Start) is respected.
        pass

    def auto_start_tunnels(self):
        # Deprecated logic - ControlClient handles re-requesting added tunnels.
        pass

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
        
        # Update Inputs based on 'Connected' state
        if is_connected:
            self.inp_host.setEnabled(False)
            self.inp_port.setEnabled(False)
            self.btn_connect.setText("Disconnect")
        else:
            # If we are retrying or connecting, keep inputs disabled?
            # 'status' can be "Retry in 30s...", "Connecting...", "Handshake Failed", "Disconnected"
            if "Disconnected" in status:
                self.inp_host.setEnabled(True)
                self.inp_port.setEnabled(True)
                self.btn_connect.setText("Connect")
                self.btn_connect.setChecked(False) # Force Reset logic
            else:
                # Retrying, Connecting, Error...
                # Keep inputs disabled to prevent changing while re-connecting
                self.inp_host.setEnabled(False)
                self.inp_port.setEnabled(False)
                self.btn_connect.setText("Disconnect") # Allow aborting retry
                self.btn_connect.setChecked(True) # Ensure it stays down

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
