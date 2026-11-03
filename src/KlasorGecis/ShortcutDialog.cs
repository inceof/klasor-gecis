namespace KlasorGecis;

internal sealed class ShortcutDialog : Form
{
    internal ShortcutDialog(ShortcutBinding current, Func<ShortcutBinding, string?> apply)
    {
        Text = Localizer.T("Kısayol ayarı");
        Name = "KlasorGecisShortcutSettings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(470, 286);
        BackColor = AppTheme.Surface; ForeColor = AppTheme.Text; Font = AppTheme.Body;
        var title = new Label { Text = Localizer.T("Kısayolu değiştir"), Font = AppTheme.Heading,
            AutoSize = false, Bounds = new Rectangle(24, 18, 422, 34) };
        var help = new Label { Text = Localizer.T("Ctrl veya Alt ile bir harf, sayı ya da F tuşuna basın."),
            ForeColor = AppTheme.Muted, AutoSize = false, Bounds = new Rectangle(24, 62, 422, 28) };
        var entryLabel = new Label { Text = Localizer.T("Yeni kısayol"), Font = AppTheme.Strong, Bounds = new Rectangle(24, 96, 422, 20) };
        var entry = new TextBox { ReadOnly = true, Text = current.DisplayText,
            Name = "ShortcutEntry", AccessibleName = Localizer.T("Yeni kısayol"), Bounds = new Rectangle(24, 120, 422, 36),
            Font = AppTheme.Heading, BackColor = AppTheme.Canvas, ForeColor = AppTheme.Text, TextAlign = HorizontalAlignment.Center };
        var status = new Label { AutoSize = false, Bounds = new Rectangle(24, 166, 422, 48), ForeColor = AppTheme.Muted,
            Text = Localizer.T("Kısayol başka bir programdaysa önceki ayar korunur.") };
        var chosen = current;
        entry.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Tab or Keys.Escape) return;
            e.SuppressKeyPress = true;
            if (ShortcutBinding.FromKeys(e.KeyData) is { } candidate)
            {
                chosen = candidate; entry.Text = chosen.DisplayText; status.ForeColor = AppTheme.Muted; status.Text = Localizer.T("Kaydet ile uygulayın.");
            }
            else { status.ForeColor = AppTheme.Danger; status.Text = Localizer.T("Ctrl/Alt ile harf, sayı veya F tuşu kullanın. F12 ve Alt+F4 ayrılamaz."); }
        };
        var reset = new Button { Text = Localizer.T("Varsayılan"), Bounds = new Rectangle(24, 226, 110, 36) };
        reset.Click += (_, _) => { chosen = ShortcutBinding.Default; entry.Text = chosen.DisplayText; status.ForeColor = AppTheme.Muted; status.Text = Localizer.T("Kaydet ile uygulayın."); };
        var save = new Button { Text = Localizer.T("Kaydet"), Bounds = new Rectangle(244, 226, 96, 36) };
        save.Click += (_, _) =>
        {
            string? error = apply(chosen);
            if (error is not null) { status.ForeColor = AppTheme.Danger; status.Text = error; return; }
            DialogResult = DialogResult.OK; Close();
        };
        var cancel = new Button { Text = Localizer.T("İptal"), DialogResult = DialogResult.Cancel, Bounds = new Rectangle(350, 226, 96, 36) };
        AppTheme.Style(reset); AppTheme.Style(save, true); AppTheme.Style(cancel);
        CancelButton = cancel;
        Controls.AddRange([title, help, entryLabel, entry, status, reset, save, cancel]);
        Localizer.ApplyOrientation(this);
        Shown += (_, _) => entry.Focus();
    }
}
