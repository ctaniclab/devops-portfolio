using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using XstReader;
using Microsoft.Win32;
using Message = XstReader.Message;

namespace MailArchive;

public partial class MessageWindow : Window
{
    private readonly XstFile? archive;
    private readonly Message message;
    private readonly MailPreview preview;
    private PreparedBody? body;
    private bool loading;
    private bool ready;
    public bool ExportRequested { get; private set; }
    public string? LastDiagnosticPath { get; private set; }
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MessageWindow(XstFile? archive, Message message)
    {
        this.archive = archive; this.message = message;
        InitializeComponent(); preview = new MailPreview(HtmlPreview);
        WindowTheme.Attach(this);
        Loaded += Reader_Loaded;
    }
    private async void Reader_Loaded(object sender, RoutedEventArgs e)
    {
        loading = true;
        try
        {
            if (archive is not null) await Task.Run(() => archive.ReadMessageDetails(message));
            SubjectText.Text = string.IsNullOrWhiteSpace(message.Subject) ? L.T("Без темы") : message.Subject;
            Title = SubjectText.Text + " — Mail Archive";
            var address = message.Properties.FirstOrDefault(p => (ushort)p.Tag == 0x0C1F)?.Value as string;
            SenderText.Text = L.T("От: ") + (message.From ?? L.T("не указан")) + (string.IsNullOrEmpty(address) || address == message.From ? "" : " <" + address + ">");
            string Names(RecipientType type) => string.Join("; ", message.Recipients.Where(r => r.RecipientType == type)
                .Select(r => string.IsNullOrEmpty(r.EmailAddress) || r.EmailAddress == r.DisplayName ? r.DisplayName : r.DisplayName + " <" + r.EmailAddress + ">"));
            HeaderDetails.Text = L.F($"Кому: {Names(RecipientType.To)}\nКопия: {Names(RecipientType.Cc)}\nСкрытая копия: {Names(RecipientType.Bcc)}\nПолучено: {message.Received?.ToLocalTime():g}\nОтправлено: {message.Submitted?.ToLocalTime():g}");
            body = await Task.Run(() => PreparedBody.FromMessage(message));
            BodyText.Text = body.Text.Length == 0 ? L.T("Текстовое содержимое отсутствует в локальном архиве.") : body.Text;
            HtmlText.Text = body.Html; MessageProperties.ItemsSource = message.Properties; Attachments.ItemsSource = message.Attachments;
            if (message.Attachments.Count > 0) Attachments.SelectedIndex = 0;
            ready = true; ExportMessageButton.IsEnabled = archive is not null;
            await ShowPreviewAsync();
            ReaderStatus.Text = message.Attachments.Count == 0 ? L.T("Вложений нет. Исходный архив открыт только для чтения.") : L.F($"Вложений: {message.Attachments.Count}. Для сохранения выберите вложение сверху.");
            Ready.TrySetResult();
        }
        catch (Exception error)
        {
            LastDiagnosticPath = DiagnosticLog.Write(error, L.T("Чтение письма"));
            PreviewHint.Text = L.T("Не удалось прочитать письмо: ") + error.Message;
            ReaderStatus.Text = L.T("Ошибка чтения. Подробности в журнале главного окна.");
            Ready.TrySetException(error);
        }
        finally { loading = false; SaveAttachmentButton.IsEnabled = Attachments.SelectedItem is Attachment { IsFile: true }; }
    }
    private async Task ShowPreviewAsync()
    {
        if (body is null) return;
        try
        {
            int imageBytes = 0;
            bool dark = DisplaySettings.IsDark && OriginalColors.IsChecked != true;
            var html = await Task.Run(() => PreviewHtml.Create(body.Html, cid =>
            {
                var attachment = message.Attachments.FirstOrDefault(a => a.IsFile && string.Equals(a.ContentId?.Trim('<', '>'), cid, StringComparison.OrdinalIgnoreCase));
                if (archive is null || attachment is null || attachment.Size > 8 * 1024 * 1024 || imageBytes >= 24 * 1024 * 1024) return null;
                try
                {
                    using var buffer = new BoundedImageStream(8 * 1024 * 1024);
                    archive.SaveAttachment(buffer, attachment); imageBytes += (int)buffer.Length; return buffer.ToArray();
                }
                catch (Exception error) when (error is IOException or XstException) { return null; }
            }, dark));
            HtmlPreview.Visibility = Visibility.Visible;
            await preview.ShowAsync(html).WaitAsync(TimeSpan.FromSeconds(25));
            PreviewHint.Visibility = Visibility.Collapsed;
        }
        catch (Exception error)
        {
            HtmlPreview.Visibility = Visibility.Collapsed; PreviewHint.Visibility = Visibility.Visible;
            PreviewHint.Text = L.T("HTML-просмотр недоступен. Откройте вкладку «Текст». Для HTML нужен бесплатный Microsoft Edge WebView2 Runtime.");
            LastDiagnosticPath = DiagnosticLog.Write(error, L.T("Просмотр HTML письма")); BodyTabs.SelectedIndex = 1;
        }
    }
    private async void Colors_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready || loading) return;
        loading = true;
        try { await ShowPreviewAsync(); }
        finally { loading = false; }
    }
    private void Attachment_Selected(object sender, SelectionChangedEventArgs e) => SaveAttachmentButton.IsEnabled = !loading && Attachments.SelectedItem is Attachment { IsFile: true };
    private async void SaveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (archive is null || Attachments.SelectedItem is not Attachment { IsFile: true } attachment || loading) return;
        var dialog = new SaveFileDialog { FileName = ArchiveUtilities.SafeAttachmentName(attachment.FileName), OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        loading = true; SaveAttachmentButton.IsEnabled = false; ExportMessageButton.IsEnabled = false;
        var temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await Task.Run(() => archive.SaveAttachment(temporary, null, attachment));
            File.Move(temporary, dialog.FileName, overwrite: true); ReaderStatus.Text = L.T("Вложение сохранено: ") + dialog.FileName;
        }
        catch (Exception error) { LastDiagnosticPath = DiagnosticLog.Write(error, L.T("Сохранение вложения")); MessageBox.Show(this, error.Message, L.T("Вложение"), MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); loading = false; SaveAttachmentButton.IsEnabled = true; ExportMessageButton.IsEnabled = true; }
    }
    private void Export_Click(object sender, RoutedEventArgs e) { if (!loading && archive is not null) { ExportRequested = true; Close(); } }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Reader_Closing(object? sender, CancelEventArgs e)
    {
        if (loading) { e.Cancel = true; return; }
        preview.Dispose(); message.ClearContents(); message.RtfCompressed = null; message.Properties.Clear(); message.Recipients.Clear();
    }
    private sealed class BoundedImageStream(int limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) { if (Position + count > limit) throw new IOException(L.T("Изображение слишком большое.")); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { if (Position + buffer.Length > limit) throw new IOException(L.T("Изображение слишком большое.")); base.Write(buffer); }
    }
}
