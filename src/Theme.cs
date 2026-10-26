using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Dark "console" look shared by the launcher windows (Selene blue/violet palette).
// High-contrast mode keeps the system colors and standard controls.
static class Theme
{
    public static readonly bool HighContrast = SystemInformation.HighContrast;
    public static readonly Color Background = Pick(Color.FromArgb(13, 15, 20), SystemColors.Window);
    public static readonly Color Rail = Pick(Color.FromArgb(17, 20, 27), SystemColors.Window);
    public static readonly Color Card = Pick(Color.FromArgb(21, 24, 32), SystemColors.Window);
    public static readonly Color Surface = Pick(Color.FromArgb(27, 31, 42), SystemColors.Window);
    public static readonly Color SurfaceHover = Pick(Color.FromArgb(37, 42, 57), SystemColors.Window);
    public static readonly Color Border = Pick(Color.FromArgb(38, 43, 56), SystemColors.WindowText);
    public static readonly Color Text = Pick(Color.FromArgb(205, 214, 244), SystemColors.WindowText);
    public static readonly Color Muted = Pick(Color.FromArgb(140, 147, 173), SystemColors.WindowText);
    public static readonly Color Faint = Pick(Color.FromArgb(88, 95, 120), SystemColors.GrayText);
    public static readonly Color Accent = Pick(Color.FromArgb(137, 180, 250), SystemColors.Highlight);
    public static readonly Color Accent2 = Pick(Color.FromArgb(125, 130, 217), SystemColors.Highlight);
    public static readonly Color OnAccent = Pick(Color.FromArgb(13, 15, 20), SystemColors.HighlightText);
    public static readonly Color Good = Pick(Color.FromArgb(166, 227, 161), SystemColors.WindowText);
    public static readonly Color Bad = Pick(Color.FromArgb(243, 139, 168), SystemColors.WindowText);
    public static readonly Color Warn = Pick(Color.FromArgb(249, 226, 175), SystemColors.WindowText);

    static Color Pick(Color dark, Color system) { return HighContrast ? system : dark; }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string id);

    // Dark title bar (Windows 10 2004+/11) and caption color matching the window (Windows 11).
    // The window stays cloaked until every control has painted once, so it never shows a grey or
    // white frame while the dark theme is still drawing.
    public static Icon AppIcon;

    public static void DarkChrome(Form form)
    {
        if (AppIcon != null) form.Icon = AppIcon;
        if (HighContrast) return;
        form.HandleCreated += delegate {
            try {
                int on = 1; DwmSetWindowAttribute(form.Handle, 20, ref on, 4);
                int caption = Background.R | Background.G << 8 | Background.B << 16; DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);
                int cloak = 1; DwmSetWindowAttribute(form.Handle, 13, ref cloak, 4);
            } catch (Exception) { }
        };
        form.Shown += delegate {
            PaintNow(form);
            form.BeginInvoke(new Action(delegate {
                PaintNow(form);
                try { int cloak = 0; DwmSetWindowAttribute(form.Handle, 13, ref cloak, 4); } catch (Exception) { }
            }));
        };
    }

    static void PaintNow(Control control)
    {
        control.Update();
        foreach (Control child in control.Controls) PaintNow(child);
    }

    // Dark scrollbars for scrollable panels, text boxes and lists.
    public static void DarkScrollbars(Control control)
    {
        if (HighContrast) return;
        EventHandler apply = delegate { try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); } catch (Exception) { } };
        if (control.IsHandleCreated) apply(control, EventArgs.Empty); else control.HandleCreated += apply;
    }

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    // Inner left/right padding for borderless text boxes.
    public static void TextMargins(TextBox box, int pixels)
    {
        EventHandler apply = delegate { int px = pixels * box.DeviceDpi / 96; SendMessage(box.Handle, 0xD3, (IntPtr)3, (IntPtr)(px | px << 16)); };
        if (box.IsHandleCreated) apply(box, EventArgs.Empty); else box.HandleCreated += apply;
    }

    // Native dark combobox (Windows 10 1903+), including its drop-down list.
    public static void DarkCombo(ComboBox combo)
    {
        if (HighContrast) return;
        EventHandler apply = delegate { try { SetWindowTheme(combo.Handle, "DarkMode_CFD", null); } catch (Exception) { } };
        if (combo.IsHandleCreated) apply(combo, EventArgs.Empty); else combo.HandleCreated += apply;
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }

    public static ToolStripRenderer MenuRenderer() { return HighContrast ? (ToolStripRenderer)new ToolStripSystemRenderer() : new DarkMenuRenderer(); }

    sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Surface; } }
        public override Color ImageMarginGradientBegin { get { return Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Surface; } }
        public override Color ImageMarginGradientEnd { get { return Surface; } }
        public override Color MenuBorder { get { return Border; } }
        public override Color MenuItemBorder { get { return SurfaceHover; } }
        public override Color MenuItemSelected { get { return SurfaceHover; } }
        public override Color ButtonSelectedHighlight { get { return SurfaceHover; } }
        public override Color ButtonSelectedGradientBegin { get { return SurfaceHover; } }
        public override Color ButtonSelectedGradientEnd { get { return SurfaceHover; } }
        public override Color ButtonSelectedBorder { get { return Border; } }
        public override Color ButtonPressedGradientBegin { get { return Border; } }
        public override Color ButtonPressedGradientEnd { get { return Border; } }
        public override Color SeparatorDark { get { return Border; } }
        public override Color SeparatorLight { get { return Border; } }
        public override Color StatusStripGradientBegin { get { return Rail; } }
        public override Color StatusStripGradientEnd { get { return Rail; } }
    }

    sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Text : Faint; base.OnRenderItemText(e); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is StatusStrip) using (var pen = new Pen(Border)) e.Graphics.DrawLine(pen, 0, 0, e.ToolStrip.Width, 0);
            else using (var pen = new Pen(Border)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
    }
}

enum ButtonKind { Primary, Secondary, Danger, Ghost }

// Rounded, owner-drawn button. Falls back to the standard button in high-contrast mode.
sealed class AnnexButton : Button
{
    ButtonKind kind;
    bool hover, down;
    public ButtonKind Kind { get { return kind; } set { if (kind == value) return; kind = value; Invalidate(); } }

    public AnnexButton()
    {
        if (Theme.HighContrast) { FlatStyle = FlatStyle.Standard; return; }
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Color.Transparent; Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Theme.HighContrast) { base.OnPaint(e); return; }
        var g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color text, border;
        using (var path = Theme.Rounded(rect, 8 * scale)) {
            if (kind == ButtonKind.Ghost) {
                if (Enabled && (hover || down)) using (var fill = new SolidBrush(down ? Theme.Border : Theme.SurfaceHover)) g.FillPath(fill, path);
                border = Enabled && Focused && ShowFocusCues ? Theme.Accent : Color.Transparent;
                text = !Enabled ? Theme.Faint : hover ? Theme.Text : Theme.Muted;
            } else if (!Enabled) {
                using (var fill = new SolidBrush(Color.FromArgb(24, 27, 36))) g.FillPath(fill, path);
                border = Color.FromArgb(34, 38, 50); text = Theme.Faint;
            } else if (kind == ButtonKind.Primary) {
                Color a = hover ? Light(Theme.Accent, 18) : Theme.Accent, b = hover ? Light(Theme.Accent2, 18) : Theme.Accent2;
                if (down) { a = Theme.Accent2; b = Theme.Accent2; }
                using (var fill = new LinearGradientBrush(rect, a, b, LinearGradientMode.ForwardDiagonal)) g.FillPath(fill, path);
                border = Focused && ShowFocusCues ? Theme.Text : a; text = Theme.OnAccent;
            } else {
                using (var fill = new SolidBrush(down ? Theme.Border : hover ? Theme.SurfaceHover : Theme.Surface)) g.FillPath(fill, path);
                border = Focused && ShowFocusCues ? Theme.Accent : kind == ButtonKind.Danger ? Color.FromArgb(84, 46, 60) : Color.FromArgb(46, 52, 72);
                text = kind == ButtonKind.Danger ? Theme.Bad : Theme.Text;
            }
            if (border != Color.Transparent) using (var pen = new Pen(border)) g.DrawPath(pen, path);
        }
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        using (var font = kind == ButtonKind.Primary && Enabled ? new Font(Font, FontStyle.Bold) : null)
            TextRenderer.DrawText(g, Text, font ?? Font, Rectangle.Round(rect), text, flags);
    }

    static Color Light(Color c, int by) { return Color.FromArgb(Math.Min(255, c.R + by), Math.Min(255, c.G + by), Math.Min(255, c.B + by)); }
}

// The Annex mark (picture-in-picture), drawn as vectors so it stays sharp at any DPI.
sealed class LogoMark : Control
{
    public LogoMark()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent; TabStop = false; AccessibleRole = AccessibleRole.Graphic; AccessibleName = "Annex logo";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Draw(e.Graphics, ClientRectangle);
    }

    public static void Draw(Graphics g, Rectangle bounds)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float size = Math.Min(bounds.Width, bounds.Height), u = size / 64f;
        float ox = bounds.X + (bounds.Width - size) / 2, oy = bounds.Y + (bounds.Height - size) / 2;
        Func<float, float, float, float, RectangleF> R = (x, y, w, h) => new RectangleF(ox + x * u, oy + y * u, w * u, h * u);
        bool small = size <= 32; // same mark, slightly thicker strokes so the lines stay visible
        Color frame = Theme.HighContrast ? SystemColors.WindowText : Theme.Accent;
        using (var pen = new Pen(frame, (small ? 5 : 4) * u))
        using (var path = Theme.Rounded(R(4, 10, 56, 44), 8 * u)) g.DrawPath(pen, path);
        using (var line = new SolidBrush(Color.FromArgb(small ? 190 : 140, frame))) {
            using (var p = Theme.Rounded(small ? R(10, 16, 15, 4) : R(10, 16, 14, 3), (small ? 2 : 1.5f) * u)) g.FillPath(line, p);
            using (var p = Theme.Rounded(small ? R(10, 23, 10, 4) : R(10, 22, 9, 3), (small ? 2 : 1.5f) * u)) g.FillPath(line, p);
        }
        RectangleF inner = R(30, 30, 24, 18);
        using (var path = Theme.Rounded(inner, 4 * u))
        using (Brush fill = Theme.HighContrast ? (Brush)new SolidBrush(SystemColors.Highlight) : new LinearGradientBrush(inner, Theme.Accent, Theme.Accent2, LinearGradientMode.ForwardDiagonal))
            g.FillPath(fill, path);
    }
}

// Status title with a colored dot in front.
sealed class StatusLabel : Label
{
    Color dot = Theme.Faint;
    public Color DotColor { get { return dot; } set { if (dot == value) return; dot = value; Invalidate(); } }
    public StatusLabel() { UseMnemonic = false; AutoSize = true; }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Padding = new Padding(Font.Height * 3 / 4, 0, 0, 0); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float d = Font.Height * 0.36f, y = (Font.Height - d) / 2f + 1;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var halo = new SolidBrush(Color.FromArgb(50, dot))) e.Graphics.FillEllipse(halo, 1 - d * 0.35f, y - d * 0.35f, d * 1.7f, d * 1.7f);
        using (var brush = new SolidBrush(dot)) e.Graphics.FillEllipse(brush, 1 + d * 0.0f, y, d, d);
    }
}

// Read-only text field on a rounded dark surface (TextBox borders can't be recolored).
sealed class FieldBox : Panel
{
    public readonly TextBox Box;
    public FieldBox(TextBox box)
    {
        Box = box; Padding = new Padding(10, 6, 10, 6); Margin = box.Margin;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Background;
        box.BorderStyle = Theme.HighContrast ? BorderStyle.FixedSingle : BorderStyle.None; box.Dock = DockStyle.None; box.Margin = Padding.Empty;
        box.BackColor = Theme.Surface; box.ForeColor = Theme.Text;
        Controls.Add(box);
        box.FontChanged += delegate { Height = box.PreferredHeight + Padding.Vertical; PerformLayout(); };
        Height = box.PreferredHeight + Padding.Vertical;
    }
    // Centre the text box vertically; password dots sit on the baseline, so lift them a pixel.
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int lift = Box.UseSystemPasswordChar || Box.PasswordChar != '\0' ? DeviceDpi / 96 : 0;
        Box.SetBounds(Padding.Left, Math.Max(0, (Height - Box.Height) / 2 - lift), Math.Max(10, Width - Padding.Horizontal), Box.Height);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Theme.HighContrast) { base.OnPaint(e); return; }
        e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8 * DeviceDpi / 96f)) {
            using (var fill = new SolidBrush(Theme.Surface)) e.Graphics.FillPath(fill, path);
            using (var pen = new Pen(Box.Focused ? Theme.Accent : Theme.Border)) e.Graphics.DrawPath(pen, path);
        }
    }
}
