using System.Globalization;
using System.Text.Json;

namespace KlasorGecis;

internal sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

internal static class Localizer
{
    internal static readonly LanguageOption[] Languages =
    [new("tr", "Türkçe"), new("en", "English"), new("zh-CN", "中文（简体）"),
     new("hi", "हिन्दी"), new("es", "Español"), new("ar", "العربية")];
    internal static IReadOnlyDictionary<string, Dictionary<string, string>> Catalog { get; } = Load();
    private static string current = "tr";
    internal static string Current => current;
    internal static bool IsRtl => current == "ar";
    internal static string FontFamily => current == "hi" ? "Nirmala UI" : "Segoe UI";
    internal static event Action? Changed;
    internal static bool IsSupported(string? code) => Languages.Any(l => l.Code == code);

    internal static void SetCurrent(string code)
    {
        if (!IsSupported(code)) throw new ArgumentOutOfRangeException(nameof(code));
        if (current == code) return;
        current = code;
        Changed?.Invoke();
    }

    internal static string T(string source) => T(source, current);
    internal static string T(string source, string language) => language == "tr" ? source :
        Catalog.TryGetValue(source, out var values) && values.TryGetValue(language, out var translated) ? translated : source;
    internal static string F(string source, params object?[] values) =>
        string.Format(CultureInfo.GetCultureInfo(current), T(source), values);
    internal static string? Message(string? message)
    {
        if (message is null) return null;
        if (Catalog.ContainsKey(message)) return T(message);
        // Only callers displaying known application errors use this conversion.
        // User folder names/paths never enter it; unknown OS errors stay intact.
        foreach (var entry in Catalog)
            if (entry.Value.Values.Contains(message, StringComparer.Ordinal)) return T(entry.Key);
        return message;
    }

    internal static void ApplyOrientation(Control control)
    {
        // Only UI direction/font changes; user text is never passed through T.
        control.RightToLeft = IsRtl ? RightToLeft.Yes : RightToLeft.No;
        if (control is Form form) form.RightToLeftLayout = IsRtl;
        control.Font = AppTheme.FontFor(control.Font.Size, control.Font.Style);
        foreach (Control child in control.Controls) ApplyOrientation(child);
        if (control.Name.StartsWith("Data_", StringComparison.Ordinal)) control.RightToLeft = RightToLeft.No;
    }

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        using var stream = typeof(Localizer).Assembly.GetManifestResourceStream("KlasorGecis.Translations.json")
            ?? throw new InvalidOperationException("Missing language catalog");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)
            ?? throw new InvalidOperationException("Invalid language catalog");
    }
}
