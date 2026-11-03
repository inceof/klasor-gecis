namespace KlasorGecis;

internal enum InteractionRestore { Unavailable, Restored, Skipped }
internal sealed record DialogInteractionSnapshot(nint Filename, string FilenameValue,
    int Start, int End, nint OriginalFocus);

internal static class DialogInteraction
{
    internal static DialogInteractionSnapshot? TryCapture(DialogTarget target, string? filename)
    {
        if (filename is null || target.FilenameEdit == 0 || !target.IsCurrent() ||
            !Native.IsChild(target.Handle, target.FilenameEdit) || Native.Class(target.FilenameEdit) != "Edit" ||
            !Native.TryReadSelection(target.FilenameEdit, out int start, out int end)) return null;
        nint focus = Native.Focus(target.ThreadId);
        // Only the recognized filename Edit is restored. Unknown controls stay untouched.
        return focus == target.FilenameEdit ? new(target.FilenameEdit, filename, start, end, focus) : null;
    }

    internal static InteractionRestore TryRestore(DialogTarget target, DialogInteractionSnapshot? snapshot,
        nint addressEdit, InputMonitor input, long inputEpoch, uint inputStamp, CancellationToken token)
    {
        if (snapshot is null) return InteractionRestore.Unavailable;
        bool Guard() => !token.IsCancellationRequested && target.IsCurrent() &&
            input.Unchanged(inputEpoch) && input.Received(inputStamp) && Native.IsChild(target.Handle, snapshot.Filename) &&
            snapshot.Filename == target.FilenameEdit && Native.Class(snapshot.Filename) == "Edit" &&
            Native.IsWindowVisible(snapshot.Filename) && Native.IsWindowEnabled(snapshot.Filename) &&
            Native.TryReadText(snapshot.Filename, out string now) && now == snapshot.FilenameValue;
        if (!Guard()) return InteractionRestore.Skipped;
        nint focus = Native.Focus(target.ThreadId);
        if (focus == snapshot.Filename)
        {
            // A changed selection in an already focused filename may belong to the person.
            return Native.TryReadSelection(focus, out int start, out int end) &&
                start == snapshot.Start && end == snapshot.End ? InteractionRestore.Restored : InteractionRestore.Skipped;
        }
        // Do not override another Edit/combo/button selected by the person or another tool.
        if (focus != addressEdit && (focus == 0 || !Native.IsChild(target.Handle, focus) ||
            Native.Class(focus) is "Edit" or "ComboBox" or "Button")) return InteractionRestore.Skipped;
        if (!Guard()) return InteractionRestore.Skipped;
        // One bounded synchronous dialog operation; no queued focus message can wake later.
        // Our code has no other focus-setting operation in flight. Never force foreground.
        if (Native.SendMessageTimeout(target.Handle, 0x28, snapshot.Filename, 1, 0x22, 150, out _) == 0 ||
            Native.Focus(target.ThreadId) != snapshot.Filename || !Guard()) return InteractionRestore.Skipped;
        if (Native.SendMessageTimeout(snapshot.Filename, 0xB1, snapshot.Start, snapshot.End, 0x22, 150, out _) == 0)
            return InteractionRestore.Skipped;
        return Guard() && Native.TryReadSelection(snapshot.Filename, out int restoredStart, out int restoredEnd) &&
            restoredStart == snapshot.Start && restoredEnd == snapshot.End ? InteractionRestore.Restored : InteractionRestore.Skipped;
    }
}
