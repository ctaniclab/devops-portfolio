namespace MailArchive;

// OpenSharedItem + Move imports a standalone MSG directly into the chosen PST.
// Saving a new MailItem may instead use the profile's default Drafts folder.
public static class OutlookMessageTransfer
{
    public static object MoveAndReopen(object sharedMessage, object destination, object outlookSession,
        Func<object, object> keep, Action<string> stage, Action<object>? onMoved = null)
    {
        dynamic source = sharedMessage, folder = destination, session = outlookSession;
        stage("MoveToPst");
        dynamic moved = keep(source.Move(folder)); // Move returns a NEW item, with a NEW EntryID.
        onMoved?.Invoke((object)moved);
        stage("VerifyPstLocation");
        dynamic parent = keep(moved.Parent);
        string storeId = parent.StoreID;
        if (!session.CompareEntryIDs(storeId, (string)folder.StoreID)
            || !session.CompareEntryIDs((string)parent.EntryID, (string)folder.EntryID))
            throw new SavedMailVerificationException("Outlook поместил письмо вне выбранной папки PST.");
        string entryId = moved.EntryID;
        if (string.IsNullOrEmpty(entryId))
            throw new SavedMailVerificationException("Outlook не присвоил перенесённому письму EntryID.");
        // Do not reuse the pre-Move object/EntryID or silently retry in the default store.
        stage("ReopenPstMessage");
        dynamic reopened = keep(session.GetItemFromID(entryId, storeId));
        dynamic reopenedParent = keep(reopened.Parent);
        if (!session.CompareEntryIDs((string)reopenedParent.StoreID, (string)folder.StoreID)
            || !session.CompareEntryIDs((string)reopenedParent.EntryID, (string)folder.EntryID))
            throw new SavedMailVerificationException("Повторно открытое письмо находится вне выбранной папки PST.");
        return (object)reopened;
    }
}
