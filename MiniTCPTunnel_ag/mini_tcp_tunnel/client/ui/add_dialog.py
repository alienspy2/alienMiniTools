from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QFormLayout, QLineEdit, 
    QDialogButtonBox, QMessageBox
)

class AddTunnelDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Add New Tunnel")
        self.setFixedWidth(350)
        self.init_ui()

    def init_ui(self):
        layout = QVBoxLayout(self)

        form = QFormLayout()
        self.inp_id = QLineEdit()
        self.inp_id.setPlaceholderText("e.g. web-server-1")
        
        self.inp_remote = QLineEdit()
        self.inp_remote.setPlaceholderText("e.g. 8080")
        
        self.inp_local_host = QLineEdit("127.0.0.1")
        
        self.inp_local_port = QLineEdit()
        self.inp_local_port.setPlaceholderText("e.g. 80")

        form.addRow("Tunnel ID:", self.inp_id)
        form.addRow("Remote Port:", self.inp_remote)
        form.addRow("Local Host:", self.inp_local_host)
        form.addRow("Local Port:", self.inp_local_port)
        
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
            "local_port": int(self.inp_local_port.text().strip())
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
