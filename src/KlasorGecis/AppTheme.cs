using System.Drawing.Drawing2D;

namespace KlasorGecis;

internal static class AppTheme
{
    internal static Color Surface => SystemInformation.HighContrast ? SystemColors.Window : Color.White;
    internal static Color Canvas => SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(247, 248, 250);
    internal static Color Text => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(32, 36, 45);
    internal static Color Muted => SystemInformation.HighContrast ? SystemColors.GrayText : Color.FromArgb(98, 105, 119);
    internal static Color Border => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(223, 227, 233);
    internal static Color ControlBorder => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(135, 144, 155);
    internal static Color Primary => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(36, 91, 214);
    internal static Color Tint => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(237, 243, 255);
    internal static Color SelectedText => SystemInformation.HighContrast ? SystemColors.HighlightText : Primary;
    internal static Color Folder => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(169, 104, 6);
    internal static Color FolderTint => SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(255, 242, 216);
    internal static Color Danger => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(183, 53, 53);
    private static readonly Dictionary<(string, float, FontStyle), Font> fonts = [];
    internal static Font FontFor(float size, FontStyle style = FontStyle.Regular)
    {
        lock (fonts)
        {
            var key = (Localizer.FontFamily, size, style);
            if (!fonts.TryGetValue(key, out var font)) fonts[key] = font = new Font(key.Item1, size, style);
            return font;
        }
    }
    internal static Font Body => FontFor(9f);
    internal static Font Strong => FontFor(9f, FontStyle.Bold);
    internal static Font Small => FontFor(8.25f);
    internal static Font Heading => FontFor(17f);

    internal static void Style(Button button, bool primary = false)
    {
        button.Font = Body; button.FlatStyle = FlatStyle.Flat;
        button.BackColor = primary ? Primary : Surface;
        button.ForeColor = primary ? SystemColors.HighlightText : Text;
        button.FlatAppearance.BorderColor = primary ? Primary : ControlBorder;
        button.FlatAppearance.MouseOverBackColor = primary ? SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(25, 74, 182) : Canvas;
        button.FlatAppearance.MouseDownBackColor = primary ? Primary : Tint;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
        button.AutoEllipsis = true;
    }

    internal static void Style(ContextMenuStrip menu)
    {
        menu.Font = Body; menu.BackColor = Surface; menu.ForeColor = Text;
        menu.Renderer = new LightMenuRenderer();
    }

    internal static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        float size = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, size, size, 180, 90);
        path.AddArc(bounds.Right - size, bounds.Top, size, size, 270, 90);
        path.AddArc(bounds.Right - size, bounds.Bottom - size, size, size, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - size, size, size, 90, 90);
        path.CloseFigure(); return path;
    }

    private sealed class LightMenuRenderer : ToolStripProfessionalRenderer
    {
        internal LightMenuRenderer() : base(new LightColors()) { RoundedEdges = true; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? (e.Item.Selected || e.Item is ToolStripButton { Checked: true } ? SelectedText : Text) : Muted;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (SystemInformation.HighContrast) { base.OnRenderMenuItemBackground(e); return; }
            e.Graphics.Clear(Surface);
            if (e.Item.Selected && e.Item.Enabled)
            {
                using var path = Rounded(new Rectangle(3, 1, Math.Max(1, e.Item.Width - 6), Math.Max(1, e.Item.Height - 2)), 5);
                using var brush = new SolidBrush(Tint); e.Graphics.FillPath(brush, path);
            }
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            // A bounded popup can be narrower than DropDownMenu's measured text
            // columns. Keep its submenu arrow inside the actual row as well.
            if (e.Item is { } item)
            {
                var arrow = e.ArrowRectangle;
                int inset = Math.Max(3, (int)Math.Round(6 * (item.Owner?.DeviceDpi ?? 96) / 96d));
                if (arrow.Right > item.Width - inset)
                {
                    arrow.X = Math.Max(0, item.Width - inset - arrow.Width);
                    e.ArrowRectangle = arrow;
                }
                if (arrow.Left < inset) { arrow.X = inset; e.ArrowRectangle = arrow; }
                e.ArrowColor = item.Enabled ? item.Selected ? SelectedText : Text : Muted;
            }
            base.OnRenderArrow(e);
        }
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (SystemInformation.HighContrast) { base.OnRenderButtonBackground(e); return; }
            e.Graphics.Clear(Surface);
            if (e.Item is ToolStripButton button)
            {
                if (button.Checked || button.Selected)
                {
                    using var brush = new SolidBrush(Tint); e.Graphics.FillRectangle(brush, new Rectangle(0, 0, e.Item.Width, e.Item.Height));
                }
                if (button.Checked)
                {
                    using var pen = new Pen(Primary, Math.Max(2, (e.ToolStrip?.DeviceDpi ?? 96) / 48f));
                    e.Graphics.DrawLine(pen, 4, e.Item.Height - 2, e.Item.Width - 4, e.Item.Height - 2);
                }
            }
        }
    }
    private sealed class LightColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Tint;
        public override Color MenuItemSelected => Tint;
        public override Color MenuItemSelectedGradientBegin => Tint;
        public override Color MenuItemSelectedGradientEnd => Tint;
        public override Color ButtonSelectedGradientBegin => Tint;
        public override Color ButtonSelectedGradientMiddle => Tint;
        public override Color ButtonSelectedGradientEnd => Tint;
        public override Color ButtonCheckedGradientBegin => Tint;
        public override Color ButtonCheckedGradientMiddle => Tint;
        public override Color ButtonCheckedGradientEnd => Tint;
        public override Color CheckBackground => Tint;
        public override Color CheckSelectedBackground => Tint;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
    }
}

internal enum UiIcon { Folder, FolderGo, Star, Clock, Settings, Keyboard, Shield, Check, Chevron, Trash, Power, Info }

internal static class UiIcons
{
    private static readonly Dictionary<(UiIcon, int, int), Bitmap> images = [];
    internal static Bitmap Image(UiIcon icon, Color? color = null, int size = 20)
    {
        var tint = color ?? AppTheme.Muted;
        lock (images)
        {
            var key = (icon, tint.ToArgb(), size);
            if (images.TryGetValue(key, out var existing)) return existing;
            var image = new Bitmap(size, size);
            using var graphics = Graphics.FromImage(image);
            Draw(graphics, new Rectangle(0, 0, size, size), icon, tint);
            images.Add(key, image); return image;
        }
    }

    internal static void Draw(Graphics graphics, Rectangle bounds, UiIcon icon, Color color)
    {
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f);
            using var pen = new Pen(color, 1.65f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            void Lines(params PointF[] points) => graphics.DrawLines(pen, points);
            switch (icon)
            {
                case UiIcon.Folder:
                case UiIcon.FolderGo:
                    Lines(new(21, icon == UiIcon.FolderGo ? 11 : 20), new(21, 8), new(12, 8), new(10, 5), new(3, 5), new(3, 20), new(icon == UiIcon.FolderGo ? 10 : 21, 20));
                    graphics.DrawLine(pen, 3, 11, 21, 11);
                    if (icon == UiIcon.FolderGo) { graphics.DrawLine(pen, 14, 17, 22, 17); Lines(new(19, 14), new(22, 17), new(19, 20)); }
                    break;
                case UiIcon.Star:
                    Lines(new(12, 3), new(15, 9), new(21, 10), new(17, 14), new(18, 21), new(12, 18), new(6, 21), new(7, 14), new(3, 10), new(9, 9), new(12, 3)); break;
                case UiIcon.Clock:
                    graphics.DrawEllipse(pen, 3, 3, 18, 18); Lines(new(12, 7), new(12, 12), new(16, 14)); break;
                case UiIcon.Settings:
                    graphics.DrawEllipse(pen, 8, 8, 8, 8);
                    for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; graphics.DrawLine(pen, 12 + 7 * (float)Math.Cos(a), 12 + 7 * (float)Math.Sin(a), 12 + 10 * (float)Math.Cos(a), 12 + 10 * (float)Math.Sin(a)); }
                    graphics.DrawEllipse(pen, 5, 5, 14, 14); break;
                case UiIcon.Keyboard:
                    graphics.DrawRectangle(pen, 2, 5, 20, 14);
                    for (int y = 9; y <= 12; y += 3) for (int x = 6; x <= 18; x += 4) graphics.DrawLine(pen, x, y, x + .25f, y);
                    graphics.DrawLine(pen, 7, 16, 17, 16); break;
                case UiIcon.Shield:
                    Lines(new(12, 3), new(4, 6), new(4, 12), new(7, 18), new(12, 21), new(17, 18), new(20, 12), new(20, 6), new(12, 3));
                    Lines(new(8, 12), new(11, 15), new(16, 9)); break;
                case UiIcon.Check: Lines(new(5, 12), new(10, 17), new(20, 6)); break;
                case UiIcon.Chevron: Lines(new(8, 10), new(12, 14), new(16, 10)); break;
                case UiIcon.Trash:
                    graphics.DrawLine(pen, 3, 6, 21, 6); Lines(new(5, 6), new(6, 21), new(18, 21), new(19, 6)); Lines(new(9, 6), new(9, 3), new(15, 3), new(15, 6));
                    graphics.DrawLine(pen, 10, 10, 10, 17); graphics.DrawLine(pen, 14, 10, 14, 17); break;
                case UiIcon.Power:
                    graphics.DrawArc(pen, 3, 3, 18, 18, -45, 270); graphics.DrawLine(pen, 12, 2, 12, 12); break;
                case UiIcon.Info:
                    graphics.DrawEllipse(pen, 3, 3, 18, 18); graphics.DrawLine(pen, 12, 11, 12, 17); graphics.DrawLine(pen, 12, 7, 12.1f, 7); break;
            }
        }
        finally { graphics.Restore(state); }
    }
}
