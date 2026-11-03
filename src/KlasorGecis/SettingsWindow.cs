using System.Drawing.Drawing2D;

namespace KlasorGecis;

internal sealed class SettingsWindow : Form
{
    private readonly IAppSettings actions;
    private readonly Panel pages = new() { Name = "SettingsContent", Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(26, 22, 26, 22) };
    private readonly TableLayoutPanel content = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
    private readonly Label error = new() { Name = "SettingsError", AutoSize = true, ForeColor = AppTheme.Danger, MaximumSize = new Size(650, 0), Margin = new Padding(18, 8, 18, 8), AccessibleRole = AccessibleRole.Alert };
    private readonly Label status = new() { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 8, 0) };
    private readonly Dictionary<string, Button> navigation = [];
    private readonly Dictionary<string, ToggleSwitch> switches = [];
    private Label? shortcutValue, recentCount;
    private Button? clearHistory, addFavorite;
    private TableLayoutPanel? favorites;
    private string favoriteFingerprint = "";
    private string page = "general";
    private ComboBox? language;
    private bool changingLanguage;
    private Icon? windowIcon;
    private readonly ToolTip tips = new() { ShowAlways = true, InitialDelay = 550, AutoPopDelay = 15000 };

    internal SettingsWindow(IAppSettings actions)
    {
        this.actions = actions;
        Name = "KlasorGecisSettings"; Text = Localizer.T("Klasör Geçiş — Ayarlar");
        Font = AppTheme.Body; BackColor = AppTheme.Surface; ForeColor = AppTheme.Text;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, Math.Min(680, Math.Max(510, Screen.PrimaryScreen?.WorkingArea.Height - 80 ?? 680)));
        MinimumSize = new Size(650, 510);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = windowIcon = Icon.ExtractAssociatedIcon(StartupSettings.ExecutablePath); } catch { }
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); error.Visible = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(18, 0, 18, 0), BackColor = AppTheme.Surface, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var brand = new Label { Text = "  Klasör Geçiş", Font = AppTheme.Strong, Image = UiIcons.Image(UiIcon.FolderGo, AppTheme.Folder, 24), ImageAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false, Size = new Size(150, 44), Anchor = AnchorStyles.Left, AccessibleName = "Klasör Geçiş" };
        header.Controls.Add(brand); header.Controls.Add(status);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.Single };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var rail = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = AppTheme.Canvas, Padding = new Padding(9, 18, 9, 9), Margin = Padding.Empty };
        rail.Controls.Add(new Label { Name = "SettingsRailHeading", Text = Localizer.T("TERCİHLER"), AutoSize = true, Font = AppTheme.Small, ForeColor = AppTheme.Muted, Margin = new Padding(12, 0, 0, 12) });
        foreach (var entry in new[] { ("general", Localizer.T("Genel"), UiIcon.Settings), ("shortcut", Localizer.T("Kısayol"), UiIcon.Keyboard), ("favorites", Localizer.T("Favoriler"), UiIcon.Star), ("privacy", Localizer.T("Geçmiş"), UiIcon.Shield) })
        {
            var button = new Button { Name = "SettingsPage_" + entry.Item1, Text = "  " + entry.Item2, Size = new Size(137, 42), TextAlign = ContentAlignment.MiddleLeft,
                ImageAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText, Padding = new Padding(6, 0, 0, 0),
                Image = UiIcons.Image(entry.Item3), Margin = new Padding(0, 2, 0, 2), AccessibleName = entry.Item2 };
            AppTheme.Style(button); button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => SelectPage(entry.Item1); navigation[entry.Item1] = button; rail.Controls.Add(button);
        }
        pages.Controls.Add(content); body.Controls.Add(rail, 0, 0); body.Controls.Add(pages, 1, 0);
        var footer = new Label { Dock = DockStyle.Fill, Text = "Klasör Geçiş", Font = AppTheme.Small, ForeColor = AppTheme.Muted, BackColor = AppTheme.Canvas, Padding = new Padding(18, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
        root.Controls.Add(header, 0, 0); root.Controls.Add(body, 0, 1); root.Controls.Add(error, 0, 2); root.Controls.Add(footer, 0, 3); Controls.Add(root);
        Activated += (_, _) => RefreshState();
        Resize += (_, _) => error.MaximumSize = new Size(Math.Max(100, ClientSize.Width - 36), 0);
        SelectPage("general");
        Localizer.Changed += OnLanguageChanged;
        Localizer.ApplyOrientation(this);
    }

    internal void SelectPage(string name)
    {
        page = navigation.ContainsKey(name) ? name : "general";
        SuspendLayout(); content.SuspendLayout();
        foreach (Control control in content.Controls.Cast<Control>().ToArray()) control.Dispose();
        content.Controls.Clear(); content.RowStyles.Clear(); content.RowCount = 0;
        switches.Clear(); shortcutValue = recentCount = null; clearHistory = addFavorite = null; favorites = null; favoriteFingerprint = "";
        language = null;
        foreach (var item in navigation) { item.Value.BackColor = item.Key == page ? AppTheme.Tint : AppTheme.Canvas; item.Value.ForeColor = item.Key == page ? AppTheme.SelectedText : AppTheme.Text; }
        var title = new Label { AutoSize = true, Dock = DockStyle.Top, Font = AppTheme.Heading, Text = page switch { "shortcut" => Localizer.T("Kısayol"), "favorites" => Localizer.T("Favoriler"), "privacy" => Localizer.T("Son klasörler"), _ => Localizer.T("Genel") }, Margin = new Padding(0, 0, 0, 7) };
        Add(title);
        Add(Description(page switch { "shortcut" => Localizer.T("Klasöre geçmek için size uygun bir tuş birleşimi seçin."), "favorites" => Localizer.T("Sık kullandığınız klasörleri her zaman elinizin altında tutun."), "privacy" => Localizer.T("Klasör geçmişinizi tutup tutmamak sizin seçiminiz."), _ => Localizer.T("Dosya pencerelerindeki davranışı ayarlayın.") }, 16));
        if (page == "general")
        {
            Add(LanguageRow());
            Section(Localizer.T("Klasör geçişi"));
            Add(SwitchRow("enabled", Localizer.T("Geçiş satırını göster"), Localizer.T("Aç ve Kaydet pencerelerinde klasörlerinize hızlıca ulaşın."), actions.SetEnabled));
            Add(SwitchRow("automatic", Localizer.T("Explorer’dan dönünce otomatik geç"), Localizer.T("Aynı dosya penceresine döndüğünüzde son ziyaret ettiğiniz Explorer klasörüne geçin."), actions.SetAutomatic));
            Section(Localizer.T("Klavye")); Add(ShortcutRow());
            Section(Localizer.T("Başlangıç")); Add(SwitchRow("startup", Localizer.T("Windows ile başlat"), Localizer.T("Oturum açtığınızda bildirim alanında hazır olsun."), actions.SetStartup));
        }
        else if (page == "shortcut")
        {
            shortcutValue = new Label { Name = "CurrentShortcut", Dock = DockStyle.Top, AutoSize = false, Height = 94, Font = AppTheme.Heading,
                BackColor = AppTheme.Canvas, BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 7, 0, 16), AccessibleName = Localizer.T("Geçerli kısayol") };
            Add(shortcutValue);
            var buttons = ButtonRow(); buttons.Controls.Add(Button(Localizer.T("Kısayolu değiştir"), "ChangeShortcut", EditShortcut, true));
            buttons.Controls.Add(Button(Localizer.T("Varsayılana dön"), "ResetShortcut", () => Apply(actions.SetShortcut(ShortcutBinding.Default)))); Add(buttons);
            Add(Description(Localizer.T("Başka bir uygulamanın kullandığı kısayol seçilirse önceki ayarınız korunur."), 14));
            Add(Row(Localizer.T("Fareyle de kullanabilirsiniz"), Localizer.T("Kısayoldan bağımsız olarak geçiş satırına veya menüdeki bir klasöre tıklayabilirsiniz."), null));
        }
        else if (page == "favorites")
        {
            favorites = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Margin = Padding.Empty };
            favorites.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Add(favorites);
            addFavorite = Button(Localizer.T("Explorer klasörünü ekle"), "AddCurrentFavorite", () => Apply(actions.AddCurrentFavorite()));
            var buttons = ButtonRow(); buttons.Controls.Add(addFavorite); Add(buttons);
            Add(Description(Localizer.T("Explorer açık olmasa da favori klasörlerinize menüden geçebilirsiniz."), 0));
        }
        else
        {
            Add(SwitchRow("history", Localizer.T("Son klasörleri hatırla"), Localizer.T("Yalnız başarılı geçişlerin klasörü ve ilgili uygulama kaydedilir. En fazla 20 kayıt tutulur."), actions.SetHistory));
            Add(Description(Localizer.T("Dosya adları ve yazdığınız metinler geçmişe eklenmez. Bu seçenek başlangıçta kapalıdır."), 12));
            clearHistory = Button("Temizle", "ClearHistory", () => Apply(actions.ClearHistory())); clearHistory.ForeColor = AppTheme.Danger;
            var row = Row(Localizer.T("Geçmişi temizle"), "", clearHistory);
            recentCount = row.Controls.Find("RowDescription", true).OfType<Label>().Single(); Add(row);
            Add(Description(Localizer.T("Geçmişi kapatmak yeni kaydı durdurur. Önceki kayıtlar yalnız siz temizlerseniz silinir."), 0));
        }
        content.ResumeLayout(true); ResumeLayout(true); pages.AutoScrollPosition = Point.Empty; RefreshState();
        Localizer.ApplyOrientation(this);
    }

    private TableLayoutPanel LanguageRow()
    {
        language = new ComboBox { Name = "AppLanguage", DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 190, AccessibleName = Localizer.T("Dil"), TabIndex = 0 };
        language.Items.AddRange(Localizer.Languages);
        language.SelectedItem = Localizer.Languages.Single(l => l.Code == actions.ReadSettings().Language);
        language.SelectedIndexChanged += (_, _) =>
        {
            if (changingLanguage || language?.SelectedItem is not LanguageOption choice || choice.Code == actions.ReadSettings().Language) return;
            changingLanguage = true;
            try { Apply(actions.SetLanguage(choice.Code)); }
            finally { changingLanguage = false; }
        };
        return Row(Localizer.T("Uygulama dili"), Localizer.T("Değişiklik hemen uygulanır."), language);
    }

    private void OnLanguageChanged()
    {
        if (IsDisposed) return;
        void RefreshLanguage()
        {
            if (IsDisposed) return;
            Text = Localizer.T("Klasör Geçiş — Ayarlar");
            Controls.Find("SettingsRailHeading", true).Single().Text = Localizer.T("TERCİHLER");
            foreach (var item in navigation)
                item.Value.Text = "  " + (item.Key switch { "shortcut" => Localizer.T("Kısayol"), "favorites" => Localizer.T("Favoriler"), "privacy" => Localizer.T("Geçmiş"), _ => Localizer.T("Genel") });
            SelectPage(page);
        }
        // Defer disposal of the language selector until its native change event ends.
        if (IsHandleCreated) BeginInvoke((Action)RefreshLanguage); else RefreshLanguage();
    }

    private void Add(Control control)
    {
        content.RowCount++; content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        content.Controls.Add(control, 0, content.RowCount - 1);
    }
    private void Section(string text) => Add(new Label { Text = text, AutoSize = true, Font = AppTheme.Strong, Margin = new Padding(0, 15, 0, 9) });
    private static Label Description(string text, int bottom) => new() { Text = text, AutoSize = true, Dock = DockStyle.Top, ForeColor = AppTheme.Muted, Margin = new Padding(0, 0, 0, bottom), Padding = new Padding(0, 0, 0, 2) };
    private static FlowLayoutPanel ButtonRow() => new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 14, 0, 14) };
    private static Button Button(string text, string name, Action click, bool primary = false)
    {
        var button = new Button { Text = text, Name = name, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(86, 34), Padding = new Padding(10, 3, 10, 3), Margin = new Padding(0, 0, 9, 5), AccessibleName = text };
        AppTheme.Style(button, primary); button.Click += (_, _) => click(); return button;
    }
    private static TableLayoutPanel Row(string title, string description, Control? action)
    {
        var row = new SettingsCard { AutoSize = true, Dock = DockStyle.Top, ColumnCount = action is null ? 1 : 2, Padding = new Padding(16, 13, 16, 13), Margin = new Padding(0, 0, 0, 7), BackColor = AppTheme.Surface, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (action is not null) row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var labels = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 1, Margin = new Padding(0, 0, action is null ? 0 : 18, 0) };
        labels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        labels.Controls.Add(new Label { Text = title, AutoSize = true, Dock = DockStyle.Top, Font = AppTheme.Strong, Margin = new Padding(0, 0, 0, 4) });
        labels.Controls.Add(new Label { Name = "RowDescription", Text = description, AutoSize = true, Dock = DockStyle.Top, ForeColor = AppTheme.Muted, Margin = Padding.Empty });
        row.Controls.Add(labels, 0, 0);
        if (action is not null) { action.Anchor = AnchorStyles.Right; action.Margin = Padding.Empty; row.Controls.Add(action, 1, 0); }
        return row;
    }
    private TableLayoutPanel SwitchRow(string key, string title, string detail, Func<bool, string?> change)
    {
        var toggle = new ToggleSwitch { Name = "Setting_" + key, AccessibleName = title, AccessibleDescription = detail };
        toggle.Click += (_, _) => Apply(change(!toggle.Checked)); switches[key] = toggle;
        return Row(title, detail, toggle);
    }
    private TableLayoutPanel ShortcutRow()
    {
        var button = Button(Localizer.T("Kısayolu değiştir"), "ChangeShortcut", EditShortcut);
        var row = Row(Localizer.T("Klasöre geçiş kısayolu"), "", button);
        shortcutValue = row.Controls.Find("RowDescription", true).OfType<Label>().Single();
        shortcutValue.Name = "CurrentShortcut"; return row;
    }
    private void EditShortcut()
    {
        using var dialog = new ShortcutDialog(actions.ReadSettings().Shortcut, actions.SetShortcut);
        dialog.ShowDialog(this); RefreshState();
    }
    private void Apply(string? message)
    {
        error.Text = message ?? ""; error.Visible = message is not null;
        RefreshState();
        if (message is not null) AccessibilityNotifyClients(AccessibleEvents.SystemAlert, -1);
    }
    internal void RefreshState()
    {
        if (IsDisposed) return;
        var state = actions.ReadSettings();
        if (language is not null)
        {
            changingLanguage = true;
            language.SelectedItem = Localizer.Languages.Single(l => l.Code == state.Language);
            changingLanguage = false;
        }
        status.Text = state.Enabled ? Localizer.T("Etkin") : Localizer.T("Duraklatıldı");
        status.ForeColor = state.Enabled ? AppTheme.Primary : AppTheme.Muted;
        foreach (var item in switches)
        {
            item.Value.Checked = item.Key switch { "enabled" => state.Enabled, "automatic" => state.Automatic, "history" => state.History, _ => state.Startup };
            if (item.Key == "automatic") item.Value.Enabled = state.AutomaticAvailable;
        }
        if (shortcutValue is not null) shortcutValue.Text = state.Shortcut.DisplayText;
        if (recentCount is not null) recentCount.Text = state.RecentCount == 0 ? Localizer.T("Kaydedilmiş klasör yok.") : Localizer.F("{0} klasör kaydı var.", state.RecentCount);
        if (clearHistory is not null) clearHistory.Enabled = state.RecentCount > 0;
        if (state.Warning is not null && string.IsNullOrEmpty(error.Text)) { error.Text = state.Warning; error.Visible = true; }
        if (favorites is null) return;
        if (addFavorite is not null)
        {
            addFavorite.Enabled = state.CurrentFolder is not null && !state.Favorites.Any(f => string.Equals(f.Path, state.CurrentFolder.Path, StringComparison.OrdinalIgnoreCase));
            addFavorite.AccessibleDescription = state.CurrentFolder?.Path ?? Localizer.T("Açık bir Explorer klasörü bulunamadı.");
        }
        string fingerprint = string.Join('\n', state.Favorites.Select(f => f.Path + "\t" + f.DisplayName));
        if (fingerprint == favoriteFingerprint && favorites.Controls.Count > 0) return;
        favoriteFingerprint = fingerprint;
        foreach (Control control in favorites.Controls.Cast<Control>().ToArray()) control.Dispose(); favorites.Controls.Clear();
        favorites.RowStyles.Clear(); favorites.RowCount = 0;
        foreach (var favorite in state.Favorites)
        {
            var remove = Button(Localizer.T("Kaldır"), "RemoveFavorite", () => { Apply(actions.RemoveFavorite(favorite.Path)); if (addFavorite?.Enabled == true) addFavorite.Focus(); else navigation["favorites"].Focus(); });
            remove.AccessibleName = Localizer.F("{0} klasörünü favoriden kaldır", favorite.DisplayName);
            var row = Row(favorite.DisplayName, favorite.Path, remove);
            row.AccessibleName = favorite.DisplayName + ", " + favorite.Path;
            var labels = row.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<Label>().ToArray();
            foreach (var label in labels)
            {
                label.Name = "Data_" + label.Name;
                label.AutoSize = false; label.AutoEllipsis = true;
                label.Height = label.Font.Height * 2 + 2;
                label.AccessibleName = label.Text; tips.SetToolTip(label, label.Text);
            }
            favorites.RowCount++; favorites.RowStyles.Add(new RowStyle(SizeType.AutoSize)); favorites.Controls.Add(row, 0, favorites.RowCount - 1);
        }
        if (state.Favorites.Length == 0) favorites.Controls.Add(Description(Localizer.T("Henüz favoriniz yok. Explorer'da bir klasör açıp ekleyebilirsiniz."), 12));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { Localizer.Changed -= OnLanguageChanged; tips.Dispose(); windowIcon?.Dispose(); windowIcon = null; }
        base.Dispose(disposing);
    }

    private sealed class SettingsCard : TableLayoutPanel
    {
        internal SettingsCard() => DoubleBuffered = true;
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var shape = AppTheme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 7 * DeviceDpi / 96f);
            using var pen = new Pen(AppTheme.Border); e.Graphics.DrawPath(pen, shape);
        }
    }

    private sealed class ToggleSwitch : CheckBox
    {
        internal ToggleSwitch()
        {
            AutoCheck = false; AutoSize = false; Size = new Size(48, 30); Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int inset = Math.Max(2, (int)Math.Round(4 * DeviceDpi / 96d));
            var track = new Rectangle(inset, inset, Math.Max(1, Width - inset * 2), Math.Max(1, Height - inset * 2));
            using var path = AppTheme.Rounded(track, track.Height / 2f);
            using var brush = new SolidBrush(Checked ? AppTheme.Primary : AppTheme.Canvas);
            using var pen = new Pen(Checked ? AppTheme.Primary : AppTheme.ControlBorder);
            e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path);
            int dot = Math.Max(1, track.Height - inset);
            var dotBounds = new Rectangle(Checked != Localizer.IsRtl ? track.Right - dot - inset / 2 : track.Left + inset / 2, track.Top + inset / 2, dot, dot);
            using var dotBrush = new SolidBrush(Checked ? SystemColors.HighlightText : Enabled ? AppTheme.Muted : SystemColors.GrayText);
            e.Graphics.FillEllipse(dotBrush, dotBounds);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, AppTheme.Primary, BackColor);
        }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    }
}
