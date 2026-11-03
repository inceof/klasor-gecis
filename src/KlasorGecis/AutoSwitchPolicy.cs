namespace KlasorGecis;

internal readonly record struct WindowIdentity(nint Handle, uint ProcessId, uint ThreadId, long Created, nint AddressRoot = 0)
{
    internal static WindowIdentity? Read(nint window)
    {
        uint thread = Native.GetWindowThreadProcessId(window, out uint process);
        long created = Native.ProcessCreation(process);
        return thread == 0 || created == 0 ? null : new(window, process, thread, created);
    }
    internal bool IsLive => Native.IsWindow(Handle) && Read(Handle) == this;
    internal bool Matches(DialogTarget target) => Handle == target.Handle && ProcessId == target.ProcessId &&
        ThreadId == target.ThreadId && Created == target.ProcessCreated && (AddressRoot == 0 || AddressRoot == target.AddressRoot);
    internal bool SameWindow(WindowIdentity other) => Handle == other.Handle && ProcessId == other.ProcessId && ThreadId == other.ThreadId && Created == other.Created;
    internal static WindowIdentity ForDialog(DialogTarget target) => new(target.Handle, target.ProcessId, target.ThreadId, target.ProcessCreated, target.AddressRoot);
}
internal enum ForegroundKind { Other, Explorer, Dialog }
internal readonly record struct ForegroundObservation(WindowIdentity Window, ForegroundKind Kind, bool EligibleDialog = false);
internal sealed record AutoSwitchIntent(WindowIdentity Target, WindowIdentity Source, long Generation);

internal sealed class AutoSwitchPolicy
{
    private WindowIdentity? lastDialog, armedDialog, visitedSource;
    private long started, generation;
    internal void Reset() { lastDialog = armedDialog = visitedSource = null; generation++; }
    internal void Destroyed(nint handle)
    {
        if (lastDialog?.Handle == handle || armedDialog?.Handle == handle || visitedSource?.Handle == handle) Reset();
    }

    internal AutoSwitchIntent? Observe(ForegroundObservation observation, long elapsedMilliseconds, bool enabled)
    {
        if (!enabled) { Reset(); return null; }
        if (armedDialog is not null && (elapsedMilliseconds < started || elapsedMilliseconds - started > 30000)) Reset();
        switch (observation.Kind)
        {
            case ForegroundKind.Other: Reset(); return null;
            case ForegroundKind.Dialog:
                AutoSwitchIntent? intent = armedDialog is { } armed && armed.SameWindow(observation.Window) &&
                    (observation.Window.AddressRoot == 0 || armed.AddressRoot == observation.Window.AddressRoot) && visitedSource is { } source
                    ? new(armed, source, generation) : null;
                armedDialog = visitedSource = null;
                // Deactivation can remove child focus just before EVENT_SYSTEM_FOREGROUND.
                // Keep a known candidate for the same dialog; return-time eligibility is rechecked.
                lastDialog = observation.EligibleDialog ? observation.Window :
                    lastDialog is { } known && known.SameWindow(observation.Window) ? known : null;
                return intent;
            case ForegroundKind.Explorer:
                if (lastDialog is { } dialog)
                {
                    armedDialog = dialog; lastDialog = null; started = elapsedMilliseconds; generation++;
                }
                if (armedDialog is not null) visitedSource = observation.Window;
                return null;
            default: Reset(); return null;
        }
    }
}
