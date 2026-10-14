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
