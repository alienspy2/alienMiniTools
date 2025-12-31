using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Text.Json.Serialization;

namespace RemoteKM.Client;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _hostBox = new();
    private readonly TextBox _portBox = new();
    private readonly TextBox _hotKeyBox = new();
    private readonly ComboBox _edgeBox = new();
    private readonly CheckBox _serviceToggle = new();
    private readonly Label _serviceStatusLabel = new();
    private readonly string _settingsPath;
    private readonly Action<ClientSettings> _apply;
    private bool _updatingServiceToggle;
    private bool _initializing;

    private const string ServiceName = "RemoteKMClient";

    internal SettingsForm(string settingsPath, ClientSettings settings, Action<ClientSettings> apply)
    {
        _settingsPath = settingsPath;
        _apply = apply;
        _initializing = true;

        Text = "RemoteKM Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        _hostBox.Text = settings.Host;
        _portBox.Text = settings.Port.ToString();
        _hotKeyBox.Text = settings.HotKey;
        _edgeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _edgeBox.Items.AddRange(new object[]
        {
            CaptureEdge.None,
            CaptureEdge.Left,
            CaptureEdge.Right,
            CaptureEdge.Top,
            CaptureEdge.Bottom
        });
        _edgeBox.SelectedItem = settings.CaptureEdge;
        if (_edgeBox.SelectedItem == null)
        {
            _edgeBox.SelectedItem = CaptureEdge.None;
        }
        _hotKeyBox.ReadOnly = true;
        _hotKeyBox.ShortcutsEnabled = false;
        _hotKeyBox.KeyDown += HotKeyBoxOnKeyDown;
        _hostBox.Leave += (_, _) => SaveIfValid(showError: false);
        _portBox.Leave += (_, _) => SaveIfValid(showError: true);
        _edgeBox.SelectedIndexChanged += (_, _) => SaveIfValid(showError: false);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "Host", AutoSize = true }, 0, 0);
        layout.Controls.Add(_hostBox, 1, 0);
        layout.Controls.Add(new Label { Text = "Port", AutoSize = true }, 0, 1);
        layout.Controls.Add(_portBox, 1, 1);
        layout.Controls.Add(new Label { Text = "HotKey", AutoSize = true }, 0, 2);
        layout.Controls.Add(_hotKeyBox, 1, 2);
        layout.Controls.Add(new Label { Text = "Capture Edge", AutoSize = true }, 0, 3);
        layout.Controls.Add(_edgeBox, 1, 3);

        _serviceToggle.Appearance = Appearance.Button;
        _serviceToggle.AutoSize = true;
        _serviceToggle.TextAlign = ContentAlignment.MiddleCenter;
        _serviceStatusLabel.AutoSize = true;
        UpdateServiceToggle(IsServiceRegistered(ServiceName));
        _serviceToggle.CheckedChanged += (_, _) => ToggleServiceRegistration();
        var servicePanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        servicePanel.Controls.Add(_serviceToggle);
        servicePanel.Controls.Add(_serviceStatusLabel);
        layout.Controls.Add(new Label { Text = "Service", AutoSize = true }, 0, 4);
        layout.Controls.Add(servicePanel, 1, 4);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };

        var closeButton = new Button { Text = "Close", AutoSize = true };
        closeButton.Click += (_, _) => Close();
        buttonPanel.Controls.Add(closeButton);

        layout.Controls.Add(buttonPanel, 0, 5);
        layout.SetColumnSpan(buttonPanel, 2);

        Controls.Add(layout);
        _initializing = false;
    }

    private void HotKeyBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;

        var key = e.KeyCode;
        if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
        {
            return;
        }

        _hotKeyBox.Text = FormatHotKey(key, e.Modifiers);
        SaveIfValid(showError: false);
    }

    private static string FormatHotKey(Keys key, Keys modifiers)
    {
        var parts = new List<string>(4);
        if ((modifiers & Keys.Control) != 0)
        {
            parts.Add("CTRL");
        }

        if ((modifiers & Keys.Alt) != 0)
        {
            parts.Add("ALT");
        }

        if ((modifiers & Keys.Shift) != 0)
        {
            parts.Add("SHIFT");
        }

        parts.Add(key.ToString().ToUpperInvariant());
        return string.Join('+', parts);
    }

    private void SaveIfValid(bool showError)
    {
        if (_initializing)
        {
            return;
        }

        if (!int.TryParse(_portBox.Text, out var port))
        {
            if (showError)
            {
                MessageBox.Show("Port must be a number.", "RemoteKM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _portBox.Focus();
            }
            return;
        }

        var settings = new ClientSettings(_hostBox.Text.Trim(), port, _hotKeyBox.Text.Trim(), (CaptureEdge)_edgeBox.SelectedItem!);
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            });
            File.WriteAllText(_settingsPath, json);
            _apply(settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save settings: {ex.Message}", "RemoteKM", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ToggleServiceRegistration()
    {
        if (_updatingServiceToggle)
        {
            return;
        }

        _serviceToggle.Enabled = false;
        try
        {
            var targetRegister = _serviceToggle.Checked;
            var success = targetRegister ? RegisterService() : UnregisterService();
            if (!success)
            {
                UpdateServiceToggle(!targetRegister);
                return;
            }

            MessageBox.Show(this,
                targetRegister ? "서비스 등록 완료" : "서비스 해제 완료",
                "RemoteKM Client",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        finally
        {
            _serviceToggle.Enabled = true;
        }

        UpdateServiceToggle(IsServiceRegistered(ServiceName));
    }

    private void UpdateServiceToggle(bool registered)
    {
        _updatingServiceToggle = true;
        _serviceToggle.Checked = registered;
        _serviceToggle.Text = registered ? "Service 해제" : "Service 등록";
        _serviceStatusLabel.Text = registered ? "등록됨" : "미등록";
        _updatingServiceToggle = false;
    }

    private static bool RegisterService()
    {
        var exePath = Application.ExecutablePath;
        var args = $"create \"{ServiceName}\" binPath= \"{exePath}\" start= auto";
        return RunSc(args, out var output, out var error, out var exitCode)
            || ShowServiceError("서비스 등록 실패", output, error, exitCode);
    }

    private static bool UnregisterService()
    {
        RunSc($"stop \"{ServiceName}\"", out _, out _, out _);
        var args = $"delete \"{ServiceName}\"";
        return RunSc(args, out var output, out var error, out var exitCode)
            || ShowServiceError("서비스 해제 실패", output, error, exitCode);
    }

    private static bool IsServiceRegistered(string name)
    {
        return RunSc($"query \"{name}\"", out _, out _, out var exitCode) && exitCode == 0;
    }

    private static bool RunSc(string arguments, out string output, out string error, out int exitCode)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("sc.exe", arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        output = process.StandardOutput.ReadToEnd();
        error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        exitCode = process.ExitCode;
        return exitCode == 0;
    }

    private static bool ShowServiceError(string title, string output, string error, int exitCode)
    {
        var detail = !string.IsNullOrWhiteSpace(error) ? error : output;
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = $"sc.exe exit code: {exitCode}";
        }

        MessageBox.Show(detail.Trim(), title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }
}
