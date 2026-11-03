using System.Text.Json;

namespace KlasorGecis;

internal sealed record ShortcutBinding(uint Modifiers, uint Key)
{
    internal static ShortcutBinding Default => new(2, 0x47);
    internal uint RegistrationModifiers => Modifiers | 0x4000; // MOD_NOREPEAT
    internal string DisplayText => ((Modifiers & 2) != 0 ? "Ctrl+" : "") +
        ((Modifiers & 1) != 0 ? "Alt+" : "") + ((Modifiers & 4) != 0 ? "Shift+" : "") +
        (Key is >= 0x30 and <= 0x39 ? ((char)Key).ToString() : ((Keys)Key).ToString());

    internal static ShortcutBinding? FromKeys(Keys keys)
    {
        var key = keys & Keys.KeyCode;
        if ((keys & ~(Keys.KeyCode | Keys.Control | Keys.Alt | Keys.Shift)) != 0 ||
            (keys & (Keys.Control | Keys.Alt)) == 0 ||
            !(key is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9 or >= Keys.F1 and <= Keys.F24) ||
            key == Keys.F12 || ((keys & Keys.Alt) != 0 && key == Keys.F4)) return null;
        uint modifiers = ((keys & Keys.Control) != 0 ? 2u : 0) |
            ((keys & Keys.Alt) != 0 ? 1u : 0) | ((keys & Keys.Shift) != 0 ? 4u : 0);
        return new(modifiers, (uint)key);
    }

    internal bool IsValid => Modifiers <= 7 && FromKeys((Keys)Key |
        ((Modifiers & 2) != 0 ? Keys.Control : 0) | ((Modifiers & 1) != 0 ? Keys.Alt : 0) |
        ((Modifiers & 4) != 0 ? Keys.Shift : 0)) == this;
}

internal static class ShortcutSettings
{
    internal static string FilePath => Path.Combine(Diagnostics.DirectoryPath, "shortcut.json");

    internal static ShortcutBinding Load(string path, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(path)) return ShortcutBinding.Default;
            var binding = JsonSerializer.Deserialize<ShortcutBinding>(File.ReadAllText(path));
            if (binding is null || !binding.IsValid) throw new JsonException();
            return binding;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            error = Localizer.T("Kısayol ayarı okunamadı; varsayılan kullanılıyor. Mevcut ayar dosyası korunuyor.");
            return ShortcutBinding.Default;
        }
    }

    internal static bool Save(string path, ShortcutBinding binding, out string? error)
    {
        error = null;
        if (!binding.IsValid) { error = Localizer.T("Geçerli bir kısayol seçin."); return false; }
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(binding));
            File.Move(temporary, path, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = Localizer.T("Kısayol kaydedilemedi; önceki ayar korunuyor.");
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}

internal sealed class ShortcutRegistration(nint window, ShortcutBinding initial) : IDisposable
{
    internal ShortcutBinding Binding { get; private set; } = initial;
    internal bool Active { get; private set; }
    internal int Id { get; private set; } = 1;

    internal bool Register()
    {
        if (!Active) Active = Native.RegisterHotKey(window, Id, Binding.RegistrationModifiers, Binding.Key);
        return Active;
    }

    internal void Unregister()
    {
        if (Active) Native.UnregisterHotKey(window, Id);
        Active = false;
    }

    internal string? Apply(ShortcutBinding candidate, string settingsPath)
    {
        if (!candidate.IsValid) return Localizer.T("Geçerli bir kısayol seçin.");
        if (candidate == Binding && Active)
            return ShortcutSettings.Save(settingsPath, candidate, out var sameError) ? null : sameError;
        int nextId = Active ? 3 - Id : Id;
        if (!Native.RegisterHotKey(window, nextId, candidate.RegistrationModifiers, candidate.Key))
            return Localizer.T("Bu kısayol kullanılamıyor. Başka bir kısayol seçin; önceki ayar korunuyor.");
        if (!ShortcutSettings.Save(settingsPath, candidate, out var error))
        {
            Native.UnregisterHotKey(window, nextId);
            return error;
        }
        bool wasActive = Active;
        Unregister();
        Binding = candidate;
        Id = nextId;
        Active = wasActive;
        if (!wasActive) Native.UnregisterHotKey(window, nextId);
        return null;
    }

    public void Dispose() => Unregister();
}
