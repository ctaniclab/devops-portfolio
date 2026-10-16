"""Independent test-only MS-OXMSG reader; extract-msg is never shipped in the EXE."""
import datetime
import hashlib
import json
import pathlib
import sys
import extract_msg
from extract_msg.structures.entry_id import EntryID, OneOffRecipient

directory = pathlib.Path(sys.argv[1])
files = sorted(directory.glob('*.msg'))
assert len(files) >= 4, 'Synthetic cases and public OST/PST messages are required'
for path in files:
    expected = json.loads(path.with_suffix('.json').read_text(encoding='utf-8'))
    with extract_msg.openMsg(path) as message:
        assert message.subject == expected['Subject'], path
        assert message.body == expected['Text'], path
        assert message.htmlBody.decode('utf-8') == expected['Html'], path
        assert len(message.recipients) == expected['Recipients'], path
        for actual, wanted in zip(message.recipients, expected['Addresses']):
            assert (actual.name, actual.email, int(actual.type)) == (wanted['Name'], wanted['Email'], wanted['Type']), path
            entry = EntryID.autoCreate(actual.getStream('__substg1.0_0FFF0102'))
            assert isinstance(entry, OneOffRecipient), path
            assert (entry.displayName, entry.emailAddress) == (wanted['Name'], wanted['Email']), path
        for tag, field in [('0E060040', 'Received'), ('00390040', 'Sent')]:
            actual = message.props.getValue(tag)
            wanted = expected[field]
            assert actual == (datetime.datetime.fromisoformat(wanted.replace('Z', '+00:00')) if wanted else None), (path, field, actual, wanted)
        assert sorted(hashlib.sha256(a.data).hexdigest().upper() for a in message.attachments) == sorted(expected['AttachmentHashes']), path
        for actual, wanted in zip(message.attachments, expected['Attachments']):
            assert actual.longFilename == wanted['Name'], path
            assert actual.cid == wanted['ContentId'], path
        assert not (message.props.getValue('0E070003') & 0x304), path
    print('PASS: independent MSG reader:', path.name)
print(f'Independently verified {len(files)} MSG files without Outlook.')
