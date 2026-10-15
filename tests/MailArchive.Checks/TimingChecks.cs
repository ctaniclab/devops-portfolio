using MailArchive;

internal static class TimingChecks
{
    public static void Run(Action<bool, string> check)
    {
        var estimate = new ConversionEstimate();
        var initial = estimate.Observe(0, 10, TimeSpan.FromSeconds(15));
        check(initial.Percent == 0 && initial.Remaining is null && initial.Label.Contains("расчёт"), "No fake ETA is shown while Outlook starts");
        var first = estimate.Observe(1, 10, TimeSpan.FromSeconds(20));
        check(first.Remaining is null, "First message anchors timing without treating Outlook startup as message speed");
        var second = estimate.Observe(2, 10, TimeSpan.FromSeconds(22));
        check(second.Percent == 20 && second.Remaining == TimeSpan.FromSeconds(16), "Two-second message rate estimates 16 seconds for eight remaining messages");
        var tick = estimate.Observe(2, 10, TimeSpan.FromSeconds(23));
        check(tick.Percent == 20 && tick.Remaining == TimeSpan.FromSeconds(15), "Timer ticks update elapsed/remaining time without advancing the percentage");
        var stalled = estimate.Observe(2, 10, TimeSpan.FromSeconds(45));
        check(stalled.Stalled && stalled.Remaining is null && stalled.Label.Contains("уточняется"), "A delayed message invalidates a stale countdown rather than showing zero seconds forever");
        var resumed = estimate.Observe(3, 10, TimeSpan.FromSeconds(46));
        check(!resumed.Stalled && resumed.Remaining is { TotalSeconds: > 16 }, "Timing resumes and adapts to slower conversion");
        var final = estimate.Observe(10, 10, TimeSpan.FromSeconds(60), true);
        check(final.Percent == 99 && final.Remaining is null && final.Label.Contains("Проверка"), "Final PST verification stays at 99 percent without promising completion before the file closes");
        var unknown = new ConversionEstimate().Observe(5, null, TimeSpan.FromSeconds(12));
        check(unknown.Percent is null && unknown.Remaining is null && !unknown.Label.Contains('%'), "Unknown message totals use indeterminate progress instead of a fabricated percentage");
        check(ConversionEstimateValue.Duration(TimeSpan.FromSeconds(125)) == "2 мин 05 с"
            && ConversionEstimateValue.Duration(TimeSpan.FromHours(2.5)) == "2 ч 30 мин", "ETA durations handle seconds, minutes and long conversions");
    }
}
