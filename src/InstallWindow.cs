using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// Progress window for scripts\install-sunshine.ps1 -Report (STEP / OK / PROGRESS / DONE / ERROR lines).
sealed class InstallWindow : Form
{
    readonly Label step, detail, status;
    readonly Label done;
    readonly ProgressLine bar;
    readonly AnnexButton close;
    public bool Finished { get; private set; }

    public InstallWindow(string title)
    {
        Text = "Annex"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Background; ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(480, 300);
        Theme.DarkChrome(this);
        Controls.Add(new Label { Text = title, Left = 24, Top = 20, AutoSize = true, Font = new Font("Segoe UI Semibold", 14), ForeColor = Theme.Text });
        step = new Label { Left = 24, Top = 58, Width = 432, Height = 22, ForeColor = Theme.Text, UseMnemonic = false, Text = "Starting..." };
        bar = new ProgressLine { Left = 24, Top = 86, Width = 432, Height = 8 };
        detail = new Label { Left = 24, Top = 100, Width = 432, Height = 20, ForeColor = Theme.Faint, UseMnemonic = false };
        done = new Label { Left = 24, Top = 128, Width = 432, Height = 100, ForeColor = Theme.Muted, UseMnemonic = false };
        status = new Label { Left = 24, Top = 230, Width = 300, Height = 44, ForeColor = Theme.Muted, UseMnemonic = false };
        close = new AnnexButton { Text = "Close", Kind = ButtonKind.Secondary, Left = 356, Top = 244, Size = new Size(100, 36), Enabled = false, DialogResult = DialogResult.OK };
        Controls.AddRange(new Control[] { step, bar, detail, done, status, close });
        AcceptButton = close;
        FormClosing += (s, e) => { if (!Finished && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
    }

    public void Report(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        int space = line.IndexOf(' ');
        string kind = space < 0 ? line : line.Substring(0, space), text = space < 0 ? "" : line.Substring(space + 1);
        switch (kind) {
            case "STEP": step.Text = text; detail.Text = ""; break;
            case "OK": done.Text += "✓  " + text + Environment.NewLine; break;
            case "PROGRESS": {
                int split = text.IndexOf(' '); int pct;
                if (split > 0 && int.TryParse(text.Substring(0, split), out pct)) { bar.Value = pct; detail.Text = pct + "%   " + text.Substring(split + 1); }
                break;
            }
            case "DONE": Finish(true, text); break;
            case "ERROR": Finish(false, text); break;
        }
    }

    public void Finish(bool success, string message)
    {
        if (Finished) return;
        Finished = true;
        step.Text = success ? "Sunshine is ready" : "Sunshine was not installed";
        step.ForeColor = success ? Theme.Good : Theme.Bad;
        if (success) bar.Value = 100;
        status.Text = message; status.ForeColor = success ? Theme.Muted : Theme.Bad;
        close.Enabled = true; close.Kind = success ? ButtonKind.Primary : ButtonKind.Secondary; close.Focus();
    }

    // Thin rounded progress bar in the accent gradient.
    sealed class ProgressLine : Control
    {
        int value;
        public int Value { get { return value; } set { this.value = Math.Max(0, Math.Min(100, value)); Invalidate(); } }
        public ProgressLine() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var path = Theme.Rounded(track, Height / 2f)) using (var fill = new SolidBrush(Theme.Surface)) e.Graphics.FillPath(fill, path);
            if (value <= 0) return;
            var bar = new RectangleF(0, 0, Math.Max(Height, (Width - 1) * value / 100f), Height - 1);
            using (var path = Theme.Rounded(bar, Height / 2f))
            using (Brush fill = Theme.HighContrast ? (Brush)new SolidBrush(SystemColors.Highlight) : new LinearGradientBrush(new RectangleF(0, 0, Width, Height), Theme.Accent, Theme.Accent2, LinearGradientMode.Horizontal))
                e.Graphics.FillPath(fill, path);
        }
    }
}
