Source: https://github.com/Dijji/XstReader
Commit: 3b7856c8b3d01bdf8aa13744e476d1ef7761b832
License: MS-PL (see LICENSE-MS-PL.md).
Modification: Message.cs Windows-only blocks enabled with WINDOWS instead of !NETCOREAPP for .NET 8 WPF.
System.Windows imports are conditional on WINDOWS for the portable reader checks.
All source files have #nullable disable to retain upstream nullability behavior under modern C#.
Crypto.cs credits libpff (LGPLv3+) for lookup tables; upstream notices preserved.
