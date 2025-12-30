using System.Net;
using System.Windows.Forms;

namespace RemoteKM.Server;

internal sealed class SettingsForm : Form
{
    private readonly Action<ServerSettings> _apply;
    private readonly TextBox _hostText;
    private readonly NumericUpDown _portUpDown;

    internal SettingsForm(ServerSettings settings, Action<ServerSettings> apply)
    {
        _apply = apply;

        Text = "RemoteKM Server Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Width = 360;
        Height = 190;

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

        var applyButton = new Button
        {
            Text = "Apply",
            Left = 100,
            Top = 100,
            Width = 80
        };
        applyButton.Click += (_, _) => Apply();

        var closeButton = new Button
        {
            Text = "Close",
            Left = 190,
            Top = 100,
            Width = 80
        };
        closeButton.Click += (_, _) => Close();

        Controls.Add(hostLabel);
        Controls.Add(_hostText);
        Controls.Add(portLabel);
        Controls.Add(_portUpDown);
        Controls.Add(applyButton);
        Controls.Add(closeButton);
    }

    private void Apply()
    {
        var host = _hostText.Text.Trim();
        if (!IPAddress.TryParse(host, out _))
        {
            MessageBox.Show(this, "Host must be a valid IP address.", "Invalid host", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _hostText.Focus();
            return;
        }

        var port = (int)_portUpDown.Value;
        _apply(new ServerSettings(host, port));
    }
}
