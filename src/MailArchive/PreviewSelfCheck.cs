using System.Text.Json;
using Microsoft.Web.WebView2.Wpf;

namespace MailArchive;

public static class PreviewSelfCheck
{
    public static async Task VerifyAsync(WebView2 view)
    {
        using var preview = new MailPreview(view);
        var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.NavigationCompleted += (_, e) =>
        {
            if (view.CoreWebView2?.Source.StartsWith("https://mailarchive.invalid/message/", StringComparison.Ordinal) != true) return;
            if (e.IsSuccess) navigated.TrySetResult();
            else navigated.TrySetException(new IOException("Preview navigation failed: " + e.WebErrorStatus));
        };
        await preview.ShowAsync(PreviewHtml.Create("<table><tr><td>Тест письма</td></tr></table><script>document.body.innerText='unsafe'</script>"));
        await navigated.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var cell = JsonSerializer.Deserialize<string>(await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('table td').textContent"));
        if (cell != "Тест письма" || view.CoreWebView2.Settings.IsScriptEnabled)
            throw new InvalidDataException("HTML preview failed content/script checks.");
        if (await view.CoreWebView2.ExecuteScriptAsync("document.scripts.length") != "0")
            throw new InvalidDataException("Mail preview retained script elements.");
    }
}
