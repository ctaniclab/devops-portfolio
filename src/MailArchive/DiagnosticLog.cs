namespace MailArchive;

internal static class DiagnosticLog
{
    public static string? Write(Exception error, string context)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MailArchive", "Logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "error-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
            File.WriteAllText(path, $"Mail Archive 1.1.0\nUTC: {DateTime.UtcNow:O}\n{context}\n\n{error}\n", System.Text.Encoding.UTF8);
            return path;
        }
        catch { return null; } // Do not mask the original error if logging is unavailable.
    }
}
