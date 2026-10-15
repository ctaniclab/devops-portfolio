using MailArchive;

internal static class ContentChecks
{
    public static void Run(Action<bool, string> check)
    {
        void Reject(SavedMailContent expected, SavedMailContent actual, string name)
        {
            try { expected.Verify(actual); }
            catch (SavedMailVerificationException) { check(true, name); return; }
            throw new Exception("FAIL: " + name);
        }
        var expected = new SavedMailContent("Проверка", "<p>Привет, мир</p><p>Вторая строка</p>", "Привет, мир\nВторая строка", 2, ["hash-a", "hash-b"]);
        Reject(expected, new("", "", "", 0, []), "Blank Outlook item cannot count as a converted mail");
        Reject(expected, expected with { Html = "<html><body></body></html>", Text = "" }, "Correct subject with an empty saved body is rejected");
        Reject(expected, expected with { Html = "<p>Привет, мир</p>", Text = "Привет, мир" }, "Truncated saved body is rejected");
        expected.Verify(expected with { Html = "<html><head><style>ignored CSS</style></head><body><div>Привет,\u00a0 мир</div><div>Вторая\r\n строка</div></body></html>" });
        check(true, "Word HTML rewriting and whitespace changes preserve all source text");
        Reject(expected, expected with { AttachmentHashes = ["hash-a", "changed"] }, "Same attachment count with changed bytes fails SHA256 verification");
        Reject(expected, expected with { Recipients = 0 }, "Lost recipients are detected on read-back");
        var image = new SavedMailContent("Image", "<img src='cid:logo'>", "", 0, []);
        Reject(image, image with { Html = "<p></p>" }, "Image-only message cannot pass after losing its image");
        var subjectOnly = new SavedMailContent("Legitimate subject-only draft", "<p></p>", "", 0, []);
        subjectOnly.Verify(subjectOnly); check(true, "An intentionally bodyless source is not mislabeled as data loss");
        var name1 = ArchiveUtilities.MsgFileName("../../same:subject?", 0x100, 0x200);
        var name2 = ArchiveUtilities.MsgFileName("../../same:subject?", 0x100, 0x201);
        check(name1 != name2 && name1.EndsWith(".msg") && !name1.Contains('/') && !name1.Contains(':') && !name1.Contains('?'), "MSG names cannot escape output directory and equal subjects have different paths");
        var report = new ConversionReport { Converted = 1, ContentVerified = 0 };
        bool rejected = false;
        try { PstInventory.Verify(report, [new("PST/Inbox", 1)]); } catch (IOException) { rejected = true; }
        check(rejected && !report.OutlookReadBackVerified, "A matching PST item count cannot substitute for message-content verification");
        check(ExportFolderLayout.IsServiceBranch(new() { Name = "Ошибки синхронизации (только этот компьютер)" }, 2)
            && !ExportFolderLayout.IsServiceBranch(new() { Name = "Sync Issues" }, 4), "Top-level Outlook logs are excluded without hiding a deep user's folder with the same name");
    }
}
