using System.Diagnostics;

namespace KlasorGecis;

internal sealed record NavigationResult(bool Success, string Code, string Message,
    string? ActualFolder = null, bool? FilenamePreserved = null, bool? FilterPreserved = null,
    InteractionRestore Interaction = InteractionRestore.Unavailable);

internal static class DialogNavigator
{
    internal static async Task<NavigationResult> NavigateAsync(DialogTarget target, string folder, CancellationToken token)
    {
        if (token.IsCancellationRequested) return Fail("cancelled", Localizer.T("Geçiş iptal edildi."));
        if (!CoreRules.TryNormalizePath(folder, out var path))
            return Fail("invalid-path", Localizer.T("Bu konum bir dosya sistemi klasörü değil."));
        if (!target.IsCurrent()) return Fail("target-changed", Localizer.T("Dosya penceresi artık etkin değil."));
        if (string.Equals(target.CurrentFolder(), path, StringComparison.OrdinalIgnoreCase))
            return new(true, "already-there", Localizer.T("Zaten bu klasördesiniz."), path);
        string? filename = null;
        if (target.FilenameEdit != 0)
        {
            if (!Native.IsChild(target.Handle, target.FilenameEdit) || !Native.TryReadText(target.FilenameEdit, out filename))
                return Fail("filename-unreadable", Localizer.T("Dosya adı doğrulanamadı; geçiş yapılmadı."));
        }
        int? filter = target.FilterIndex();
        var interaction = DialogInteraction.TryCapture(target, filename);
        try
        {
            // Ctrl+G is received on key-down. Wait for the person's modifiers to be released.
            if (!await WaitUntil(() => Native.ModifiersReleased(), target, 1200, token))
                return Fail("modifier-held", Localizer.T("Kısayol tuşlarını bırakıp tekrar deneyin."));
            token.ThrowIfCancellationRequested();
            if (!target.IsCurrent()) return Fail("target-changed", Localizer.T("Dosya penceresi artık etkin değil."));
            using var input = new InputMonitor();
            if (!input.Available) return Fail("input-monitor-unavailable", Localizer.T("Yeni kullanıcı hareketi izlenemedi; geçiş yapılmadı."));
            long inputEpoch = input.Epoch;
            if ((filename is not null && (!Native.TryReadText(target.FilenameEdit, out var currentName) || currentName != filename)) ||
                (filter is not null && target.FilterIndex() != filter) ||
                (interaction is not null && (Native.Focus(target.ThreadId) != interaction.OriginalFocus ||
                    !Native.TryReadSelection(interaction.Filename, out int first, out int last) || first != interaction.Start || last != interaction.End)))
                return Fail("interaction-changed", Localizer.T("Dosya penceresinde yeni bir etkileşim var; geçiş durduruldu."));
            // Ctrl+L targets the address field across localized dialogs. Alt+D can be the
            // Turkish "Dosya adı" mnemonic and must not be used as the address shortcut.
            if (!Native.KeysWithStamp(out uint addressStamp, (0x11, false), (0x4C, false), (0x4C, true), (0x11, true)))
                return FailAccess(target, "input-blocked", Localizer.T("Windows bu pencereye klavye erişimi vermedi."));
            nint edit = 0;
            if (!await WaitUntil(() => target.IsAddressEdit(edit = Native.Focus(target.ThreadId)) && input.Received(addressStamp), target, 650, token))
            {
                return FailAccess(target, "address-unavailable", Localizer.T("Bu pencerenin adres alanına erişilemedi."));
            }
            token.ThrowIfCancellationRequested();
            if (!input.Unchanged(inputEpoch)) return Fail("input-changed", Localizer.T("Yeni bir kullanıcı hareketi algılandı; geçiş durduruldu."));
            if (!target.IsCurrent() || Native.Focus(target.ThreadId) != edit || !target.IsAddressEdit(edit))
                return Fail("focus-changed", Localizer.T("Odak değişti; geçiş durduruldu."));
            if (!Native.SetText(edit, path))
                return FailAccess(target, "address-write-blocked", Localizer.T("Klasör yolu adres alanına yazılamadı."));
            if (!Native.TryReadText(edit, out var addressValue) ||
                !string.Equals(addressValue, path, StringComparison.OrdinalIgnoreCase))
                return Fail("address-mismatch", Localizer.T("Adres alanındaki yol doğrulanamadı."));
            token.ThrowIfCancellationRequested();
            if (!target.IsCurrent() || Native.Focus(target.ThreadId) != edit || !target.IsAddressEdit(edit) ||
                !Native.ModifiersReleased()) return Fail("focus-changed", Localizer.T("Odak değişti; geçiş durduruldu."));
            // Enter is only sent when the address control is provably focused. Never invoke IDOK.
            if (!input.Unchanged(inputEpoch))
                return Fail("input-changed", Localizer.T("Yeni bir kullanıcı hareketi algılandı; geçiş durduruldu."));
            if (!Native.KeysWithStamp(out uint enterStamp, (0x0D, false), (0x0D, true)))
                return Fail("enter-blocked", Localizer.T("Windows klasör geçişine izin vermedi."));
            string? actual = null;
            var reached = await WaitUntil(() =>
            {
                actual = target.CurrentFolder();
                return string.Equals(actual, path, StringComparison.OrdinalIgnoreCase);
            }, target, 2200, token);
            if (!reached) return Fail("navigation-unverified", Localizer.T("Klasör geçişi doğrulanamadı. Konumu kontrol edin.")) with { ActualFolder = actual };
            bool filenameSame = filename is null || (Native.IsChild(target.Handle, target.FilenameEdit) &&
                Native.TryReadText(target.FilenameEdit, out var now) && string.Equals(now, filename, StringComparison.Ordinal));
            bool filterSame = filter is null || target.FilterIndex() == filter;
            if (!filenameSame || !filterSame)
                return new(false, "dialog-state-changed", Localizer.T("Dosya adı veya dosya türü değişti; kontrol edin."), actual,
                    filename is null ? null : filenameSame, filter is null ? null : filterSame);
            var restored = DialogInteraction.TryRestore(target, interaction, edit, input, inputEpoch, enterStamp, token);
            return new(true, "ok", restored == InteractionRestore.Skipped
                ? Localizer.T("Klasöre geçildi; yeni etkileşime müdahale edilmedi.") : Localizer.T("Klasöre geçildi."),
                actual, filename is null ? null : true, filter is null ? null : true, restored);
        }
        catch (OperationCanceledException)
        {
            return Fail("cancelled", Localizer.T("Geçiş iptal edildi."));
        }
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, DialogTarget target, int limitMs, CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < limitMs)
        {
            token.ThrowIfCancellationRequested();
            if (!target.IsCurrent()) return false;
            if (condition()) return true;
            await Task.Delay(20, token);
        }
        return false;
    }

    private static NavigationResult FailAccess(DialogTarget target, string code, string message)
    {
        if (!StartupSettings.IsAdministrator() &&
            PrivilegePolicy.TryIsElevated(target.ProcessId, out var elevated) && elevated)
            return Fail(code, Localizer.T("Yönetici penceresinde Windows güvenlik kısıtlaması olabilir; geçiş tamamlanmadı. Yönetici yetkisi istenmedi."));
        return Fail(code, message);
    }

    private static NavigationResult Fail(string code, string message) => new(false, code, message);
}
