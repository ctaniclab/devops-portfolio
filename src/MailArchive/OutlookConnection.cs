using System.Runtime.InteropServices;

namespace MailArchive;

public sealed class OutlookDisconnectedException(Exception inner) : IOException(
    "Связь с Outlook потеряна: возможно, Outlook был закрыт. Перенос остановлен; уже сохранённые письма остались в указанном PST. Откройте Outlook и не закрывайте его во время переноса в PST. Экспорт MSG работает без Outlook.", inner);

public static class OutlookConnection
{
    public static bool IsDisconnected(Exception error)
    {
        if (error is AggregateException aggregate && aggregate.InnerExceptions.Any(IsDisconnected)) return true;
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is InvalidComObjectException || current is COMException && unchecked((uint)current.HResult) is
                0x80010108 or 0x800706BA or 0x800706BE or 0x80010007 or 0x80010012 or 0x800401FD)
                return true;
        return false;
    }
}
