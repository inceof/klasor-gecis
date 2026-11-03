using KlasorGecis;

internal static class RefreshPolicyTests
{
    internal static void Run(Action<string, Func<bool>> check)
    {
        check("inactive refresh backs off while active refresh remains 650 ms", () =>
        {
            var policy = new RefreshPolicy();
            if (!policy.ShouldRefresh(0, false, false)) return false;
            policy.Started(0); policy.Completed(false, true);
            if (policy.ShouldRefresh(4999, false, false) || !policy.ShouldRefresh(5000, false, false)) return false;
            policy.Started(5000); policy.Completed(false, true);
            return !policy.ShouldRefresh(14999, false, false) && policy.ShouldRefresh(15000, false, false) &&
                policy.ShouldRefresh(5650, true, false) && !policy.ShouldRefresh(5649, true, false);
        });
        check("foreground events coalesce and never overlap a scan", () =>
        {
            var policy = new RefreshPolicy(); policy.Started(0); policy.Completed(false, true);
            policy.Invalidate(); policy.Invalidate();
            if (!policy.ShouldRefresh(1, false, false)) return false;
            policy.Started(1);
            if (policy.ShouldRefresh(1, false, false)) return false;
            policy.Invalidate();
            if (policy.ShouldRefresh(2, false, true)) return false;
            policy.Completed(false, true);
            if (!policy.ShouldRefresh(2, false, false)) return false;
            policy.Started(2); policy.Completed(false, true);
            return !policy.ShouldRefresh(3, false, false);
        });
        check("source errors have bounded backoff and foreground interrupts the wait", () =>
        {
            var policy = new RefreshPolicy(); long time = 0;
            for (int i = 0; i < 12; i++)
            {
                policy.Started(time); policy.Completed(true, false); time += 10000;
            }
            long last = time - 10000;
            if (policy.ShouldRefresh(last + 9999, true, false) || !policy.ShouldRefresh(last + 10000, true, false)) return false;
            policy.Invalidate();
            if (!policy.ShouldRefresh(last + 1, true, false)) return false;
            policy.Started(last + 1); policy.Completed(true, true);
            return !policy.ShouldRefresh(last + 650, true, false) && policy.ShouldRefresh(last + 651, true, false);
        });
    }
}
