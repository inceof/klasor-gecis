using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KlasorGecis;

/// <summary>A nonactivating, owned suggestion row. Navigation belongs to the caller.</summary>
internal sealed class FolderBar : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;
    private const int GwlpHwndParent = -8;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpShowWindow = 0x0040;

    private readonly FolderButton _button;
    private readonly FolderButton _others;
    private readonly ToolTip _toolTip;
    private FolderLocation? _folder;
    private nint _dialog;
    private bool _shortcutAvailable;
    private string? _status;
    private bool _isError;
    private bool _informational;
    private bool _hasMenuItems;
    private bool IsBusy => _status is not null && !_isError && !_informational;
    private string _shortcutText = "Ctrl+G";

    public event EventHandler? NavigateRequested;
    public event EventHandler? OtherFoldersRequested;

    public FolderBar()
    {
        Name = "KlasorGecisFolderBar";
        Text = "Klasör Geçiş";
        AccessibleName = "Klasör Geçiş";
        AccessibleRole = AccessibleRole.ToolBar;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(500, 50);
        DoubleBuffered = true;
        BackColor = AppTheme.Surface;

        _button = new FolderButton
        {
            Name = "NavigateFolderButton",
            Dock = DockStyle.Fill,
            TabStop = false,
            CausesValidation = false,
            AccessibleRole = AccessibleRole.PushButton,
            AccessibleDefaultActionDescription = Localizer.T("Klasöre git")
        };
        _button.Click += OnNavigateClick;
        Controls.Add(_button);
        _others = new FolderButton
        {
            Name = "OtherFoldersButton", Dock = DockStyle.Right, Width = 42,
            TabStop = false, CausesValidation = false, AccessibleRole = AccessibleRole.PushButton,
            AccessibleName = Localizer.T("Açık, favori ve son klasörler"), AccessibleDefaultActionDescription = Localizer.T("Klasörleri göster"),
            MenuOnly = true, Text = "▾", Visible = false
        };
        _others.Click += (_, _) => OtherFoldersRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(_others);

        _toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 550,
            ReshowDelay = 150,
            AutoPopDelay = 15000
        };
        UpdateContent();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle = (parameters.ExStyle | WsExNoActivate | WsExToolWindow) & ~WsExAppWindow;
            return parameters;
        }
    }

    public void SetFolder(FolderLocation? folder, bool shortcutAvailable)
    {
        if (Equals(_folder, folder) && _shortcutAvailable == shortcutAvailable)
            return;

        _folder = folder;
        _shortcutAvailable = shortcutAvailable;
        UpdateContent();
    }

    public void SetShortcut(string text)
    {
        if (_shortcutText == text) return;
        _shortcutText = text;
        UpdateContent();
    }

    public void SetOtherFoldersAvailable(bool available)
    {
        bool changed = _hasMenuItems != available;
        _others.Visible = available;
        _hasMenuItems = available;
        _others.Enabled = available && !IsBusy;
        _others.Width = Math.Max(34, (int)Math.Round(42 * DeviceDpi / 96d));
        if (changed) UpdateContent();
    }

    internal Point MenuPosition => PointToScreen(new Point(0, -4));

    /// <summary>
    /// A nonempty, nonerror message denotes work in progress and disables the row.
    /// An error remains clickable for a retry. Pass null to restore the normal row.
    /// </summary>
    public void SetStatus(string? message, bool isError, bool informational = false)
    {
        message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (_status == message && _isError == isError && _informational == informational)
            return;

        _status = message;
        _isError = isError;
        _informational = informational;
        UpdateContent();
    }

    internal void EndProgress() { if (IsBusy) SetStatus(null, false); }
    internal void RefreshLanguage()
    {
        _button.RefreshLanguage(); _others.RefreshLanguage();
        _button.AccessibleDefaultActionDescription = Localizer.T("Klasöre git");
        _others.AccessibleName = Localizer.T("Açık, favori ve son klasörler");
        _others.AccessibleDefaultActionDescription = Localizer.T("Klasörleri göster");
        UpdateContent();
    }

    /// <summary>Uses caller-supplied physical screen bounds; never activates either window.</summary>
    public void AttachTo(nint dialog, Rectangle bounds)
    {
        if (IsDisposed || Disposing)
            return;

        if (dialog == 0 || !IsWindow(dialog) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            Hide();
            return;
        }

        _dialog = dialog;
        nint handle = Handle;
        // A foreign owner can destroy this HWND without disposing the managed
        // Form or clearing Visible. Recreating only Handle leaves an empty row.
        _ = _button.Handle;
        if (_others.Visible) _ = _others.Handle;
        if (!TrySetOwner(handle, dialog))
        {
            Hide();
            return;
        }

        if (!Visible)
        {
            Bounds = bounds;
            Show();
            // Form.Show may update native ownership; retain the actual file dialog as owner.
            if (!TrySetOwner(handle, dialog))
            {
                Hide();
                return;
            }
        }

        // HWND_TOP belongs to the ordinary z-order; this is deliberately not HWND_TOPMOST.
        if (!SetWindowPos(handle, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                SwpNoActivate | SwpNoOwnerZOrder | SwpShowWindow))
            Hide();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_dialog != 0 && IsWindow(_dialog))
            TrySetOwner(Handle, _dialog);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // Placement already accounts for the target monitor. Paint uses DeviceDpi.
        e.Cancel = true;
        base.OnDpiChanged(e);
        Invalidate(true);
    }

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        Invalidate(true);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmMouseActivate)
        {
            m.Result = MaNoActivate;
            return;
        }

        base.WndProc(ref m);
    }

    private void OnNavigateClick(object? sender, EventArgs e)
    {
        if (Visible && _folder is not null && _button.Enabled)
            NavigateRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateContent()
    {
        bool busy = IsBusy;
        string title = _folder?.DisplayName ?? (_hasMenuItems ? Localizer.T("Kayıtlı klasörler") : Localizer.T("Klasör bulunamadı"));
        if (string.IsNullOrWhiteSpace(title))
            title = _folder?.Path ?? Localizer.T("Klasör bulunamadı");

        string subtitle = _status ?? (_folder is null
            ? _hasMenuItems ? Localizer.T("Menüden bir favori veya son klasör seçin") : Localizer.T("Dosya Gezgini’nde bir klasör açın")
            : _shortcutAvailable
                ? _folder.Path
                : _shortcutText + Localizer.T(" kullanılamıyor · Geçmek için tıklayın"));

        _button.SetContent(title, subtitle, _isError && _status is not null,
            _shortcutAvailable, _folder is not null && !busy,
            _folder is not null && _status is null && _shortcutAvailable);
        _button.ShortcutText = _shortcutText;
        _button.HintOverride = _folder is null && _hasMenuItems ? Localizer.T("Menü") : null;
        _others.Enabled = _hasMenuItems && !busy;
        _button.AccessibleName = _folder is null
            ? title
            : Localizer.F("{0} klasörüne git: {1}", title, _folder.Path);
        _button.AccessibleDescription = _status ?? (_folder is null
            ? subtitle
            : Localizer.T("Açık dosya penceresinde bu klasöre geçin.") + " " + (_shortcutAvailable
                ? Localizer.F("Kısayol: {0}.", _shortcutText) : Localizer.F("{0} kullanılamıyor; satıra tıklayın.", _shortcutText)));

        string tip = _folder is null ? subtitle : $"{_folder.Path}\n\n{_button.AccessibleDescription}";
        _toolTip.SetToolTip(_button, tip);
        // A disabled child does not receive hover messages; the form supplies its tooltip.
        _toolTip.SetToolTip(this, tip);
        _toolTip.SetToolTip(_others, Localizer.T("Açık klasörler, favoriler ve son klasörler"));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _button.Click -= OnNavigateClick;
        }

        base.Dispose(disposing);
    }

    private static bool TrySetOwner(nint window, nint owner)
    {
        Marshal.SetLastPInvokeError(0);
        nint previous = nint.Size == 8
            ? SetWindowLongPtr64(window, GwlpHwndParent, owner)
            : (nint)SetWindowLong32(window, GwlpHwndParent, unchecked((int)owner));
        return previous != 0 || Marshal.GetLastPInvokeError() == 0;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint window, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint window, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y,
        int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    /// <summary>Retains Button accessibility and Invoke while bypassing mouse focus acquisition.</summary>
    private sealed class FolderButton : Button
    {
        internal bool MenuOnly;
        internal string ShortcutText = "Ctrl+G";
        internal string? HintOverride;
        private const int WmLeftButtonDown = 0x0201;
        private const int WmLeftButtonUp = 0x0202;
        private const int WmLeftButtonDoubleClick = 0x0203;
        private bool _hovered;
        private bool _mouseDown;
        private bool _isError;
        private bool _shortcutAvailable;
        private bool _pathSubtitle;
        private string _subtitle = string.Empty;
        private Font? _titleFont;
        private Font? _detailFont;
        private Font? _hintFont;
        private int _fontDpi;
        internal void RefreshLanguage() { _fontDpi = 0; Invalidate(); }

        internal FolderButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.UserMouse, true);
            // Keep Selectable: Button.PerformClick and its accessible Invoke require CanSelect.
            // Actual mouse-down processing is handled below without calling Select or Focus.
        }

        internal void SetContent(string title, string subtitle, bool isError,
            bool shortcutAvailable, bool canNavigate, bool pathSubtitle)
        {
            Text = title;
            _subtitle = subtitle;
            _isError = isError;
            _shortcutAvailable = shortcutAvailable;
            _pathSubtitle = pathSubtitle;
            Enabled = canNavigate;
            Cursor = canNavigate ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WmMouseActivate:
                    m.Result = MaNoActivate;
                    return;
                case WmLeftButtonDown:
                case WmLeftButtonDoubleClick:
                    if (Enabled)
                    {
                        _mouseDown = true;
                        Capture = true;
                        Invalidate();
                    }
                    m.Result = 0;
                    return;
                case WmLeftButtonUp:
                    Point point = new(unchecked((short)m.LParam.ToInt64()),
                        unchecked((short)(m.LParam.ToInt64() >> 16)));
                    bool invoke = _mouseDown && Enabled && ClientRectangle.Contains(point);
                    ResetMouseDown();
                    m.Result = 0;
                    if (invoke)
                        PerformClick();
                    return;
            }

            base.WndProc(ref m);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hovered = ClientRectangle.Contains(e.Location);
            if (_hovered != hovered)
            {
                _hovered = hovered;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture)
            {
                _mouseDown = false;
                Invalidate();
            }
            base.OnMouseCaptureChanged(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            if (!Enabled)
                ResetMouseDown();
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            if (!Visible)
            {
                _hovered = false;
                ResetMouseDown();
            }
            base.OnVisibleChanged(e);
        }

        private void ResetMouseDown()
        {
            _mouseDown = false;
            if (Capture)
                Capture = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 2 || Height < 2)
                return;

            EnsureFonts();
            Graphics graphics = e.Graphics;
            bool highContrast = SystemInformation.HighContrast;
            bool hot = Enabled && _hovered;
            bool pressed = hot && _mouseDown;
            Color background = highContrast
                ? (hot ? SystemColors.Highlight : SystemColors.Window)
                : pressed ? AppTheme.Tint
                : hot ? AppTheme.Canvas : AppTheme.Surface;
            Color foreground = highContrast
                ? (Enabled ? (hot ? SystemColors.HighlightText : SystemColors.WindowText) : SystemColors.GrayText)
                : Enabled ? AppTheme.Text : AppTheme.Muted;
            Color secondary = highContrast ? foreground
                : _isError ? AppTheme.Danger : AppTheme.Muted;
            Color border = highContrast ? foreground
                : _isError ? AppTheme.Danger : AppTheme.Border;

            graphics.Clear(background);
            using (Pen borderPen = new(border))
                graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            if (MenuOnly)
            {
                UiIcons.Draw(graphics, new Rectangle((Width - Scale(18)) / 2, (Height - Scale(18)) / 2,
                    Scale(18), Scale(18)), UiIcon.Chevron, hot ? AppTheme.SelectedText : AppTheme.Muted);
                return;
            }

            int inset = Scale(10);
            int iconWidth = Scale(32);
            if (Width > Scale(75))
            {
                var tile = new Rectangle(inset, (Height - Scale(32)) / 2, iconWidth, Scale(32));
                using var tilePath = AppTheme.Rounded(tile, Scale(7));
                using var tileBrush = new SolidBrush(highContrast ? background : AppTheme.FolderTint);
                graphics.FillPath(tileBrush, tilePath);
                UiIcons.Draw(graphics, Rectangle.Inflate(tile, -Scale(5), -Scale(5)), UiIcon.FolderGo,
                    highContrast ? foreground : AppTheme.Folder);
            }

            int textLeft = Width > Scale(75) ? inset + iconWidth + Scale(12) : Scale(6);
            int textRight = Math.Max(textLeft, Width - inset);
            string hint = HintOverride ?? (_shortcutAvailable ? ShortcutText : Localizer.T("Tıkla"));
            int hintWidth = TextRenderer.MeasureText(graphics, hint, _hintFont!, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + Scale(16);
            if (Width >= Scale(270))
            {
                Rectangle hintBounds = new(Width - inset - hintWidth, (Height - Scale(25)) / 2,
                    hintWidth, Scale(25));
                using GraphicsPath hintPath = RoundedRectangle(hintBounds, Scale(5));
                using SolidBrush hintBrush = new(highContrast ? background : AppTheme.Canvas);
                using Pen hintPen = new(highContrast ? foreground : AppTheme.Border);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.FillPath(hintBrush, hintPath);
                graphics.DrawPath(hintPen, hintPath);
                graphics.SmoothingMode = SmoothingMode.None;
                TextRenderer.DrawText(graphics, hint, _hintFont!, hintBounds, secondary,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                textRight = hintBounds.Left - Scale(14);
            }

            int textWidth = Math.Max(0, textRight - textLeft);
            int titleHeight = Math.Max(Scale(17), (int)Math.Ceiling(_titleFont!.GetHeight(graphics)));
            int detailHeight = Math.Max(Scale(14), (int)Math.Ceiling(_detailFont!.GetHeight(graphics)));
            bool showDetail = Height >= titleHeight + detailHeight + Scale(8);
            int blockHeight = titleHeight + (showDetail ? Scale(1) + detailHeight : 0);
            int textTop = Math.Max(0, (Height - blockHeight) / 2);
            TextFormatFlags textFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            if (Localizer.IsRtl) textFlags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
            TextRenderer.DrawText(graphics, Text, _titleFont!,
                new Rectangle(textLeft, textTop, textWidth, titleHeight), foreground,
                textFlags | TextFormatFlags.EndEllipsis);
            if (showDetail)
                TextRenderer.DrawText(graphics, _subtitle, _detailFont!,
                    new Rectangle(textLeft, textTop + titleHeight + Scale(1), textWidth, detailHeight), secondary,
                    textFlags | (_pathSubtitle ? TextFormatFlags.PathEllipsis : TextFormatFlags.EndEllipsis));

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(ClientRectangle, -Scale(4), -Scale(4)),
                    foreground, background);
        }

        private int Scale(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96d));

        private void EnsureFonts()
        {
            if (_fontDpi == DeviceDpi && _titleFont is not null)
                return;

            _titleFont?.Dispose();
            _detailFont?.Dispose();
            _hintFont?.Dispose();
            float scale = DeviceDpi / 96f;
            string family = Localizer.FontFamily;
            _titleFont = new Font(family, 13f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            _detailFont = new Font(family, 11.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            _hintFont = new Font(family, 11.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            _fontDpi = DeviceDpi;
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawFolder(Graphics graphics, Rectangle bounds, Color color)
        {
            float sx = bounds.Width / 24f;
            float sy = bounds.Height / 22f;
            PointF Point(float x, float y) => new(bounds.Left + x * sx, bounds.Top + y * sy);
            using GraphicsPath back = new();
            back.AddPolygon([Point(1, 4), Point(9, 4), Point(12, 7), Point(23, 7), Point(23, 19), Point(1, 19)]);
            using GraphicsPath front = new();
            front.AddPolygon([Point(1, 10), Point(23, 10), Point(20.5f, 19), Point(2.5f, 19)]);
            using Pen pen = new(color, Math.Max(1f, sx)) { LineJoin = LineJoin.Round };
            using SolidBrush brush = new(Color.FromArgb(42, color));
            SmoothingMode previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPath(brush, back);
            graphics.DrawPath(pen, back);
            graphics.DrawPath(pen, front);
            graphics.SmoothingMode = previous;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _titleFont?.Dispose();
                _detailFont?.Dispose();
                _hintFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
