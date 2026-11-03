using KlasorGecis;

internal static class AutoSwitchTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        var dialog = new WindowIdentity(11, 1, 10, 100);
        var otherDialog = new WindowIdentity(12, 1, 10, 101);
        var explorerA = new WindowIdentity(21, 2, 20, 200);
        var explorerB = new WindowIdentity(22, 2, 20, 200);
        ForegroundObservation D(WindowIdentity id) => new(id, ForegroundKind.Dialog, true);
        ForegroundObservation E(WindowIdentity id) => new(id, ForegroundKind.Explorer);
        check("automatic switch consumes exact dialog round trip once", () =>
        {
            var policy = new AutoSwitchPolicy(); policy.Observe(D(dialog), 0, true);
            policy.Observe(E(explorerA), 10, true); policy.Observe(E(explorerB), 20, true);
            var intent = policy.Observe(D(dialog), 30, true);
            return intent?.Target == dialog && intent.Source == explorerB && policy.Observe(D(dialog), 40, true) is null;
        });
        check("automatic switch never crosses an unrelated foreground window", () =>
        {
            var policy = new AutoSwitchPolicy(); policy.Observe(D(dialog), 0, true);
            policy.Observe(E(explorerA), 10, true);
            policy.Observe(new(explorerB, ForegroundKind.Other), 20, true);
            return policy.Observe(D(dialog), 30, true) is null;
        });
        check("automatic switch rejects another or reused dialog identity", () =>
        {
            var policy = new AutoSwitchPolicy(); policy.Observe(D(dialog), 0, true);
            policy.Observe(E(explorerA), 10, true);
            return policy.Observe(D(otherDialog), 20, true) is null;
        });
        check("automatic switch expires and disabled mode cannot replay an old visit", () =>
        {
            var policy = new AutoSwitchPolicy(); policy.Observe(D(dialog), 0, true);
            policy.Observe(E(explorerA), 10, true);
            if (policy.Observe(D(dialog), 30011, true) is not null) return false;
            policy.Observe(E(explorerA), 30020, true);
            policy.Observe(D(dialog), 30025, false);
            return policy.Observe(D(dialog), 30030, true) is null;
        });
        check("unknown dialog focus never arms automatic switching", () =>
        {
            var policy = new AutoSwitchPolicy(); policy.Observe(new(dialog, ForegroundKind.Dialog), 0, true);
            policy.Observe(E(explorerA), 10, true);
            return policy.Observe(D(dialog), 20, true) is null;
        });
        check("temporary focus loss before foreground event preserves known dialog candidate", () =>
        {
            var policy = new AutoSwitchPolicy(); policy.Observe(D(dialog), 0, true);
            policy.Observe(new(dialog, ForegroundKind.Dialog), 9, true);
            policy.Observe(E(explorerA), 10, true);
            return policy.Observe(new(dialog, ForegroundKind.Dialog), 20, true)?.Source == explorerA;
        });
        check("destroyed visited Explorer cannot be replayed under a reused handle", () =>
        {
            var policy = new AutoSwitchPolicy(); policy.Observe(D(dialog), 0, true); policy.Observe(E(explorerA), 10, true);
            policy.Destroyed(explorerA.Handle);
            return policy.Observe(D(dialog), 20, true) is null;
        });
    }
}
