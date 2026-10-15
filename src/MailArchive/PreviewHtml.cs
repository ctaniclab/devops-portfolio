using AngleSharp.Html.Parser;
using AngleSharp.Dom;

namespace MailArchive;

public static class PreviewHtml
{
    private const int MaxImageBytes = 8 * 1024 * 1024;
    public const string ContentSecurityPolicy = "default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src 'none'; media-src 'none'; object-src 'none'; frame-src 'none'; connect-src 'none'; base-uri 'none'; form-action 'none'";

    public static string Create(string html, Func<string, byte[]?>? loadCid = null)
    {
        var document = new HtmlParser().ParseDocument(html);
        foreach (var element in document.QuerySelectorAll("script,iframe,frame,frameset,object,embed,applet,link,meta,base,form,input,button,textarea,select,video,audio,source,track,svg,math").ToArray())
            element.Remove();
        foreach (var element in document.All.ToArray())
        {
            foreach (var attribute in element.Attributes.ToArray())
            {
                if (attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                    || attribute.Name is "srcset" or "ping" or "action" or "formaction" or "background")
                    element.RemoveAttribute(attribute.Name);
            }
            if (element.LocalName != "img") continue;
            var src = element.GetAttribute("src") ?? "";
            string? safe = null;
            if (src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase) && loadCid is not null)
            {
                var bytes = loadCid(Uri.UnescapeDataString(src[4..]).Trim('<', '>'));
                if (bytes is not null) safe = ImageDataUri(bytes);
            }
            else if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && src.Length <= MaxImageBytes * 4 / 3 + 256)
            {
                int comma = src.IndexOf(',');
                if (comma >= 0 && src[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                {
                    try { safe = ImageDataUri(Convert.FromBase64String(src[(comma + 1)..])); }
                    catch (FormatException) { }
                }
            }
            if (safe is not null) element.SetAttribute("src", safe);
            else
            {
                element.RemoveAttribute("src");
                if (string.IsNullOrWhiteSpace(element.GetAttribute("alt"))) element.SetAttribute("alt", "[Изображение недоступно или внешняя загрузка отключена]");
            }
        }
        var head = document.Head!;
        var charset = document.CreateElement("meta"); charset.SetAttribute("charset", "utf-8"); head.Prepend(charset);
        var csp = document.CreateElement("meta"); csp.SetAttribute("http-equiv", "Content-Security-Policy");
        csp.SetAttribute("content", ContentSecurityPolicy); head.Prepend(csp);
        var style = document.CreateElement("style");
        style.TextContent = "html{color-scheme:light}body{margin:18px;font-family:Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.45;overflow-wrap:anywhere}img{max-width:100%;height:auto}pre{white-space:pre-wrap}table{max-width:100%}";
        head.AppendChild(style);
        return "<!doctype html>\n" + document.DocumentElement!.OuterHtml;
    }

    public static string? ImageDataUri(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxImageBytes) return null;
        string? mime = bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? "image/png"
            : bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 }) ? "image/jpeg"
            : bytes.AsSpan().StartsWith("GIF8"u8) ? "image/gif"
            : bytes.AsSpan().StartsWith("BM"u8) ? "image/bmp"
            : bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8) ? "image/webp" : null;
        return mime is null ? null : "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
    }
}
