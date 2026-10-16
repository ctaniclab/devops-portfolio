using System.Runtime.InteropServices;
using MailArchive;

public static class TransferChecks
{
    public static void Run(Action<bool, string> check)
    {
        for (int pass = 1; pass <= 3; pass++)
        {
            var folder = new FakeFolder("pst-store", "inbox");
            var original = new FakeMail(new FakeFolder("default-account", "drafts"), "old-id");
            var session = new FakeSession();
            // Reproduce the report: an ID belonging to another store is invalid in the PST.
            bool invalidId = false;
            try { session.GetItemFromID(original.EntryID, folder.StoreID); }
            catch (COMException e) when (e.HResult == unchecked((int)0x80040107)) { invalidId = true; }
            check(invalidId, $"Pass {pass}: cross-store ID reproduces Outlook 0x80040107");
            session.Reads = 0;
            string stage = "";
            var reopened = (FakeMail)OutlookMessageTransfer.MoveAndReopen(original, folder, session, value => value, s => stage = s);
            check(original.Moves == 1 && reopened.EntryID == "new-pst-id" && session.Reads == 1
                && session.LastStore == folder.StoreID && stage == "ReopenPstMessage",
                $"Pass {pass}: PST import reopens the NEW moved ID in its actual destination store");
            check(original.DefaultSaves == 0 && reopened.Parent == folder,
                $"Pass {pass}: import never saves a new letter into account Drafts");
        }
        foreach (bool wrongStore in new[] { true, false })
        {
            var destination = new FakeFolder("pst-store", "inbox");
            var message = new FakeMail(destination, "old")
            { Redirect = wrongStore ? new("account", "inbox") : new("pst-store", "wrong-folder") };
            var session = new FakeSession(); bool rejected = false;
            try { OutlookMessageTransfer.MoveAndReopen(message, destination, session, x => x, _ => { }); }
            catch (SavedMailVerificationException) { rejected = true; }
            check(rejected && session.Reads == 0, "A moved message in the wrong " + (wrongStore ? "store" : "folder") + " cannot pass as a PST export");
        }
        var sessionFailure = new FakeSession { FailRead = true };
        string failedStage = ""; bool failed = false;
        object? movedBeforeFailure = null;
        try { OutlookMessageTransfer.MoveAndReopen(new FakeMail(new("pst-store", "inbox"), "old"), new FakeFolder("pst-store", "inbox"), sessionFailure, x => x, s => failedStage = s, value => movedBeforeFailure = value); }
        catch (COMException e) when (e.HResult == unchecked((int)0x80040107)) { failed = true; }
        check(failed && failedStage == "ReopenPstMessage" && sessionFailure.Reads == 1,
            "PST read-back errors retain the precise stage and never fall back to another store");
        check(movedBeforeFailure is FakeMail { EntryID: "new-pst-id" }, "Exact moved item remains available for isolation when reopening fails");
        var empty = new ConversionReport(); PstInventory.Verify(empty, []);
        check(!empty.OutlookReadBackVerified, "Zero converted letters cannot produce a verified PST report");
        var orphan = new ConversionReport(); bool orphanRejected = false;
        try { PstInventory.Verify(orphan, [new("PST/inbox", 1)]); }
        catch (IOException) { orphanRejected = true; }
        check(orphanRejected && !orphan.OutlookReadBackVerified, "Unverified leftover PST elements are reported rather than counted as converted");
    }

    public sealed record FakeFolder(string StoreID, string EntryID);
    public sealed class FakeMail(FakeFolder parent, string id)
    {
        public FakeFolder Parent { get; } = parent;
        public string EntryID { get; } = id;
        public FakeFolder? Redirect { get; init; }
        public int Moves { get; private set; }
        public int DefaultSaves { get; private set; }
        public void Save() => DefaultSaves++;
        public FakeMail Move(FakeFolder target) { Moves++; return new(Redirect ?? target, "new-pst-id"); }
    }
    public sealed class FakeSession
    {
        public int Reads { get; set; }
        public string? LastStore { get; private set; }
        public bool FailRead { get; init; }
        public bool CompareEntryIDs(string a, string b) => a == b;
        public FakeMail GetItemFromID(string id, string store)
        {
            Reads++; LastStore = store;
            if (FailRead || id != "new-pst-id" || store != "pst-store")
                throw new COMException("Invalid entry ID", unchecked((int)0x80040107));
            return new(new("pst-store", "inbox"), id);
        }
    }
}
