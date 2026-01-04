from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QFormLayout, QLineEdit, 
    QDialogButtonBox, QMessageBox, QCheckBox
)

class AddTunnelDialog(QDialog):
    def __init__(self, parent=None, current_data=None):
        super().__init__(parent)
        self.setWindowTitle("Edit Tunnel" if current_data else "Add New Tunnel")
        self.setFixedWidth(350)
        self.current_data = current_data
        self.init_ui()

    def init_ui(self):
        layout = QVBoxLayout(self)

        form = QFormLayout()
        
        # Pre-fill values
        id_val = self.current_data['id'] if self.current_data else ""
        remote_val = str(self.current_data['remote_port']) if self.current_data else ""
        local_host_val = self.current_data['local_host'] if self.current_data else "127.0.0.1"
        local_port_val = str(self.current_data['local_port']) if self.current_data else ""
        auto_start_val = bool(self.current_data.get('auto_start')) if self.current_data else False

        self.inp_id = QLineEdit(id_val)
        self.inp_id.setPlaceholderText("e.g. web-server-1")
        
        self.inp_remote = QLineEdit(remote_val)
        self.inp_remote.setPlaceholderText("e.g. 8080")
        
        self.inp_local_host = QLineEdit(local_host_val)
        
        self.inp_local_port = QLineEdit(local_port_val)
        self.inp_local_port.setPlaceholderText("e.g. 80")
        
        # Auto Start 체크박스
        self.chk_auto_start = QCheckBox("Auto Start")
        self.chk_auto_start.setChecked(auto_start_val)

        form.addRow("Tunnel ID:", self.inp_id)
        form.addRow("Remote Port:", self.inp_remote)
        form.addRow("Local Host:", self.inp_local_host)
        form.addRow("Local Port:", self.inp_local_port)
        form.addRow("Auto Start:", self.chk_auto_start)
        
        layout.addLayout(form)

        btns = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        btns.accepted.connect(self.accept)
        btns.rejected.connect(self.reject)
        layout.addWidget(btns)

    def get_data(self):
        return {
            "id": self.inp_id.text().strip(),
            "remote_port": int(self.inp_remote.text().strip()),
            "local_host": self.inp_local_host.text().strip(),
            "local_port": int(self.inp_local_port.text().strip()),
            "auto_start": self.chk_auto_start.isChecked()
        }
    
    def accept(self):
        # Validate
        try:
            d = self.get_data()
            if not d['id']: raise ValueError("ID is required")
            if not (1 <= d['remote_port'] <= 65535): raise ValueError("Invalid Remote Port")
            if not (1 <= d['local_port'] <= 65535): raise ValueError("Invalid Local Port")
        except ValueError as e:
            QMessageBox.warning(self, "Invalid Input", str(e))
            return
            
        super().accept()
