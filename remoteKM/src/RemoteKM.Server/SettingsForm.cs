using System.Net;
using System.Windows.Forms;

namespace RemoteKM.Server;

internal sealed class SettingsForm : Form
{
    private readonly Action<ServerSettings> _apply;
    private readonly TextBox _hostText;
    private readonly NumericUpDown _portUpDown;
    private readonly CheckBox _runAtStartupCheck;
    private ServerSettings _currentSettings;
    private ServerSettings _taskSettings;
    private bool _startupEnabled;
    private bool _suppressEvents;

    internal SettingsForm(ServerSettings settings, Action<ServerSettings> apply)
    {
        _apply = apply;
        _currentSettings = settings;
        _taskSettings = settings;

        Text = "RemoteKM Server Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Width = 360;
        Height = 220;

        var hostLabel = new Label
        {
            Text = "Host",
            Left = 12,
            Top = 20,
            Width = 80
        };
        _hostText = new TextBox
        {
            Left = 100,
            Top = 16,
            Width = 220,
            Text = settings.Host
        };

        var portLabel = new Label
        {
            Text = "Port",
            Left = 12,
            Top = 58,
            Width = 80
        };
        _portUpDown = new NumericUpDown
        {
            Left = 100,
            Top = 54,
            Width = 120,
            Minimum = 1,
            Maximum = 65535,
            Value = settings.Port
        };

        _runAtStartupCheck = new CheckBox
        {
            Text = "Run at startup",
            Left = 100,
            Top = 92,
            AutoSize = true
        };
        try
        {
            _startupEnabled = StartupTaskManager.IsEnabled();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to read startup task: {ex.Message}", "RemoteKM Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _startupEnabled = false;
        }
        _runAtStartupCheck.Checked = _startupEnabled;
        _runAtStartupCheck.CheckedChanged += (_, _) => ApplyIfChanged();

        var closeButton = new Button
        {
            Text = "Close",
            Left = 240,
            Top = 130,
            Width = 80
        };
        closeButton.Click += (_, _) => Close();

        _hostText.TextChanged += (_, _) => ApplyIfChanged();
        _portUpDown.ValueChanged += (_, _) => ApplyIfChanged();

        Controls.Add(hostLabel);
        Controls.Add(_hostText);
        Controls.Add(portLabel);
        Controls.Add(_portUpDown);
        Controls.Add(_runAtStartupCheck);
        Controls.Add(closeButton);

        CancelButton = closeButton;
    }

    private void ApplyIfChanged()
    {
        if (_suppressEvents)
        {
            return;
        }

        var host = _hostText.Text.Trim();
        var port = (int)_portUpDown.Value;
        if (IPAddress.TryParse(host, out _))
        {
            if (!host.Equals(_currentSettings.Host, StringComparison.OrdinalIgnoreCase) || port != _currentSettings.Port)
            {
                _currentSettings = new ServerSettings(host, port);
                _apply(_currentSettings);
            }
        }

        UpdateStartupTask();
    }

    private void UpdateStartupTask()
    {
        var requested = _runAtStartupCheck.Checked;
        if (requested)
        {
            if (_startupEnabled && SettingsMatch(_taskSettings, _currentSettings))
            {
                return;
            }

            try
            {
                StartupTaskManager.SetEnabled(true, _currentSettings);
                _startupEnabled = true;
                _taskSettings = _currentSettings;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to update startup task: {ex.Message}", "RemoteKM Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _suppressEvents = true;
                _runAtStartupCheck.Checked = _startupEnabled;
                _suppressEvents = false;
            }

            return;
        }

        if (!_startupEnabled)
        {
            return;
        }

        try
        {
            StartupTaskManager.SetEnabled(false, _currentSettings);
            _startupEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to update startup task: {ex.Message}", "RemoteKM Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _suppressEvents = true;
            _runAtStartupCheck.Checked = _startupEnabled;
            _suppressEvents = false;
        }
    }

    private static bool SettingsMatch(ServerSettings left, ServerSettings right)
    {
        return left.Port == right.Port
            && left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase);
    }
}
