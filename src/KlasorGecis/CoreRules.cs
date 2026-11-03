using System.Drawing;

namespace KlasorGecis;

internal static class CoreRules
{
    public static bool TryNormalizePath(string? raw, out string path)
    {
        path = "";
        if (string.IsNullOrEmpty(raw) || raw.Any(char.IsControl)) return false;
        string candidate = raw;
        if (raw.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || !uri.IsFile ||
                uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
            candidate = uri.LocalPath;
        }
        if (candidate.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            candidate.StartsWith(@"\\?\", StringComparison.Ordinal)) return false;
        bool drive = candidate.Length >= 3 && char.IsAsciiLetter(candidate[0]) &&
                     candidate[1] == ':' && (candidate[2] == '\\' || candidate[2] == '/');
        bool unc = candidate.StartsWith(@"\\", StringComparison.Ordinal) &&
                   candidate[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Length >= 2;
        if (!drive && !unc) return false;
        if (candidate.Any(c => char.IsControl(c) || c is '*' or '?' or '"' or '<' or '>' or '|') ||
            candidate[(drive ? 2 : 0)..].Contains(':')) return false;
        try
        {
            var normalized = Path.GetFullPath(candidate);
            var root = Path.GetPathRoot(normalized)!;
            path = normalized.TrimEnd('\\', '/');
            if (path.Length < root.Length) path = root;
            return true;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        { return false; }
    }

    public static Rectangle PlaceBar(Rectangle dialog, Rectangle workingArea, int dpi)
    {
        if (workingArea.Width <= 0 || workingArea.Height <= 0) return Rectangle.Empty;
        float scale = Math.Clamp(dpi, 96, 768) / 96f;
        int margin = Math.Min((int)Math.Round(8 * scale), Math.Max(0, (workingArea.Width - 1) / 2));
        int width = Math.Min((int)Math.Round(500 * scale), workingArea.Width - margin * 2);
        int height = Math.Min((int)Math.Round(50 * scale), workingArea.Height);
        int gap = (int)Math.Round(4 * scale);
        int x = Math.Clamp(dialog.Left + (dialog.Width - width) / 2,
            workingArea.Left + margin, workingArea.Right - margin - width);
        int y = dialog.Bottom + gap;
        if (y + height > workingArea.Bottom)
            y = dialog.Top - gap - height;
        y = Math.Clamp(y, workingArea.Top, workingArea.Bottom - height);
        return new Rectangle(x, y, width, height);
    }
}
