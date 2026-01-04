from PySide6.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton, 
    QFrame, QGraphicsDropShadowEffect
)
from PySide6.QtCore import Qt, Signal
from PySide6.QtGui import QColor, QFont

class TunnelCard(QFrame):
    """
    A card widget representing a single tunnel configuration.
    Displays: ID, Remote->Local Map, Status, Control Button
    """
    request_toggle = Signal(str) # Emits tunnel_id
    request_delete = Signal(str) # Emits tunnel_id
    request_edit = Signal(str)

    def __init__(self, tunnel_vm):
        super().__init__()
        self.tunnel_vm = tunnel_vm
        self.init_ui()
        self.update_state()

    def init_ui(self):
        self.setObjectName("TunnelCard")
        self.setStyleSheet("""
            #TunnelCard {
                background-color: #2D2D2D;
                border-radius: 10px;
                border: 1px solid #3E3E3E;
            }
        """)
        
        # Shadow
        shadow = QGraphicsDropShadowEffect(self)
        shadow.setBlurRadius(15)
        shadow.setColor(QColor(0, 0, 0, 80))
        shadow.setOffset(0, 4)
        self.setGraphicsEffect(shadow)

        layout = QVBoxLayout(self)
        
        # Header: ID + Status Dot
        header_layout = QHBoxLayout()
        self.lbl_id = QLabel(self.tunnel_vm.tid)
        self.lbl_id.setStyleSheet("font-weight: bold; font-size: 14px; color: #FFFFFF;")
        
        self.lbl_status_dot = QLabel("●")
        self.lbl_status_dot.setFixedSize(20, 20)
        
        header_layout.addWidget(self.lbl_id)
        header_layout.addStretch()
        header_layout.addWidget(self.lbl_status_dot)
        
        # Body: Port Mapping
        mapping_text = f"Remote :{self.tunnel_vm.remote_port}  ➔  {self.tunnel_vm.local_host}:{self.tunnel_vm.local_port}"
        self.lbl_mapping = QLabel(mapping_text)
        self.lbl_mapping.setStyleSheet("color: #AAAAAA; font-size: 12px;")
        
        # Footer: Controls
        footer_layout = QHBoxLayout()
        
        self.btn_edit = QPushButton("Edit")
        self.btn_edit.setCursor(Qt.PointingHandCursor)
        self.btn_edit.setFixedSize(40, 28)
        self.btn_edit.setStyleSheet("background-color: #444; color: #BBB;")
        self.btn_edit.clicked.connect(self.on_edit_click)

        self.btn_delete = QPushButton("Del")
        self.btn_delete.setCursor(Qt.PointingHandCursor)
        self.btn_delete.setFixedSize(40, 28)
        self.btn_delete.setStyleSheet("background-color: #444; color: #BBB;")
        self.btn_delete.clicked.connect(self.on_delete_click)

        self.btn_toggle = QPushButton("Start")
        self.btn_toggle.setCursor(Qt.PointingHandCursor)
        self.btn_toggle.setFixedSize(80, 28)
        self.btn_toggle.clicked.connect(self.on_toggle_click)
        
        # Status Text
        self.lbl_status_text = QLabel("Stopped")
        self.lbl_status_text.setStyleSheet("color: #888888; font-size: 11px;")

        footer_layout.addWidget(self.lbl_status_text)
        footer_layout.addStretch()
        footer_layout.addWidget(self.btn_edit)
        footer_layout.addWidget(self.btn_delete)
        footer_layout.addWidget(self.btn_toggle)

        layout.addLayout(header_layout)
        layout.addWidget(self.lbl_mapping)
        layout.addLayout(footer_layout)

    def on_toggle_click(self):
        self.request_toggle.emit(self.tunnel_vm.tid)

    def on_delete_click(self):
        self.request_delete.emit(self.tunnel_vm.tid)

    def on_edit_click(self):
        self.request_edit.emit(self.tunnel_vm.tid)

    def update_state(self):
        status = self.tunnel_vm.status
        self.lbl_status_text.setText(status)
        
        if status == "Requested":
            self.lbl_status_dot.setStyleSheet("color: #FFA500;") # Orange
            self.btn_toggle.setText("...")
            self.btn_toggle.setEnabled(False)
        elif status == "Active" or status == "Open": # Need consistent status strings
            self.lbl_status_dot.setStyleSheet("color: #00FF7F;") # Spring Green
            self.btn_toggle.setText("Stop")
            self.btn_toggle.setEnabled(True) # TODO: Implement Stop
        else: # Stopped/Error
            self.lbl_status_dot.setStyleSheet("color: #FF4500;") # OrangeRed
            self.btn_toggle.setText("Start")
            self.btn_toggle.setEnabled(True)
            
        # Update connections count if needed
