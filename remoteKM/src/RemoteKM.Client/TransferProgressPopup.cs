using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace RemoteKM.Client;

internal sealed class TransferProgressPopup : Form
{
    private readonly Label _titleLabel;
    private readonly Label _detailLabel;
    private readonly ProgressPill _progressPill;
    private readonly Timer _hideTimer;

    internal TransferProgressPopup(string title)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 32, 38);
        ForeColor = Color.White;
        Padding = new Padding(16);
        Size = new Size(280, 112);
        Font = new Font("Bahnschrift", 10f, FontStyle.Regular);
        DoubleBuffered = true;

        _titleLabel = new Label
        {
            AutoSize = true,
            ForeColor = ForeColor,
            BackColor = BackColor,
            Font = new Font("Bahnschrift", 11f, FontStyle.Bold)
        };

        _detailLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(200, 200, 210),
            BackColor = BackColor,
            Font = new Font("Bahnschrift", 9f, FontStyle.Regular)
        };

        _progressPill = new ProgressPill
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 0, 0)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = BackColor
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.Controls.Add(_titleLabel, 0, 0);
        layout.Controls.Add(_detailLabel, 0, 1);
        layout.Controls.Add(_progressPill, 0, 2);
        Controls.Add(layout);

        _hideTimer = new Timer { Interval = 1200 };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    internal void UpdateProgress(int current, int total, long currentFileBytes, long totalFileBytes, bool completed)
    {
        if (total <= 0)
        {
            if (completed)
            {
                Hide();
            }
            return;
        }

        current = Math.Clamp(current, 0, total);
        var displayIndex = current == 0 ? 1 : current;
        displayIndex = Math.Clamp(displayIndex, 1, total);
        var progress = totalFileBytes > 0 ? (float)currentFileBytes / totalFileBytes : completed ? 1f : 0f;
        progress = Math.Clamp(progress, 0f, 1f);
        var percent = (int)Math.Round(progress * 100, MidpointRounding.AwayFromZero);
        _titleLabel.Text = "Transferring file";
        _detailLabel.Text = $"File {displayIndex} of {total} ({percent}%)";
        _progressPill.UpdateState(progress);

        _hideTimer.Stop();
        ShowPopup();

        if (completed && current >= total)
        {
            _hideTimer.Start();
        }
    }

    private void ShowPopup()
    {
        PositionNearTray();
        if (!Visible)
        {
            Show();
        }
        else
        {
            Invalidate();
        }
    }

    private void PositionNearTray()
    {
        var area = Screen.PrimaryScreen.WorkingArea;
        var x = Math.Max(area.Left, area.Right - Width - 12);
        var y = Math.Max(area.Top, area.Bottom - Height - 12);
        Location = new Point(x, y);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = CreateRoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 16);
        using var fill = new SolidBrush(BackColor);
        using var border = new Pen(Color.FromArgb(72, 72, 84));
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        using var path = CreateRoundedRect(new Rectangle(0, 0, Width, Height), 16);
        Region = new Region(path);
    }

    private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class ProgressPill : Control
    {
        private float _progress;

        internal ProgressPill()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Height = 18;
        }

        internal void UpdateState(float progress)
        {
            _progress = Math.Clamp(progress, 0f, 1f);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using var trackPath = CreateRoundedRect(bounds, 8);
            using var trackBrush = new SolidBrush(Color.FromArgb(36, 36, 44));
            using var trackBorder = new Pen(Color.FromArgb(70, 70, 82));
            e.Graphics.FillPath(trackBrush, trackPath);
            e.Graphics.DrawPath(trackBorder, trackPath);

            if (_progress <= 0f)
            {
                return;
            }

            var fillWidth = Math.Max(8, (int)Math.Round(bounds.Width * _progress));
            var progressBounds = new Rectangle(0, 0, fillWidth, bounds.Height);
            using var progressPath = CreateRoundedRect(progressBounds, 8);
            using var progressBrush = new LinearGradientBrush(progressBounds, Color.FromArgb(0, 208, 160), Color.FromArgb(0, 160, 140), 0f);
            e.Graphics.FillPath(progressBrush, progressPath);
        }
    }
}
