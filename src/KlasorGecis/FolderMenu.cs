namespace KlasorGecis;

internal static class FolderMenu
{
    internal static void Populate(ContextMenuStrip menu, IReadOnlyList<FolderLocation> folders,
        PreferencesStore preferences, string? application, FolderLocation? selected,
        Action<FolderRequest> navigate, Action<string?> changed, int maximumWidth, Action? settings = null)
    {
        foreach (var item in menu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
        menu.Items.Clear();
        AppTheme.Style(menu);
        menu.RightToLeft = Localizer.IsRtl ? RightToLeft.Yes : RightToLeft.No;
        menu.ShowItemToolTips = true;
        menu.ShowImageMargin = menu.ShowCheckMargin = false;
        int Scale(int value) => Math.Max(1, (int)Math.Round(value * menu.DeviceDpi / 96d));
        int width = Math.Max(80, Math.Min(maximumWidth - 8, Scale(420)));
        // DropDownMenu also measures its internal arrow/text columns. Bound the
        // native popup, not just the owner-drawn captions and hosted control.
        menu.MaximumSize = new Size(width + Scale(8), menu.MaximumSize.Height);
        var sections = new Dictionary<string, List<ToolStripItem>> { ["open"] = [], ["favorites"] = [], ["recent"] = [] };
        string FitCaption(string caption)
        {
            int first = 0, last = caption.Length;
            while (first < last)
            {
                int middle = (first + last + 1) / 2;
                string candidate = caption[..middle] + (middle < caption.Length ? "…" : "");
                if (TextRenderer.MeasureText(candidate, menu.Font, Size.Empty, TextFormatFlags.NoPrefix).Width <= width - Scale(55)) first = middle;
                else last = middle - 1;
            }
            if (first > 0 && first < caption.Length && char.IsHighSurrogate(caption[first - 1])) first--;
            caption = caption[..first] + (first < caption.Length ? "…" : "");
            return caption;
        }
        ToolStripMenuItem Item(string title, string path, FolderRequest request)
        {
            string caption = FitCaption(title + " — " + path);
            var item = new FolderChoiceItem(title, path, width, Scale(60), request.Origin == FolderOrigin.Favorite ? UiIcon.Star : UiIcon.Folder)
            { Text = caption.Replace("&", "&&"), Tag = request, ToolTipText = path, AccessibleName = title + ", " + path };
            item.Click += (_, _) => navigate(request);
            return item;
        }
        void Choice(string section, string title, string path, FolderRequest request)
        {
            var item = Item(title, path, request);
            item.Checked = request.ExplorerSource == selected && request.Origin == FolderOrigin.OpenExplorer;
            sections[section].Add(item);
        }
        var state = preferences.Current;
        foreach (var folder in folders) Choice("open", folder.DisplayName, folder.Path, FolderRequest.Explorer(folder));
        foreach (var folder in state.Favorites.DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
            if (FolderRequest.Saved(FolderOrigin.Favorite, folder.Path) is { } request) Choice("favorites", folder.DisplayName, folder.Path, request);
        var recent = preferences.RecentFor(application);
        foreach (var folder in recent)
            if (FolderRequest.Saved(FolderOrigin.Recent, folder.Path) is { } request)
                Choice("recent", Path.GetFileName(folder.Path.TrimEnd('\\')) is { Length: > 0 } name ? name : folder.Path, folder.Path, request);

        var header = new ToolStripLabel(Localizer.T("Klasörler")) { Font = AppTheme.Strong, AutoSize = false, Size = new Size(width, Scale(36)), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Scale(14), 0, 0, 0) };
        menu.Items.Add(header);
        var tabs = new FolderTabs { AutoSize = false, Size = new Size(width, Scale(40)), GripStyle = ToolStripGripStyle.Hidden,
            RightToLeft = menu.RightToLeft,
            BackColor = AppTheme.Surface, Padding = Padding.Empty, Margin = Padding.Empty, TabStop = false, Font = AppTheme.Body, Renderer = menu.Renderer };
        string initial = sections["open"].Count > 0 ? "open" : sections["favorites"].Count > 0 ? "favorites" : "recent";
        foreach (var tab in new[] { ("open", "OpenFoldersTab", Localizer.T("Açık klasörler")), ("favorites", "FavoriteFoldersTab", Localizer.T("Favoriler")), ("recent", "RecentFoldersTab", Localizer.T("Son klasörler")) })
        {
            var button = new ToolStripButton(tab.Item3) { Name = tab.Item2, Tag = tab.Item1, AutoSize = false,
                Size = new Size(width / 3, Scale(39)), DisplayStyle = ToolStripItemDisplayStyle.Text, Margin = Padding.Empty,
                AccessibleName = tab.Item3, AccessibleRole = AccessibleRole.PageTab, Checked = tab.Item1 == initial };
            button.Click += (_, _) => Select(tab.Item1);
            tabs.Items.Add(button);
        }
        menu.Items.Add(new ToolStripControlHost(tabs) { AutoSize = false, Size = tabs.Size, Margin = Padding.Empty, Padding = Padding.Empty });
        menu.Items.Add(new ToolStripSeparator());
        foreach (var section in sections)
        {
            if (section.Value.Count == 0)
            {
                string title = section.Key switch { "open" => Localizer.T("Açık Explorer klasörü yok"), "favorites" => Localizer.T("Henüz favoriniz yok"), _ => state.HistoryEnabled ? Localizer.T("Henüz bir geçiş yapılmadı") : Localizer.T("Son klasörler kapalı") };
                string detail = section.Key switch { "open" => Localizer.T("Favorileriniz Explorer kapalıyken de kullanılabilir."), "favorites" => Localizer.T("Bir Explorer klasörünü favorilerinize ekleyin."), _ => Localizer.T("Geçmiş tercihini Ayarlar'dan değiştirebilirsiniz.") };
                section.Value.Add(new FolderChoiceItem(title, detail, width, Scale(80), UiIcon.Info) { Enabled = false, AccessibleName = title + ". " + detail });
            }
            foreach (var item in section.Value) menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        if (selected is not null)
        {
            bool exists = state.Favorites.Any(f => string.Equals(f.Path, selected.Path, StringComparison.OrdinalIgnoreCase));
            var add = new ToolStripMenuItem(exists ? Localizer.T("Bu Explorer klasörünü favoriden kaldır") : Localizer.T("Bu Explorer klasörünü favoriye ekle")) { Name = "ToggleFavorite" };
            add.Click += (_, _) =>
            {
                string? error;
                if (exists) preferences.RemoveFavorite(selected.Path, out error);
                else preferences.AddFavorite(selected.Path, selected.DisplayName, out error);
                changed(error);
            };
            menu.Items.Add(add);
        }
        if (state.Favorites.Length != 0)
        {
            var remove = new ToolStripMenuItem(Localizer.T("Favoriden kaldır"));
            remove.DropDown.MaximumSize = new Size(width + Scale(8), Math.Max(100, menu.MaximumSize.Height));
            remove.DropDown.Renderer = menu.Renderer; remove.DropDown.Font = menu.Font;
            foreach (var favorite in state.Favorites)
            {
                var item = new ToolStripMenuItem(FitCaption(favorite.DisplayName).Replace("&", "&&"))
                { ToolTipText = favorite.Path, AccessibleName = favorite.DisplayName + ", " + favorite.Path };
                item.Click += (_, _) => { preferences.RemoveFavorite(favorite.Path, out var error); changed(error); };
                remove.DropDownItems.Add(item);
            }
            menu.Items.Add(remove);
        }
        if (settings is not null)
        {
            var openSettings = new ToolStripMenuItem(Localizer.T("Ayarlar…")) { Name = "FolderSettings" };
            openSettings.Click += (_, _) => settings(); menu.Items.Add(openSettings);
        }
        Select(initial);
        foreach (var footer in menu.Items.OfType<ToolStripMenuItem>().Where(item => item is not FolderChoiceItem))
        { footer.AutoSize = false; footer.Size = new Size(width, Scale(30)); }

        void Select(string key)
        {
            menu.SuspendLayout();
            foreach (ToolStripButton button in tabs.Items) button.Checked = Equals(button.Tag, key);
            foreach (var section in sections) foreach (var item in section.Value) item.Available = section.Key == key;
            menu.ResumeLayout(true); menu.Invalidate();
        }
    }

    private sealed class FolderTabs : ToolStrip
    {
        private bool down;
        internal FolderTabs() => SetStyle(ControlStyles.Selectable, false);
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x21) { message.Result = 3; return; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
            if (message.Msg is 0x201 or 0x203)
            {
                down = true; Capture = true; message.Result = 0; return;
            }
            if (message.Msg == 0x202)
            {
                var point = new Point(unchecked((short)message.LParam.ToInt64()), unchecked((short)(message.LParam.ToInt64() >> 16)));
                bool invoke = down; down = false; Capture = false; message.Result = 0;
                if (invoke && GetItemAt(point) is ToolStripButton { Enabled: true } button) button.PerformClick();
                return;
            }
            base.WndProc(ref message);
        }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) down = false; base.OnMouseCaptureChanged(e); }
    }

    private sealed class FolderChoiceItem(string title, string path, int width, int height, UiIcon icon) : ToolStripMenuItem
    {
        public override Size GetPreferredSize(Size constrainingSize) => new(width, height);
        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            bool hot = Selected && Enabled;
            var foreground = hot ? AppTheme.SelectedText : Enabled ? AppTheme.Text : AppTheme.Muted;
            var detail = SystemInformation.HighContrast && hot ? SystemColors.HighlightText : AppTheme.Muted;
            graphics.Clear(AppTheme.Surface);
            int Scale(int value) => Math.Max(1, (int)Math.Round(value * (Owner?.DeviceDpi ?? 96) / 96d));
            if (hot)
            {
                using var shape = AppTheme.Rounded(new Rectangle(3, 1, Math.Max(1, Width - 6), Height - 2), Scale(6));
                using var brush = new SolidBrush(AppTheme.Tint); graphics.FillPath(brush, shape);
            }
            UiIcons.Draw(graphics, new Rectangle(Scale(14), (Height - Scale(22)) / 2, Scale(22), Scale(22)), icon,
                SystemInformation.HighContrast ? foreground : Enabled ? AppTheme.Folder : AppTheme.Muted);
            int left = Scale(48), right = Width - Scale(16) - (Checked ? Scale(22) : 0);
            int textWidth = Math.Max(1, right - left);
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(graphics, title, AppTheme.Strong, new Rectangle(left, Height / 2 - Scale(19), textWidth, Scale(20)), foreground, flags | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(graphics, path, AppTheme.Small, new Rectangle(left, Height / 2 + Scale(3), textWidth, Scale(18)), detail, flags | TextFormatFlags.PathEllipsis);
            if (Checked) UiIcons.Draw(graphics, new Rectangle(Width - Scale(30), (Height - Scale(17)) / 2, Scale(17), Scale(17)), UiIcon.Check,
                SystemInformation.HighContrast ? foreground : AppTheme.Primary);
        }
    }
}

internal sealed class FolderPickerMenu : ContextMenuStrip
{
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Tab && (keyData & Keys.Control) != 0 && (keyData & Keys.Alt) == 0)
        {
            var tabs = Items.OfType<ToolStripControlHost>().Select(h => h.Control).OfType<ToolStrip>().SingleOrDefault();
            if (tabs is not null)
            {
                var buttons = tabs.Items.OfType<ToolStripButton>().ToArray();
                int current = Array.FindIndex(buttons, b => b.Checked);
                int direction = (keyData & Keys.Shift) != 0 ? -1 : 1;
                if (buttons.Length > 0) buttons[(current + direction + buttons.Length) % buttons.Length].PerformClick();
                return true;
            }
        }
        return base.ProcessDialogKey(keyData);
    }
}
