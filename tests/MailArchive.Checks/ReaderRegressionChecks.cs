using System.Collections;
using System.IO.Compression;
using System.Reflection;
using XstReader;

internal static class ReaderRegressionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), "MailArchive-block-" + Guid.NewGuid().ToString("N"));
        try
        {
            var expected = Enumerable.Range(0, 60000).Select(i => (byte)(i * 17)).ToArray();
            foreach (var level in new[] { CompressionLevel.Optimal, CompressionLevel.Fastest, CompressionLevel.SmallestSize })
            {
                using var packed = new MemoryStream();
                using (var zlib = new ZLibStream(packed, level, leaveOpen: true)) zlib.Write(expected);
                File.WriteAllBytes(path, packed.ToArray());
                var ndb = new NDB(path);
                typeof(NDB).GetProperty(nameof(NDB.IsUnicode4K))!.SetValue(ndb, true);
                using var stream = new FragmentedFileStream(path);
                var block = new DataRef { Length = (int)packed.Length, InflatedLength = expected.Length };
                var args = new object?[] { stream, block, 0, null, 0 };
                var result = (byte[])Invoke(typeof(NDB).GetMethod("ReadAndDecompress", BindingFlags.NonPublic | BindingFlags.Instance)!, ndb, args)!;
                check(result.SequenceEqual(expected), $"OST zlib {level}: full block survives fragmented stream reads");
            }
            var valid = File.ReadAllBytes(path);
            var damaged = valid.ToArray(); damaged[^1] ^= 0xFF;
            File.WriteAllBytes(path, damaged);
            check(RejectBlock(path, valid.Length, expected.Length), "Corrupted OST zlib checksum is rejected");
            File.WriteAllBytes(path, valid[..^3]);
            check(RejectBlock(path, valid.Length, expected.Length), "Truncated OST block is rejected instead of zero-filled");
            File.WriteAllBytes(path, valid);
            check(RejectBlock(path, valid.Length, expected.Length - 1), "OST inflated block larger than declared is rejected");
            check(RejectBlock(path, valid.Length, expected.Length + 1), "OST inflated block shorter than declared is rejected");
            // The subject points at a valid, zero-length heap allocation (a legitimate empty subject).
            check(ReadEmptySubject(path, EpropertyType.PtypString) == "<No subject>", "Empty Unicode subject does not abort folder enumeration");
            check(ReadEmptySubject(path, EpropertyType.PtypString8) == "<No subject>", "Empty ANSI subject does not abort folder enumeration");
            check(ReadSparseBitmap(path) == 42, "Table column existence uses stored bitmap bounds rather than live column count");
            var large = new NDB(path);
            var tree = (BTree<DataRef>)typeof(NDB).GetField("dataTree", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(large)!;
            var reference = new DataRef { Key = 0x100000100UL };
            tree.Root.Children.Add(reference);
            check(ReferenceEquals(large.LookupDataBlock(reference.Key | 1UL), reference), "64-bit block IDs retain high bits while clearing reserved low bit");
        }
        finally { File.Delete(path); }
    }

    private static bool RejectBlock(string path, int stored, int inflated)
    {
        var ndb = new NDB(path);
        typeof(NDB).GetProperty(nameof(NDB.IsUnicode4K))!.SetValue(ndb, true);
        using var stream = File.OpenRead(path);
        try
        {
            Invoke(typeof(NDB).GetMethod("ReadAndDecompress", BindingFlags.NonPublic | BindingFlags.Instance)!,
                ndb, [stream, new DataRef { Length = stored, InflatedLength = inflated }, 0, null, 0]);
            return false;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or XstException) { return true; }
    }

    private static int ReadSparseBitmap(string path)
    {
        var ltp = new LTP(new NDB(path));
        var rowType = typeof(LTP).GetNestedType("RowDataBlock", BindingFlags.NonPublic)!;
        var row = Activator.CreateInstance(rowType)!;
        rowType.GetField("Buffer")!.SetValue(row, new byte[] { 42, 0, 0, 0, 0, 0x40 });
        rowType.GetField("Length")!.SetValue(row, 6);
        var rows = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!;
        rows.Add(row);
        var heapType = typeof(LTP).GetNestedType("HNDataBlock", BindingFlags.NonPublic)!;
        var heaps = Activator.CreateInstance(typeof(List<>).MakeGenericType(heapType))!;
        var table = new TCINFO { cCols = 1, rgibTCI_1b = 4, rgibTCI_bm = 6 };
        var column = new TCOLDESC { tag = ((uint)EpropertyTag.PidTagContentCount << 16) | (uint)EpropertyType.PtypInteger32, cbData = 4, iBit = 9 };
        var getters = new PropertyGetters<CountRow> { { EpropertyTag.PidTagContentCount, (target, value) => target.Count = value } };
        using var stream = File.OpenRead(path);
        var method = typeof(LTP).GetMethod("ReadTableData", BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(typeof(CountRow));
        var result = (IEnumerable<CountRow>)Invoke(method, ltp,
            [stream, table, heaps, rows, new[] { column }, new List<TCOLDESC> { column }, null,
                new TCROWIDUnicode[] { new() }, getters, null, null])!;
        return result.Single().Count;
    }

    public sealed class CountRow { public int Count { get; set; } }

    private static string ReadEmptySubject(string path, EpropertyType type)
    {
        var ltp = new LTP(new NDB(path));
        var heapType = typeof(LTP).GetNestedType("HNDataBlock", BindingFlags.NonPublic)!;
        var block = Activator.CreateInstance(heapType)!;
        heapType.GetField("Buffer")!.SetValue(block, Array.Empty<byte>());
        heapType.GetField("rgibAlloc")!.SetValue(block, new ushort[] { 0, 0 });
        var blocks = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(heapType))!;
        blocks.Add(block);
        var rowType = typeof(LTP).GetNestedType("RowDataBlock", BindingFlags.NonPublic)!;
        var row = Activator.CreateInstance(rowType)!;
        rowType.GetField("Buffer")!.SetValue(row, new byte[] { 0x20, 0, 0, 0 });
        var column = new TCOLDESC { tag = ((uint)EpropertyTag.PidTagSubjectW << 16) | (uint)type, cbData = 4 };
        using var stream = File.OpenRead(path);
        return (string)Invoke(typeof(LTP).GetMethod("ReadTableColumnValue", BindingFlags.NonPublic | BindingFlags.Instance)!,
            ltp, [stream, null, blocks, row, 0L, column])!;
    }

    private static object? Invoke(MethodInfo method, object target, object?[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }

    private sealed class FragmentedFileStream(string path) : FileStream(path, FileMode.Open, FileAccess.Read)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1));
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 1)]);
    }
}
