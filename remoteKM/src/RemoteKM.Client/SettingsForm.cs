using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace RemoteKM.Client;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _hostBox = new();
    private readonly TextBox _portBox = new();
    private readonly TextBox _hotKeyBox = new();
    private readonly ComboBox _edgeBox = new();
    private readonly CheckBox _runAtStartupBox = new();
    private readonly string _settingsPath;
    private readonly Action<ClientSettings> _apply;
    private ClientSettings _currentSettings;
    private bool _startupEnabled;
    private bool _suppressEvents;

    internal SettingsForm(string settingsPath, ClientSettings settings, Action<ClientSettings> apply)
    {
        _settingsPath = settingsPath;
        _apply = apply;
        _currentSettings = settings;

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

        _runAtStartupBox.Text = "Run at startup";
        _runAtStartupBox.AutoSize = true;
        try
        {
            _startupEnabled = StartupTaskManager.IsEnabled();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to read startup task: {ex.Message}", "RemoteKM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _startupEnabled = false;
        }
        _runAtStartupBox.Checked = _startupEnabled;

        _hostBox.TextChanged += (_, _) => ApplyIfChanged();
        _portBox.TextChanged += (_, _) => ApplyIfChanged();
        _edgeBox.SelectedIndexChanged += (_, _) => ApplyIfChanged();
        _runAtStartupBox.CheckedChanged += (_, _) => ApplyIfChanged();

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
        layout.Controls.Add(_runAtStartupBox, 0, 4);
        layout.SetColumnSpan(_runAtStartupBox, 2);

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
        CancelButton = closeButton;
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
        ApplyIfChanged();
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

    private void ApplyIfChanged()
    {
        if (_suppressEvents)
        {
            return;
        }

        if (TryBuildSettings(out var settings) && !settings.Equals(_currentSettings))
        {
            if (PersistSettings(settings))
            {
                _currentSettings = settings;
                _apply(settings);
            }
        }

        UpdateStartupTask();
    }

    private bool TryBuildSettings(out ClientSettings settings)
    {
        settings = _currentSettings;
        if (!int.TryParse(_portBox.Text.Trim(), out var port))
        {
            return false;
        }

        if (port < 1 || port > 65535)
        {
            return false;
        }

        var edge = _edgeBox.SelectedItem is CaptureEdge captureEdge ? captureEdge : CaptureEdge.None;
        settings = new ClientSettings(_hostBox.Text.Trim(), port, _hotKeyBox.Text.Trim(), edge);
        return true;
    }

    private bool PersistSettings(ClientSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            });
            File.WriteAllText(_settingsPath, json);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save settings: {ex.Message}", "RemoteKM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void UpdateStartupTask()
    {
        var requested = _runAtStartupBox.Checked;
        if (requested == _startupEnabled)
        {
            return;
        }

        try
        {
            StartupTaskManager.SetEnabled(requested);
            _startupEnabled = requested;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to update startup task: {ex.Message}", "RemoteKM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _suppressEvents = true;
            _runAtStartupBox.Checked = _startupEnabled;
            _suppressEvents = false;
        }
    }
}
