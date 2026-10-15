using System.Text;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MailArchive;

public sealed class MailPreview : IDisposable
{
    private readonly WebView2 view;
    private readonly SemaphoreSlim initialization = new(1, 1);
    private bool initialized;
    private string? currentUrl;
    private byte[] currentHtml = [];

    public MailPreview(WebView2 view) => this.view = view;

    public async Task ShowAsync(string html)
    {
        await initialization.WaitAsync();
        try
        {
            if (!initialized)
            {
                var compatible = DisplaySettings.Current.CompatibleRendering;
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MailArchive", compatible ? "WebView2-srgb-software" : "WebView2-srgb");
                var options = new CoreWebView2EnvironmentOptions("--force-color-profile=srgb" + (compatible ? " --disable-gpu" : ""));
                var environment = await CoreWebView2Environment.CreateAsync(null, directory, options);
                await view.EnsureCoreWebView2Async(environment);
                var core = view.CoreWebView2;
                core.Settings.IsScriptEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsWebMessageEnabled = false;
                core.Settings.IsBuiltInErrorPageEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, e) =>
                {
                    // Serve the one document from memory. Even CSS URLs, redirects, file:// and
                    // relative resources cannot leave the preview or fetch remote tracking content.
                    bool own = string.Equals(e.Request.Uri, currentUrl, StringComparison.Ordinal);
                    e.Response = core.Environment.CreateWebResourceResponse(
                        new MemoryStream(own ? currentHtml : []), own ? 200 : 403, own ? "OK" : "Blocked",
                        own ? "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: " + PreviewHtml.ContentSecurityPolicy : "Content-Type: text/plain");
                };
                core.NavigationStarting += (_, e) =>
                {
                    if (!string.Equals(e.Uri, currentUrl, StringComparison.Ordinal)) e.Cancel = true;
                };
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.DownloadStarting += (_, e) => e.Cancel = true;
                core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
                initialized = true;
            }
            currentHtml = Encoding.UTF8.GetBytes(html);
            currentUrl = "https://mailarchive.invalid/message/" + Guid.NewGuid().ToString("N");
            view.CoreWebView2.Navigate(currentUrl);
        }
        finally { initialization.Release(); }
    }

    public void Clear()
    {
        currentUrl = null; currentHtml = [];
        if (initialized)
        {
            view.CoreWebView2.Stop();
            currentUrl = "about:blank";
            view.CoreWebView2.NavigateToString("<html><body></body></html>");
        }
    }

    public void Dispose() { view.Dispose(); initialization.Dispose(); }
}
