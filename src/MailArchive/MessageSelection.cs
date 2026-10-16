using XstReader;

namespace MailArchive;

public sealed record MessageSelection(uint FolderId, uint MessageId)
{
    public static MessageSelection FromMessage(Message message)
    {
        if (message.Folder is null) throw new InvalidOperationException(L.T("Выберите письмо из открытого архива."));
        return new(message.Folder.Nid.dwValue, message.Nid.dwValue);
    }

    public Folder ResolveFolder(Folder root)
    {
        var found = Find(root);
        return found ?? throw new InvalidOperationException(L.T("Исходная папка выбранного письма не найдена. Откройте архив заново."));
    }

    private Folder? Find(Folder folder)
    {
        if (folder.Nid.dwValue == FolderId) return folder;
        foreach (var child in folder.Folders)
        {
            var found = Find(child);
            if (found is not null) return found;
        }
        return null;
    }

    public Message ResolveMessage(IEnumerable<Message> messages) => messages.SingleOrDefault(m => m.Nid.dwValue == MessageId)
        ?? throw new InvalidOperationException(L.T("Выбранное письмо не найдено в исходной папке. Откройте архив заново."));
}
