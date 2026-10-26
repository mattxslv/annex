using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// Rounded card container for the sidebar sections.
sealed class CardPanel : TableLayoutPanel
{
    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card; Padding = new Padding(16, 14, 16, 10); ColumnCount = 1;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Top;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    }
    public void Add(Control control) { int row = RowCount++; RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(control, 0, row); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Theme.HighContrast) { base.OnPaintBackground(e); return; }
        e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Rail);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 12 * DeviceDpi / 96f)) {
            using (var fill = new SolidBrush(Theme.Card)) e.Graphics.FillPath(fill, path);
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawPath(pen, path);
        }
    }
}

// Pairing steps as numbered cards: four in a row on wide windows, two columns otherwise.
sealed class PairingSteps : Control
{
    static readonly string[] Titles = { "Open host settings", "Create a login", "Add this PC", "Enter the PIN" };
    string[] steps = new string[0];
    Font bold;
    bool sizing;

    public PairingSteps()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AccessibleName = "Pairing instructions"; AccessibleRole = AccessibleRole.StaticText; TabStop = false;
    }

    public string[] Steps { get { return steps; } }

    public override string Text
    {
        get { return base.Text; }
        set {
            base.Text = value; AccessibleDescription = value;
            string[] parts = (value ?? "").Split(new[] { Environment.NewLine + Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) {
                string prefix = (i + 1) + ". ";
                if (parts[i].StartsWith(prefix, StringComparison.Ordinal)) parts[i] = parts[i].Substring(prefix.Length);
            }
            steps = parts; FitHeight(); Invalidate();
        }
    }

    bool Numbered { get { return steps.Length == Titles.Length; } }
    int Columns { get { return !Numbered ? 1 : Width >= 760 * DeviceDpi / 96 ? 4 : 2; } }
    float S { get { return DeviceDpi / 96f; } }
    int Gap { get { return (int)(12 * S); } }
    int Pad { get { return (int)(14 * S); } }
    int Circle { get { return (int)(26 * S); } }
    Font Bold { get { if (bold == null || bold.Size != Font.Size) { if (bold != null) bold.Dispose(); bold = new Font(Font, FontStyle.Bold); } return bold; } }

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitHeight(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); FitHeight(); }

    int CellWidth { get { int cols = Columns; return Math.Max(80, (Width - Gap * (cols - 1)) / cols); } }

    int RowHeight()
    {
        int body = 0;
        foreach (string step in steps)
            body = Math.Max(body, TextRenderer.MeasureText(step, Font, new Size(CellWidth - Pad * 2, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height);
        return Pad + (Numbered ? Circle + (int)(8 * S) : 0) + body + Pad;
    }

    void FitHeight()
    {
        if (sizing || Width <= 0) return;
        sizing = true;
        try {
            int rows = (steps.Length + Columns - 1) / Columns;
            Height = Math.Max(1, rows * RowHeight() + Math.Max(0, rows - 1) * Gap);
        } finally { sizing = false; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int rowHeight = RowHeight(), cols = Columns, width = CellWidth;
        for (int i = 0; i < steps.Length; i++) {
            var cell = new Rectangle((i % cols) * (width + Gap), (i / cols) * (rowHeight + Gap), width, rowHeight);
            using (var path = Theme.Rounded(new RectangleF(cell.X + 0.5f, cell.Y + 0.5f, cell.Width - 1.5f, cell.Height - 1.5f), 10 * S)) {
                using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
                using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
            }
            int textTop = cell.Y + Pad;
            if (Numbered) {
                var dot = new RectangleF(cell.X + Pad, cell.Y + Pad, Circle, Circle);
                using (Brush fill = Theme.HighContrast ? (Brush)new SolidBrush(SystemColors.Highlight) : new LinearGradientBrush(dot, Theme.Accent, Theme.Accent2, LinearGradientMode.ForwardDiagonal)) g.FillEllipse(fill, dot);
                TextRenderer.DrawText(g, (i + 1).ToString(), Bold, Rectangle.Round(dot), Theme.OnAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                int titleLeft = cell.X + Pad + Circle + (int)(10 * S);
                TextRenderer.DrawText(g, Titles[i], Bold, new Rectangle(titleLeft, cell.Y + Pad, cell.Right - Pad - titleLeft, Circle), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                textTop += Circle + (int)(8 * S);
            }
            TextRenderer.DrawText(g, steps[i], Font, new Rectangle(cell.X + Pad, textTop, cell.Width - Pad * 2, cell.Bottom - textTop), Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    protected override void Dispose(bool disposing) { if (disposing && bold != null) bold.Dispose(); base.Dispose(disposing); }
}

// Dark replacements for MessageBox confirmations.
static class AnnexDialog
{
    public static bool Ask(IWin32Window owner, string title, string message, string confirm, string cancel, bool danger)
    {
        using (var form = Build(title, message, confirm, cancel, danger ? ButtonKind.Danger : ButtonKind.Primary, danger ? Theme.Bad : Theme.Accent))
            return form.ShowDialog(owner) == DialogResult.OK;
    }

    public static void Tell(IWin32Window owner, string title, string message, bool error)
    {
        using (var form = Build(title, message, "OK", null, ButtonKind.Primary, error ? Theme.Bad : Theme.Accent)) form.ShowDialog(owner);
    }

    internal static Form Build(string title, string message, string confirm, string cancel, ButtonKind kind, Color tone)
    {
        var form = new Form {
            Text = "Annex", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            BackColor = Theme.Background, ForeColor = Theme.Text, Font = new Font("Segoe UI", 9.5f), AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96)
        };
        Theme.DarkChrome(form);
        var badge = new Badge { Tone = tone, Left = 22, Top = 22, Size = new Size(34, 34) };
        var heading = new Label { Text = title, Left = 70, Top = 24, AutoSize = true, MaximumSize = new Size(350, 0), Font = new Font("Segoe UI Semibold", 12.5f), ForeColor = Theme.Text, UseMnemonic = false };
        var body = new Label { Text = message, Left = 70, AutoSize = true, MaximumSize = new Size(350, 0), ForeColor = Theme.Muted, UseMnemonic = false };
        form.Controls.AddRange(new Control[] { badge, heading, body });
        body.Top = heading.Top + heading.PreferredSize.Height + 8;
        int buttonsTop = Math.Max(badge.Bottom, body.Top + body.PreferredSize.Height) + 22;
        var ok = new AnnexButton { Text = confirm, Kind = kind, AutoSize = true, MinimumSize = new Size(96, 36), Padding = new Padding(14, 2, 14, 2), DialogResult = DialogResult.OK, Top = buttonsTop };
        form.Controls.Add(ok);
        ok.Left = 420 - ok.PreferredSize.Width;
        if (cancel != null) {
            var no = new AnnexButton { Text = cancel, Kind = ButtonKind.Secondary, AutoSize = true, MinimumSize = new Size(96, 36), Padding = new Padding(14, 2, 14, 2), DialogResult = DialogResult.Cancel, Top = buttonsTop };
            form.Controls.Add(no); no.Left = ok.Left - 8 - no.PreferredSize.Width;
            // Safe default, like the MessageBox it replaces: Enter/Escape pick the cancel action.
            form.CancelButton = no; form.AcceptButton = no; form.Shown += delegate { no.Focus(); };
        } else { form.AcceptButton = ok; form.CancelButton = ok; }
        form.ClientSize = new Size(442, buttonsTop + 36 + 20);
        return form;
    }

    sealed class Badge : Control
    {
        public Color Tone;
        public Badge() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.75f, 0.75f, Width - 2f, Height - 2f);
            using (var fill = new SolidBrush(Color.FromArgb(40, Tone))) e.Graphics.FillEllipse(fill, r);
            using (var pen = new Pen(Color.FromArgb(150, Tone), 1.5f)) e.Graphics.DrawEllipse(pen, r);
            using (var font = new Font("Segoe UI", 13, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, "!", font, ClientRectangle, Tone, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
