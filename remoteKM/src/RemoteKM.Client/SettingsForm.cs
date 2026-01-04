using System.Collections.Generic;
using System.Drawing;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace RemoteKM.Client;

internal sealed class SettingsForm : Form
{
    private const int HostColumnIndex = 0;
    private const int PortColumnIndex = 1;
    private const int EdgeColumnIndex = 2;
    private const int HotKeyColumnIndex = 3;
    private readonly DataGridView _serverGrid = new();
    private TextBox? _hotKeyEditingControl;
    private readonly Button _saveButton = new() { Text = "Save", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true };
    private readonly string _settingsPath;
    private readonly Action<ClientSettings> _apply;
    private bool _edgeSwapInProgress;

    internal SettingsForm(string settingsPath, ClientSettings settings, Action<ClientSettings> apply)
    {
        _settingsPath = settingsPath;
        _apply = apply;

        Text = "RemoteKM Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(520, 420);

        ConfigureServerGrid();
        PopulateServerGrid(settings.Servers);

        var toolbar = BuildServerToolbar();
        var buttonPanel = BuildActionButtons();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_serverGrid, 0, 1);
        layout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(layout);
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
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

    private void ConfigureServerGrid()
    {
        _serverGrid.AllowUserToAddRows = false;
        _serverGrid.AllowUserToDeleteRows = false;
        _serverGrid.AllowUserToResizeRows = false;
        _serverGrid.RowHeadersVisible = false;
        _serverGrid.MultiSelect = false;
        _serverGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _serverGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        _serverGrid.Dock = DockStyle.Fill;
        _serverGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _serverGrid.DataError += (_, _) => { };

        var hostColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Host",
            Name = "Host"
        };
        var portColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Port",
            Name = "Port",
            Width = 80,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        };
        var edgeColumn = new DataGridViewComboBoxColumn
        {
            HeaderText = "Edge",
            Name = "Edge",
            Width = 120,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            FlatStyle = FlatStyle.Flat,
            ValueType = typeof(CaptureEdge),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        };
        edgeColumn.Items.AddRange(new object[]
        {
            CaptureEdge.None,
            CaptureEdge.Left,
            CaptureEdge.Right,
            CaptureEdge.Top,
            CaptureEdge.Bottom
        });

        var hotKeyColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "HotKey",
            Name = "HotKey",
            Width = 140,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        };

        _serverGrid.Columns.AddRange(hostColumn, portColumn, edgeColumn, hotKeyColumn);
        _serverGrid.CellBeginEdit += ServerGridOnCellBeginEdit;
        _serverGrid.CellValueChanged += ServerGridOnCellValueChanged;
        _serverGrid.CurrentCellDirtyStateChanged += ServerGridOnDirtyStateChanged;
        _serverGrid.EditingControlShowing += ServerGridOnEditingControlShowing;
    }

    private void PopulateServerGrid(IReadOnlyList<ServerEndpoint> servers)
    {
        foreach (var server in servers)
        {
            _serverGrid.Rows.Add(server.Host, server.Port.ToString(), server.CaptureEdge, server.HotKey);
        }
    }

    private Control BuildServerToolbar()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight
        };

        var title = new Label
        {
            Text = "Servers",
            AutoSize = true,
            Padding = new Padding(0, 6, 12, 0)
        };
        var addButton = new Button { Text = "Add", AutoSize = true };
        var removeButton = new Button { Text = "Remove", AutoSize = true };

        addButton.Click += (_, _) => AddServerRow();
        removeButton.Click += (_, _) => RemoveSelectedRow();

        panel.Controls.Add(title);
        panel.Controls.Add(addButton);
        panel.Controls.Add(removeButton);
        return panel;
    }

    private FlowLayoutPanel BuildActionButtons()
    {
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };
        _saveButton.Click += (_, _) => SaveAndClose();
        _cancelButton.Click += (_, _) => Close();
        buttonPanel.Controls.Add(_saveButton);
        buttonPanel.Controls.Add(_cancelButton);
        return buttonPanel;
    }

    private void AddServerRow()
    {
        var defaultServer = ClientSettings.Defaults.Servers[0];
        var index = _serverGrid.Rows.Add(defaultServer.Host, defaultServer.Port.ToString(), CaptureEdge.None, defaultServer.HotKey);
        _serverGrid.ClearSelection();
        _serverGrid.Rows[index].Selected = true;
    }

    private void RemoveSelectedRow()
    {
        if (_serverGrid.SelectedRows.Count == 0)
        {
            return;
        }

        var row = _serverGrid.SelectedRows[0];
        _serverGrid.Rows.Remove(row);
    }

    private void ServerGridOnDirtyStateChanged(object? sender, EventArgs e)
    {
        if (_serverGrid.IsCurrentCellDirty)
        {
            _serverGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private void ServerGridOnEditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (_hotKeyEditingControl != null)
        {
            _hotKeyEditingControl.KeyDown -= HotKeyEditingControlOnKeyDown;
            _hotKeyEditingControl = null;
        }

        if (e.Control is TextBox textBox)
        {
            var columnIndex = _serverGrid.CurrentCell?.ColumnIndex ?? -1;
            textBox.ShortcutsEnabled = columnIndex != HotKeyColumnIndex;
            textBox.ImeMode = columnIndex is HostColumnIndex or PortColumnIndex
                ? ImeMode.Disable
                : ImeMode.NoControl;

            if (columnIndex == HotKeyColumnIndex)
            {
                _hotKeyEditingControl = textBox;
                _hotKeyEditingControl.ShortcutsEnabled = false;
                _hotKeyEditingControl.KeyDown += HotKeyEditingControlOnKeyDown;
            }
        }
    }

    private void HotKeyEditingControlOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        e.SuppressKeyPress = true;

        if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
        {
            textBox.Clear();
            return;
        }

        var key = e.KeyCode;
        if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
        {
            return;
        }

        textBox.Text = FormatHotKey(key, e.Modifiers);
    }

    private void ServerGridOnCellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (e.ColumnIndex != EdgeColumnIndex || e.RowIndex < 0)
        {
            return;
        }

        var row = _serverGrid.Rows[e.RowIndex];
        row.Cells[EdgeColumnIndex].Tag = GetEdgeValue(row);
    }

    private void ServerGridOnCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_edgeSwapInProgress || e.RowIndex < 0 || e.ColumnIndex != EdgeColumnIndex)
        {
            return;
        }

        var row = _serverGrid.Rows[e.RowIndex];
        var newEdge = GetEdgeValue(row);
        var previousEdge = row.Cells[EdgeColumnIndex].Tag is CaptureEdge saved ? saved : CaptureEdge.None;

        if (newEdge == CaptureEdge.None || newEdge == previousEdge)
        {
            return;
        }

        var conflictRow = FindRowByEdge(newEdge, row);
        if (conflictRow == null)
        {
            return;
        }

        _edgeSwapInProgress = true;
        SetEdgeValue(conflictRow, previousEdge);
        _edgeSwapInProgress = false;
    }

    private static CaptureEdge GetEdgeValue(DataGridViewRow row)
    {
        var value = row.Cells[EdgeColumnIndex].Value;
        if (value is CaptureEdge edge)
        {
            return edge;
        }

        if (value is string text && Enum.TryParse(text, true, out CaptureEdge parsed))
        {
            return parsed;
        }

        return CaptureEdge.None;
    }

    private static void SetEdgeValue(DataGridViewRow row, CaptureEdge edge)
    {
        row.Cells[EdgeColumnIndex].Value = edge;
    }

    private DataGridViewRow? FindRowByEdge(CaptureEdge edge, DataGridViewRow excludeRow)
    {
        foreach (DataGridViewRow row in _serverGrid.Rows)
        {
            if (ReferenceEquals(row, excludeRow))
            {
                continue;
            }

            if (GetEdgeValue(row) == edge)
            {
                return row;
            }
        }

        return null;
    }

    private void SaveAndClose()
    {
        var servers = new List<ServerEndpoint>();
        foreach (DataGridViewRow row in _serverGrid.Rows)
        {
            var host = (row.Cells[HostColumnIndex].Value?.ToString() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show("Host cannot be empty.", "RemoteKM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                row.Selected = true;
                return;
            }

            var portText = row.Cells[PortColumnIndex].Value?.ToString() ?? string.Empty;
            if (!int.TryParse(portText, out var port))
            {
                MessageBox.Show("Port must be a number.", "RemoteKM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                row.Selected = true;
                return;
            }

            var hotKey = (row.Cells[HotKeyColumnIndex].Value?.ToString() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(hotKey))
            {
                MessageBox.Show("HotKey cannot be empty.", "RemoteKM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                row.Selected = true;
                return;
            }

            servers.Add(new ServerEndpoint(host, port, GetEdgeValue(row), hotKey));
        }

        if (!ValidateUniqueEdges(servers))
        {
            MessageBox.Show("Capture edges must be unique.", "RemoteKM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var settings = new ClientSettings(servers);
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

    private static bool ValidateUniqueEdges(IReadOnlyList<ServerEndpoint> servers)
    {
        var seen = new HashSet<CaptureEdge>();
        foreach (var server in servers)
        {
            if (server.CaptureEdge == CaptureEdge.None)
            {
                continue;
            }

            if (!seen.Add(server.CaptureEdge))
            {
                return false;
            }
        }

        return true;
    }
}
