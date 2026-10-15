using System.Text;
using AngleSharp.Html.Parser;

namespace MailArchive;

public sealed class SavedMailVerificationException(string reason) : IOException(reason);

public sealed record SavedMailContent(string Subject, string Html, string Text, int Recipients, IReadOnlyList<string> AttachmentHashes)
{
    public static string VisibleText(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        foreach (var element in document.QuerySelectorAll("head,script,style,template").ToArray()) element.Remove();
        return Normalize(document.Body?.TextContent ?? "");
    }

    private static string Normalize(string text) => new(text.Normalize(NormalizationForm.FormC)
        .Where(c => !char.IsWhiteSpace(c) && c is not '\uFEFF' and not '\u200B' and not '\u00AD').ToArray());

    public void Verify(SavedMailContent actual)
    {
        if (!string.Equals(Subject.Normalize(NormalizationForm.FormC), actual.Subject.Normalize(NormalizationForm.FormC), StringComparison.Ordinal))
            throw new SavedMailVerificationException("После сохранения и повторного открытия не совпала тема письма.");
        var expectedText = VisibleText(Html);
        var actualText = VisibleText(actual.Html);
        if (actualText.Length == 0) actualText = Normalize(actual.Text);
        // Word may insert list numbering and line breaks. Every source text character
        // must still occur in order; a nonempty body cannot pass as an empty mail.
        int matched = 0;
        foreach (var character in actualText)
            if (matched < expectedText.Length && expectedText[matched] == character) matched++;
        if (matched != expectedText.Length)
            throw new SavedMailVerificationException("После сохранения и повторного открытия текст письма отсутствует или неполон.");
        if (expectedText.Length == 0 && new HtmlParser().ParseDocument(Html).QuerySelector("img") is not null
            && new HtmlParser().ParseDocument(actual.Html).QuerySelector("img") is null)
            throw new SavedMailVerificationException("После сохранения пропало изображение из письма без текста.");
        if (actual.Recipients != Recipients)
            throw new SavedMailVerificationException("После сохранения не совпало количество получателей.");
        if (!AttachmentHashes.Order(StringComparer.Ordinal).SequenceEqual(actual.AttachmentHashes.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new SavedMailVerificationException("После сохранения не совпали количество или SHA256 вложений.");
    }
}
