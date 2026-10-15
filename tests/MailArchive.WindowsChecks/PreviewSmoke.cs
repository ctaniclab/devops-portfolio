using System.Text.Json;
using System.Windows;
using MailArchive;
using Microsoft.Web.WebView2.Wpf;

internal static class PreviewSmoke
{
    public static async Task RunAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var view = new WebView2();
            var window = new Window { Width = 640, Height = 400, Content = view, Title = "MailArchive preview check" };
            var preview = new MailPreview(view);
            window.Loaded += async (_, _) =>
            {
                try
                {
                    var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    view.NavigationCompleted += (_, e) =>
                    {
                        if (view.CoreWebView2?.Source.StartsWith("https://mailarchive.invalid/message/", StringComparison.Ordinal) != true) return;
                        if (e.IsSuccess) navigated.TrySetResult();
                        else navigated.TrySetException(new Exception("Preview navigation failed: " + e.WebErrorStatus));
                    };
                    await preview.ShowAsync(PreviewHtml.Create("<table><tr><td>Тест письма</td></tr></table><script>document.body.innerText='unsafe'</script>"));
                    await navigated.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    var cell = JsonSerializer.Deserialize<string>(await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('table td').textContent"));
                    if (cell != "Тест письма" || view.CoreWebView2.Settings.IsScriptEnabled)
                        throw new Exception("HTML preview failed its content/script policy check.");
                    if (await view.CoreWebView2.ExecuteScriptAsync("document.scripts.length") != "0")
                        throw new Exception("Mail preview retained script elements.");
                    Console.WriteLine("PASS: Windows WebView2 renders the email table and Cyrillic with page scripts disabled.");
                    done.TrySetResult();
                }
                catch (Exception error) { done.TrySetException(error); }
                finally { preview.Dispose(); window.Close(); app.Shutdown(); }
            };
            try { app.Run(window); }
            catch (Exception error) { done.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
