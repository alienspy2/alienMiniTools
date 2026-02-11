import gi
gi.require_version('Gtk', '3.0')
from gi.repository import Gtk, GLib
from src.utils.ollama_client import OllamaClient
import subprocess
import threading


class GemmaDialog(Gtk.Dialog):
    def __init__(self, current_path, parent=None):
        super().__init__(
            title="Ask Gemma",
            parent=parent,
            flags=Gtk.DialogFlags.MODAL | Gtk.DialogFlags.DESTROY_WITH_PARENT,
        )
        self.set_default_size(600, 400)
        self.current_path = current_path

        content = self.get_content_area()
        content.set_spacing(8)
        content.set_margin_start(12)
        content.set_margin_end(12)
        content.set_margin_top(12)
        content.set_margin_bottom(12)

        # Instruction Label
        label = Gtk.Label(label=f"'{current_path}' 에서 수행할 작업을 입력하세요:")
        label.set_xalign(0)
        label.get_style_context().add_class("dialog-label")
        content.pack_start(label, False, False, 0)

        # Input prompt (TextView with ScrolledWindow)
        input_scroll = Gtk.ScrolledWindow()
        input_scroll.set_policy(Gtk.PolicyType.AUTOMATIC, Gtk.PolicyType.AUTOMATIC)
        input_scroll.set_size_request(-1, 80)
        input_scroll.set_vexpand(False)

        self.input_view = Gtk.TextView()
        self.input_view.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
        self.input_view.get_style_context().add_class("script-view")
        input_buf = self.input_view.get_buffer()
        input_buf.set_text("")
        # Set placeholder-like behavior
        self.input_view.set_tooltip_text("예: 모든 .txt 파일을 .md로 변경해줘")
        input_scroll.add(self.input_view)
        content.pack_start(input_scroll, False, False, 0)

        # Generate Button
        self.generate_btn = Gtk.Button(label="Bash 스크립트 생성")
        self.generate_btn.get_style_context().add_class("generate-button")
        self.generate_btn.connect("clicked", self.generate_script)
        content.pack_start(self.generate_btn, False, False, 0)

        # Output Label
        output_label = Gtk.Label(label="생성된 스크립트 (수정 가능):")
        output_label.set_xalign(0)
        output_label.get_style_context().add_class("dialog-label")
        content.pack_start(output_label, False, False, 0)

        # Script output (TextView with ScrolledWindow)
        script_scroll = Gtk.ScrolledWindow()
        script_scroll.set_policy(Gtk.PolicyType.AUTOMATIC, Gtk.PolicyType.AUTOMATIC)
        script_scroll.set_vexpand(True)

        self.script_view = Gtk.TextView()
        self.script_view.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
        self.script_view.set_monospace(True)
        self.script_view.get_style_context().add_class("script-view")
        self.script_view.set_tooltip_text("여기에 생성된 bash 스크립트가 표시됩니다.")
        script_scroll.add(self.script_view)
        content.pack_start(script_scroll, True, True, 0)

        # Buttons
        btn_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        btn_box.set_homogeneous(True)
        content.pack_start(btn_box, False, False, 0)

        self.allow_btn = Gtk.Button(label="실행 (Allow)")
        self.allow_btn.get_style_context().add_class("allow-button")
        self.allow_btn.connect("clicked", self.execute_script)
        self.allow_btn.set_sensitive(False)
        btn_box.pack_start(self.allow_btn, True, True, 0)

        self.deny_btn = Gtk.Button(label="취소 (Deny)")
        self.deny_btn.get_style_context().add_class("deny-button")
        self.deny_btn.connect("clicked", lambda w: self.response(Gtk.ResponseType.CANCEL))
        btn_box.pack_start(self.deny_btn, True, True, 0)

        self.show_all()

    def _get_text(self, textview):
        buf = textview.get_buffer()
        return buf.get_text(buf.get_start_iter(), buf.get_end_iter(), False).strip()

    def _set_text(self, textview, text):
        textview.get_buffer().set_text(text)

    def generate_script(self, button):
        prompt = self._get_text(self.input_view)
        if not prompt:
            dialog = Gtk.MessageDialog(
                parent=self,
                flags=Gtk.DialogFlags.MODAL,
                type=Gtk.MessageType.WARNING,
                buttons=Gtk.ButtonsType.OK,
                message_format="작업 내용을 입력해주세요."
            )
            dialog.run()
            dialog.destroy()
            return

        self.generate_btn.set_sensitive(False)
        self.generate_btn.set_label("생성 중...")

        # Run in background thread
        thread = threading.Thread(target=self._generate_in_thread, args=(prompt,))
        thread.daemon = True
        thread.start()

    def _generate_in_thread(self, prompt):
        client = OllamaClient()
        script = client.generate_script(prompt)
        GLib.idle_add(self._on_generation_finished, script)

    def _on_generation_finished(self, script):
        self._set_text(self.script_view, script)
        self.generate_btn.set_sensitive(True)
        self.generate_btn.set_label("Bash 스크립트 생성")
        self.allow_btn.set_sensitive(True)
        return False  # Remove from idle

    def execute_script(self, button):
        script = self._get_text(self.script_view)
        if not script:
            dialog = Gtk.MessageDialog(
                parent=self,
                flags=Gtk.DialogFlags.MODAL,
                type=Gtk.MessageType.WARNING,
                buttons=Gtk.ButtonsType.OK,
                message_format="실행할 스크립트가 없습니다."
            )
            dialog.run()
            dialog.destroy()
            return

        self.allow_btn.set_sensitive(False)
        self.allow_btn.set_label("실행 중...")

        thread = threading.Thread(
            target=self._execute_in_thread, args=(script,)
        )
        thread.daemon = True
        thread.start()

    def _execute_in_thread(self, script):
        try:
            result = subprocess.run(
                ["bash", "-c", script],
                cwd=self.current_path,
                stdin=subprocess.DEVNULL,
                capture_output=True,
                text=True,
                timeout=120
            )
            rc, out, err = result.returncode, result.stdout, result.stderr
            GLib.timeout_add(0, self._on_execution_finished, rc, out, err)
        except subprocess.TimeoutExpired:
            GLib.timeout_add(0, self._on_execution_finished, -1, "", "스크립트 실행 시간 초과 (120초)")
        except Exception as e:
            GLib.timeout_add(0, self._on_execution_finished, -1, "", str(e))

    def _on_execution_finished(self, returncode, stdout, stderr):
        self.allow_btn.set_sensitive(True)
        self.allow_btn.set_label("실행 (Allow)")

        msg = (
            f"실행 완료.\n\n"
            f"Exit Code: {returncode}\n\n"
            f"[STDOUT]\n{stdout}\n\n"
            f"[STDERR]\n{stderr}"
        )
        dialog = Gtk.MessageDialog(
            parent=self,
            flags=Gtk.DialogFlags.MODAL,
            type=Gtk.MessageType.INFO,
            buttons=Gtk.ButtonsType.OK,
            message_format=msg
        )
        dialog.run()
        dialog.destroy()

        if returncode == 0:
            self.response(Gtk.ResponseType.OK)
        return False

