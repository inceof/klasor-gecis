namespace KlasorGecis;

// Monotonic timing only. A foreground event can interrupt backoff, but never
// starts another Shell call while the existing worker is occupied.
internal sealed class RefreshPolicy
{
    private long? _lastStarted;
    private bool _invalidated = true;
    private int _idleSuccesses, _failures;

    internal void Invalidate() { _invalidated = true; _idleSuccesses = 0; }

    internal bool ShouldRefresh(long elapsedMs, bool active, bool inFlight)
    {
        if (inFlight) return false;
        if (_invalidated || _lastStarted is null) return true;
        int interval = _failures > 0 ? Math.Min(10000, 650 * (1 << Math.Min(_failures, 4))) :
            active ? 650 : _idleSuccesses < 2 ? 5000 : 10000;
        return elapsedMs - _lastStarted.Value >= interval;
    }

    internal void Started(long elapsedMs) { _lastStarted = elapsedMs; _invalidated = false; }

    internal void Completed(bool active, bool success)
    {
        if (!success) { _failures = Math.Min(_failures + 1, 4); return; }
        _failures = 0;
        if (active) _idleSuccesses = 0;
        else _idleSuccesses = Math.Min(2, _idleSuccesses + 1);
    }
}
