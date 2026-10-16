namespace MailArchive;

public sealed record ConversionEstimateValue(double? Percent, TimeSpan Elapsed, TimeSpan? Remaining, bool Stalled, bool Finalizing)
{
    public string Label => (Percent is double p ? $"{p:0} % · " : "") + (Finalizing ? L.T("Проверка и закрытие файла…")
        : Stalled ? L.T("Оценка времени уточняется…") : Remaining is TimeSpan left ? L.T("Осталось ≈ ") + Duration(left) : L.T("Осталось: расчёт…"))
        + L.T(" · Прошло ") + Duration(Elapsed);
    public static string Duration(TimeSpan value)
    {
        value = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        if (value.TotalHours >= 1) return L.F($"{(int)value.TotalHours} ч {value.Minutes:00} мин");
        if (value.TotalMinutes >= 1) return L.F($"{(int)value.TotalMinutes} мин {value.Seconds:00} с");
        return value.TotalSeconds < 1 ? L.T("< 1 с") : L.F($"{(int)value.TotalSeconds} с");
    }
}

// Estimate speed after the first completed message, excluding Outlook startup.
// Update with a monotonic Stopwatch; UI timer ticks do not fabricate completed work.
public sealed class ConversionEstimate
{
    private int lastCount;
    private TimeSpan lastSample;
    private double? secondsPerMessage;
    public ConversionEstimateValue Observe(int examined, int? total, TimeSpan elapsed, bool finalizing = false)
    {
        examined = Math.Max(0, examined);
        if (examined > lastCount)
        {
            if (lastCount > 0 && elapsed > lastSample)
            {
                double sample = (elapsed - lastSample).TotalSeconds / (examined - lastCount);
                secondsPerMessage = secondsPerMessage is double old ? .65 * old + .35 * sample : sample;
            }
            lastSample = elapsed; lastCount = examined;
        }
        bool stalled = secondsPerMessage is double rate && (elapsed - lastSample).TotalSeconds > Math.Max(8, rate * 4);
        double? percent = total is > 0 ? Math.Clamp(100d * examined / total.Value, 0, 99) : null;
        TimeSpan? remaining = !finalizing && !stalled && total is > 0 && secondsPerMessage is double speed
            ? TimeSpan.FromSeconds(Math.Max(1, speed * Math.Max(0, total.Value - examined) - (elapsed - lastSample).TotalSeconds)) : null;
        if (total is > 0 && examined >= total && !finalizing) remaining = TimeSpan.Zero;
        return new(percent, elapsed, remaining, stalled, finalizing);
    }
}
