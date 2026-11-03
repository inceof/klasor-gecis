namespace KlasorGecis;

internal sealed record ProfileSelection(string Directory, bool Explicit, bool ReplaceInstance);
internal static class ProfilePaths
{
    private static string? selectedDirectory;
    internal static string DirectoryPath => selectedDirectory ?? DefaultDirectory;
    internal static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KlasorGecis");
    internal static bool IsExplicit => selectedDirectory is not null;

    internal static bool TryParse(string[] args, out ProfileSelection selection, out string? error)
    {
        string? directory = null; bool replace = false;
        error = null;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--replace-instance" && !replace) { replace = true; continue; }
            if (args[index] == "--profile-dir" && directory is null && index + 1 < args.Length &&
                Path.IsPathFullyQualified(args[index + 1]) && CoreRules.TryNormalizePath(args[index + 1], out var normalized))
            { directory = normalized; index++; continue; }
            error = Localizer.T("Geçersiz seçenek. Profil için --profile-dir ve tam bir klasör yolu kullanın.");
            selection = new(DefaultDirectory, false, false); return false;
        }
        selection = new(directory ?? DefaultDirectory, directory is not null, replace);
        return true;
    }

    internal static bool Select(ProfileSelection selection, out string? error)
    {
        error = null;
        if (!selection.Explicit) return true;
        string probe = Path.Combine(selection.Directory, ".write-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(selection.Directory);
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            File.Delete(probe);
            selectedDirectory = selection.Directory;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = Localizer.T("Seçilen profile yazılamıyor; başka bir profile geçilmedi.");
            return false;
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
