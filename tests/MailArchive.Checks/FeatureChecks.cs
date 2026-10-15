using System.Text;
using AngleSharp.Html.Parser;
using MailArchive;
using XstReader;

internal static class FeatureChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var exception in new Exception[] {
            new UnauthorizedAccessException("Property 0x007D001F does not support this operation."),
            new ArgumentException("Value does not fall within the expected range.") })
        {
            Exception? reported = null;
            bool result = MetadataCopy.TrySet("007D", () => throw exception, (_, e) => reported = e);
            check(!result && ReferenceEquals(reported, exception), "Mapped Outlook " + exception.GetType().Name + " is recorded as a metadata warning");
        }
        const string rtf = @"{\rtf1\ansi\ansicpg1251{\fonttbl{\f0 Arial;}}\f0\fs24 {\b Bold text}\par \'cf\'f0\'e8\'e2\'e5\'f2}";
        var bytes = Encoding.ASCII.GetBytes(rtf);
        using var packed = new MemoryStream();
        using (var writer = new BinaryWriter(packed, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write((uint)(bytes.Length + 12)); writer.Write((uint)bytes.Length);
            writer.Write(0x414C454Du); writer.Write(0u); writer.Write(bytes);
        }
        var body = PreparedBody.FromMessage(new Message { RtfCompressed = packed.ToArray() });
        var document = new HtmlParser().ParseDocument(body.Html);
        check(body.ConvertedFromRtf && document.Body!.TextContent.Contains("Привет") && document.Body.TextContent.Contains("Bold text"), "RTF-to-HTML preserves Cyrillic and paragraphs without Outlook RTFBody assignment");
        check(document.QuerySelector("b,strong") is not null || document.All.Any(e => (e.GetAttribute("style") ?? "").Contains("font-weight", StringComparison.OrdinalIgnoreCase)), "RTF bold formatting survives HTML preparation");
        int format = 0; string? writtenHtml = null;
        body.WriteTo(value => format = value, value =>
        {
            if (format != 2) throw new ArgumentException("HTML format not selected");
            writtenHtml = value;
        });
        check(writtenHtml == body.Html && format == 2, "Outlook HTML format is selected before writing converted RTF body");
        var htmlPreferred = PreparedBody.FromMessage(new Message { Html = Encoding.UTF8.GetBytes("<table><tr><td>HTML content</td></tr></table>"), RtfCompressed = [0] });
        check(htmlPreferred.Html.Contains("<table>") && !htmlPreferred.ConvertedFromRtf, "Available HTML takes precedence over stale RTF metadata");
        var utf8 = new Message { Html = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("<p>Привет</p>")).ToArray() };
        utf8.Properties.Add(new Property { Tag = EpropertyTag.PidTagInternetCodepage, Value = 1251 });
        check(MessageBodies.Html(utf8) == "<p>Привет</p>", "HTML Unicode BOM takes precedence over stale cached codepage");

        var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 };
        var preview = PreviewHtml.Create("<base href='file:///c:/'><meta http-equiv='refresh' content='0;url=https://tracker.invalid'><script>alert(1)</script><iframe src='https://tracker.invalid'></iframe><table><tr><td style='color:red'>Hello</td></tr></table><img src='cid:logo' onerror='alert(1)'><img src='https://tracker.invalid/pixel' srcset='https://tracker.invalid/x 2x'><svg onload='alert(1)'></svg>", _ => png);
        var safe = new HtmlParser().ParseDocument(preview);
        check(safe.QuerySelectorAll("script,iframe,base,svg").Length == 0 && !safe.All.SelectMany(e => e.Attributes).Any(a => a.Name.StartsWith("on") || a.Name == "srcset"), "HTML preview removes active elements and event handlers");
        check(safe.QuerySelector("table td")?.TextContent == "Hello" && safe.QuerySelector("table td")!.GetAttribute("style")!.Contains("red"), "HTML preview preserves email tables and colors");
        check(safe.QuerySelectorAll("img")[0].GetAttribute("src")!.StartsWith("data:image/png;base64,") && !safe.QuerySelectorAll("img")[1].HasAttribute("src"), "CID image is embedded locally while remote tracker image is blocked");
        check(safe.QuerySelector("meta[http-equiv='Content-Security-Policy']")!.GetAttribute("content")!.Contains("script-src 'none'"), "Preview document includes restrictive content policy");
        check(PreviewHtml.ImageDataUri(Encoding.UTF8.GetBytes("<svg onload=alert(1)></svg>")) is null, "Embedded active SVG is not accepted as an image");

        var path = Path.Combine(Path.GetTempPath(), "MailArchive-held-" + Guid.NewGuid().ToString("N") + ".pst");
        try
        {
            File.WriteAllText(path, "saved store");
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var report = new ConversionReport { Completed = true };
                OutputFileStatus.Apply(report, path, true);
                check(report.Completed && File.Exists(path), "Saved PST finalization succeeds while another process retains a read handle");
                var partial = new ConversionReport { Completed = false };
                OutputFileStatus.Apply(partial, path, true);
                check(partial.RetainedPartialFile == path, "Incomplete PST is reported at its actual saved pathname");
            }
        }
        finally { File.Delete(path); }
    }
}
