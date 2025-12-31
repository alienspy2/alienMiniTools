using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Windows.Forms;

namespace RemoteKM.Server;

internal sealed class SettingsForm : Form
{
    private readonly Action<ServerSettings> _apply;
    private readonly TextBox _hostText;
    private readonly NumericUpDown _portUpDown;
    private readonly CheckBox _serviceToggle;
    private readonly Label _serviceStatusLabel;
    private bool _updatingServiceToggle;

    private const string CoreTaskName = "RemoteKMServer.Core";
    private const string TrayTaskName = "RemoteKMServer.Tray";

    internal SettingsForm(ServerSettings settings, Action<ServerSettings> apply)
    {
        _apply = apply;

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
        _hostText.Leave += (_, _) => Apply();
        _portUpDown.ValueChanged += (_, _) =>
        {
            if (IsValidHost(_hostText.Text.Trim()))
            {
                Apply();
            }
        };

        var serviceLabel = new Label
        {
            Text = "자동실행",
            Left = 12,
            Top = 96,
            Width = 80
        };
        _serviceToggle = new CheckBox
        {
            Left = 100,
            Top = 92,
            Width = 120,
            Height = 28,
            Appearance = Appearance.Button,
            TextAlign = ContentAlignment.MiddleCenter
        };
        _serviceStatusLabel = new Label
        {
            Left = 230,
            Top = 98,
            AutoSize = true
        };
        UpdateServiceToggle(IsAutoRunRegistered());
        _serviceToggle.CheckedChanged += (_, _) => ToggleServiceRegistration();

        var closeButton = new Button
        {
            Text = "Close",
            Left = 100,
            Top = 140,
            Width = 80
        };
        closeButton.Click += (_, _) => Close();

        Controls.Add(hostLabel);
        Controls.Add(_hostText);
        Controls.Add(portLabel);
        Controls.Add(_portUpDown);
        Controls.Add(serviceLabel);
        Controls.Add(_serviceToggle);
        Controls.Add(_serviceStatusLabel);
        Controls.Add(closeButton);
    }

    private void Apply()
    {
        var host = _hostText.Text.Trim();
        if (!IsValidHost(host))
        {
            MessageBox.Show(this, "Host must be a valid IP address.", "Invalid host", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _hostText.Focus();
            return;
        }

        var port = (int)_portUpDown.Value;
        _apply(new ServerSettings(host, port));
    }

    private static bool IsValidHost(string host)
    {
        return IPAddress.TryParse(host, out _);
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
            var success = targetRegister ? RegisterAutoRun() : UnregisterAutoRun();
            if (!success)
            {
                UpdateServiceToggle(!targetRegister);
                return;
            }

            MessageBox.Show(this,
                targetRegister ? "자동실행 등록 완료" : "자동실행 해제 완료",
                "RemoteKM Server",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        finally
        {
            _serviceToggle.Enabled = true;
        }

        UpdateServiceToggle(IsAutoRunRegistered());
    }

    private void UpdateServiceToggle(bool registered)
    {
        _updatingServiceToggle = true;
        _serviceToggle.Checked = registered;
        _serviceToggle.Text = registered ? "자동실행 해제" : "자동실행 등록";
        _serviceStatusLabel.Text = registered ? "등록됨" : "미등록";
        _updatingServiceToggle = false;
    }

    private static bool RegisterAutoRun()
    {
        var exePath = Application.ExecutablePath;
        var coreArgs = $"/Create /TN \"{CoreTaskName}\" /TR \"\\\"{exePath}\\\" --proxy\" /SC ONSTART /RU SYSTEM /RL HIGHEST /F";
        if (!RunSchtasks(coreArgs, out var output, out var error, out var exitCode))
        {
            return ShowServiceError("자동실행 등록 실패", output, error, exitCode);
        }

        var trayArgs = $"/Create /TN \"{TrayTaskName}\" /TR \"\\\"{exePath}\\\" --agent\" /SC ONLOGON /RL HIGHEST /F";
        if (!RunSchtasks(trayArgs, out output, out error, out exitCode))
        {
            RunSchtasks($"/Delete /TN \"{CoreTaskName}\" /F", out _, out _, out _);
            return ShowServiceError("자동실행 등록 실패", output, error, exitCode);
        }

        return true;
    }

    private static bool UnregisterAutoRun()
    {
        var success = true;
        if (IsTaskRegistered(CoreTaskName) &&
            !RunSchtasks($"/Delete /TN \"{CoreTaskName}\" /F", out var output, out var error, out var exitCode))
        {
            success = ShowServiceError("자동실행 해제 실패", output, error, exitCode);
        }

        if (IsTaskRegistered(TrayTaskName) &&
            !RunSchtasks($"/Delete /TN \"{TrayTaskName}\" /F", out output, out error, out exitCode))
        {
            success = ShowServiceError("자동실행 해제 실패", output, error, exitCode) && success;
        }

        return success;
    }

    private static bool IsTaskRegistered(string name)
    {
        return RunSchtasks($"/Query /TN \"{name}\"", out _, out _, out var exitCode) && exitCode == 0;
    }

    private static bool IsAutoRunRegistered()
    {
        return IsTaskRegistered(CoreTaskName) && IsTaskRegistered(TrayTaskName);
    }

    private static bool RunSchtasks(string arguments, out string output, out string error, out int exitCode)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("schtasks.exe", arguments)
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
            detail = $"schtasks.exe exit code: {exitCode}";
        }

        MessageBox.Show(detail.Trim(), title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }
}
