Source: https://github.com/Dijji/XstReader
Commit: 3b7856c8b3d01bdf8aa13744e476d1ef7761b832
License: MS-PL (see LICENSE-MS-PL.md).
Modification: Message.cs Windows-only blocks enabled with WINDOWS instead of !NETCOREAPP for .NET 8 WPF.
System.Windows imports are conditional on WINDOWS for the portable reader checks.
All source files have #nullable disable to retain upstream nullability behavior under modern C#.
Crypto.cs credits libpff (LGPLv3+) for lookup tables; upstream notices preserved.
1.0.1 adaptations: NDB/Map use exact-length reads, bounded ZLibStream decoding (all compression levels,
checksum/length validation), and preserve the full 64-bit BID. LTP handles empty subject allocations,
uses the on-disk bitmap size, validates heap references, and accepts empty BTH roots.
XstFile reads contents independently of cached counts, with explicit per-message header recovery from
row IDs and diagnostic ReadIssue records when table columns are unreadable.

Mail Archive 1.3.0 reads the optional PidTagContainerClass folder property to distinguish
mail/IMAP containers from contacts, calendar and configuration items in the simplified viewer.
Unknown classes remain traversable; actual contents tables, not cached counts, determine visibility.

Mail Archive 1.5 displays UTC archive message dates in the local Windows time zone in Message.DisplayDate.
On-disk values remain unchanged and UTC is retained for conversion and verification.
