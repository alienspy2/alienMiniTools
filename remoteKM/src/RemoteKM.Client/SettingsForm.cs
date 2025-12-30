using System.Windows.Forms;
using System.Text.Json.Serialization;

namespace RemoteKM.Client;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _hostBox = new();
    private readonly TextBox _portBox = new();
    private readonly TextBox _hotKeyBox = new();
    private readonly ComboBox _edgeBox = new();
    private readonly string _settingsPath;
    private readonly Action<ClientSettings> _apply;

    internal SettingsForm(string settingsPath, ClientSettings settings, Action<ClientSettings> apply)
    {
        _settingsPath = settingsPath;
        _apply = apply;

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

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
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

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };

        var saveButton = new Button { Text = "Save", AutoSize = true };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true };
        saveButton.Click += (_, _) => SaveAndClose();
        cancelButton.Click += (_, _) => Close();
        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);

        layout.Controls.Add(buttonPanel, 0, 4);
        layout.SetColumnSpan(buttonPanel, 2);

        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
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

    private void SaveAndClose()
    {
        if (!int.TryParse(_portBox.Text, out var port))
        {
            MessageBox.Show("Port must be a number.", "RemoteKM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save settings: {ex.Message}", "RemoteKM", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
