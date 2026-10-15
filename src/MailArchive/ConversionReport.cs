namespace MailArchive;

public sealed record PstFolderInfo(string Path, int Messages);
public sealed record ConversionIssue(string Folder, string Subject, string Reason, string Stage = "", string? Details = null, string? ItemClass = null, string? ErrorCode = null);
public sealed class ConversionReport
{
    public string AppVersion { get; set; } = "1.6.0";
    public string Mode { get; set; } = "Mailbox";
    public MessageSelection? Selection { get; set; }
    public List<MessageSelection>? Selections { get; set; }
    public string OutputFormat { get; set; } = "PST";
    public List<string> MsgFiles { get; set; } = [];
    public string? RetainedPartialDirectory { get; set; }
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime FinishedUtc { get; set; }
    public int Examined { get; set; }
    public int Converted { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public int FolderErrors { get; set; }
    public int MetadataWarnings { get; set; }
    public int RtfBodiesConvertedToHtml { get; set; }
    public int ContentVerified { get; set; }
    public int DatesVerified { get; set; }
    public int MessagesWithoutSourceDates { get; set; }
    public bool IncludeServiceFolders { get; set; }
    public List<string> ExcludedServiceFolders { get; set; } = [];
    public int? OutputMessagesVerified { get; set; }
    public bool OutlookReadBackVerified { get; set; }
    public bool MsgReadBackVerified { get; set; }
    public List<PstFolderInfo> OutputFolders { get; set; } = [];
    public bool Cancelled { get; set; }
    public bool Completed { get; set; }
    public string? FatalError { get; set; }
    public string? RetainedPartialFile { get; set; }
    public List<ConversionIssue> Issues { get; set; } = [];
    public List<string> Limitations { get; set; } = [
        "Восстановление писем, не полная копия MAPI-хранилища. Календари, контакты, задачи и служебные элементы не переносятся.",
        "Не гарантируется сохранение всех MAPI-свойств, цифровых подписей, правил, категорий и флагов. Не заменяет резервную копию.",
        "В OST доступны только локально сохранённые данные. Отсутствующие на диске письма и вложения восстановить нельзя.",
        "Письма с вложенными сообщениями, OLE/ссылочными вложениями и S/MIME пропускаются с записью в отчёт.",
        "RTF переводится в HTML с сохранением доступного форматирования; вёрстка может отличаться от Outlook. Исходник остаётся в OST."
        ,"Даты отправки и получения и состояние черновика восстанавливаются в закрытом временном MSG. Транспортные флаги очереди отправки и уведомлений не копируются. Приложение ничего не отправляет."
    ];
}
