using System.Security.Cryptography;
using System.Text.Json;

namespace KlasorGecis;

internal sealed record FavoriteFolder(string Path, string DisplayName);
internal sealed record RecentFolder(string Path, string? AppIdentity, DateTimeOffset LastUsedUtc);
internal sealed record FolderPreferences
{
    public int Version { get; init; } = 1;
    public string Language { get; init; } = "tr";
    public bool AutoSwitchEnabled { get; init; }
    public bool HistoryEnabled { get; init; }
    public FavoriteFolder[] Favorites { get; init; } = [];
    public RecentFolder[] Recent { get; init; } = [];
}

internal sealed class PreferencesStore
{
    private readonly object gate = new();
    private readonly string path;
    private readonly Action? beforeCommit;
    private FolderPreferences state = new();
    private byte[]? fingerprint;
    private readonly bool readOnly;
    private long epoch;
    internal string? Error { get; private set; }
    internal long HistoryEpoch { get { lock (gate) return epoch; } }
    internal FolderPreferences Current { get { lock (gate) return state with { Favorites = state.Favorites.ToArray(), Recent = state.Recent.ToArray() }; } }

    internal PreferencesStore(string path, Action? beforeCommit = null)
    {
        this.path = Path.GetFullPath(path);
        this.beforeCommit = beforeCommit;
        try
        {
            byte[] bytes = File.ReadAllBytes(this.path);
            if (bytes.Length > 1024 * 1024) throw new JsonException();
            var loaded = JsonSerializer.Deserialize<FolderPreferences>(bytes) ?? throw new JsonException();
            if (!Valid(loaded)) throw new JsonException();
            state = loaded; fingerprint = SHA256.HashData(bytes);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            readOnly = true;
            Error = Localizer.T("Klasör ayarları okunamadı veya sürümü desteklenmiyor; mevcut dosya korunuyor.");
        }
    }

    internal bool SetAutoSwitch(bool enabled, out string? error) => Update(s => s with { AutoSwitchEnabled = enabled }, false, out error);
    internal bool SetLanguage(string code, out string? error)
    {
        if (!Localizer.IsSupported(code)) { error = Localizer.T("Seçilen dil desteklenmiyor."); return false; }
        return Update(s => s with { Language = code }, false, out error);
    }
    internal bool SetHistory(bool enabled, out string? error) => Update(s => s with { HistoryEnabled = enabled }, true, out error);
    internal bool ClearHistory(out string? error) => Update(s => s with { Recent = [] }, true, out error);
    internal bool AddFavorite(string rawPath, string displayName, out string? error)
    {
        if (!CoreRules.TryNormalizePath(rawPath, out var normalized)) { error = Localizer.T("Bu konum bir klasör yolu değil."); return false; }
        string label = string.IsNullOrWhiteSpace(displayName) ? normalized : displayName;
        return Update(s => s with { Favorites = s.Favorites.Any(f => SamePath(f.Path, normalized))
            ? s.Favorites : [.. s.Favorites, new FavoriteFolder(normalized, label)] }, false, out error);
    }
    internal bool RemoveFavorite(string normalizedPath, out string? error) => Update(s => s with
    { Favorites = s.Favorites.Where(f => !SamePath(f.Path, normalizedPath)).ToArray() }, false, out error);

    internal bool RecordSuccess(string path, string? appIdentity, long expectedEpoch, out string? error)
    {
        lock (gate)
        {
            error = null;
            if (!state.HistoryEnabled || expectedEpoch != epoch) return true;
            if (!CoreRules.TryNormalizePath(path, out var normalized)) return false;
            return Update(s => s with { Recent = new[] { new RecentFolder(normalized, appIdentity, DateTimeOffset.UtcNow) }
                .Concat(s.Recent.Where(r => !SamePath(r.Path, normalized) || !string.Equals(r.AppIdentity, appIdentity, StringComparison.OrdinalIgnoreCase)))
                .Take(20).ToArray() }, false, out error);
        }
    }

    internal IReadOnlyList<RecentFolder> RecentFor(string? appIdentity)
    {
        lock (gate)
            return !state.HistoryEnabled ? [] : state.Recent
                .OrderByDescending(r => appIdentity is not null && string.Equals(r.AppIdentity, appIdentity, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(r => r.LastUsedUtc).DistinctBy(r => Normalize(r.Path), StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private bool Update(Func<FolderPreferences, FolderPreferences> change, bool invalidateHistory, out string? error)
    {
        lock (gate)
        {
            error = Error;
            if (readOnly) return false;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[]? currentFingerprint;
                try { currentFingerprint = SHA256.HashData(File.ReadAllBytes(path)); }
                catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { currentFingerprint = null; }
                if ((fingerprint is null) != (currentFingerprint is null) ||
                    (fingerprint is not null && !fingerprint.AsSpan().SequenceEqual(currentFingerprint)))
                    throw new IOException("External settings change");
                var next = change(state with { Favorites = state.Favorites.ToArray(), Recent = state.Recent.ToArray() });
                if (!Valid(next)) throw new JsonException();
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(next);
                if (bytes.Length > 1024 * 1024) throw new IOException("Settings limit");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes); stream.Flush(true); }
                beforeCommit?.Invoke();
                if (!Commit(temporary, currentFingerprint, SHA256.HashData(bytes)))
                {
                    Error = error = Localizer.T("Klasör ayarları kayıt sırasında dışarıdan değişti; dışarıdan yazılan sürüm geri yüklendi. Yeniden başlatıp kontrol edin. Çakışma kopyaları varsa aynı profil dizininde korunur.");
                    return false;
                }
                state = next; fingerprint = SHA256.HashData(bytes);
                if (invalidateHistory) epoch++;
                Error = error = null;
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                Error = error = Localizer.T("Klasör ayarları kaydedilemedi veya dosya dışarıdan değişti; önceki durum korunuyor.");
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private bool Commit(string temporary, byte[]? expected, byte[] written)
    {
        if (expected is null) { File.Move(temporary, path, false); return true; }
        // Keep the exact file displaced by the atomic operation. A preflight hash
        // alone cannot protect a noncooperating external writer in the commit gap.
        string previous = path + ".conflict-" + Guid.NewGuid().ToString("N") + ".json";
        File.Replace(temporary, path, previous, true);
        bool unchanged = false;
        try { unchanged = expected.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(previous))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (unchanged) { TryDeleteOwnCopy(previous); return true; }
        string displaced = path + ".conflict-" + Guid.NewGuid().ToString("N") + ".json";
        // A second concurrent edit is retained in displaced, never discarded.
        // This is recovery with preserved versions, not a cross-process CAS.
        File.Replace(previous, path, displaced, true);
        try
        {
            if (written.AsSpan().SequenceEqual(SHA256.HashData(File.ReadAllBytes(displaced)))) TryDeleteOwnCopy(displaced);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return false;
    }

    private static void TryDeleteOwnCopy(string file)
    {
        try { File.Delete(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static bool Valid(FolderPreferences value) => value.Version == 1 && Localizer.IsSupported(value.Language) && value.Favorites is not null && value.Recent is not null &&
        value.Recent.Length <= 20 && value.Favorites.All(f => f is not null && CoreRules.TryNormalizePath(f.Path, out _) &&
            !string.IsNullOrWhiteSpace(f.DisplayName) && !f.DisplayName.Any(char.IsControl)) &&
        value.Recent.All(r => r is not null && CoreRules.TryNormalizePath(r.Path, out _) &&
            (r.AppIdentity is null || CoreRules.TryNormalizePath(r.AppIdentity, out _)));
    private static string Normalize(string path) => CoreRules.TryNormalizePath(path, out var normalized) ? normalized : path;
    private static bool SamePath(string first, string second) => string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);
}
