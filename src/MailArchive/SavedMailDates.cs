namespace MailArchive;

public sealed record SavedMailDates(DateTime? ReceivedUtc, DateTime? SentUtc)
{
    public const uint ReceivedTag = 0x0E060040, SentTag = 0x00390040;
    public bool HasDates => ReceivedUtc is not null || SentUtc is not null;
    public void Verify(Func<uint, DateTime?> readUtc)
    {
        foreach (var (tag, expected, name) in new[] { (ReceivedTag, ReceivedUtc, L.T("получения")), (SentTag, SentUtc, L.T("отправки")) })
        {
            if (expected is not DateTime date) continue; // Missing source dates cannot be reconstructed.
            var actual = readUtc(tag);
            if (actual is null || Math.Abs((actual.Value - date).TotalSeconds) > 1)
                throw new SavedMailVerificationException(L.F($"После сохранения изменилась дата {name}: ожидалось {date:O}, прочитано {actual:O}."));
        }
    }
}
