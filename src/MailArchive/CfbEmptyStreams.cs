using System.Buffers.Binary;

namespace MailArchive;

// OpenMcdf 3.3.0 writes FREESECT for empty stream directory entries. MS-CFB
// 2.6.1 requires ENDOFCHAIN. Normalize ONLY a newly generated, closed MSG;
// nonempty data, allocation tables and the source OST/PST are never changed.
internal static class CfbEmptyStreams
{
    public static void Normalize(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var header = new byte[512]; file.ReadExactly(header);
        if (!header.AsSpan(0, 8).SequenceEqual(new byte[] { 0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1 }))
            throw new IOException("Неверная сигнатура созданного MSG.");
        int shift = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30));
        if (shift is not (9 or 12)) throw new IOException("Неверный размер сектора MSG.");
        int sectorSize = 1 << shift;
        uint U(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        byte[] Sector(uint id)
        {
            long offset = ((long)id + 1) * sectorSize;
            if (offset < sectorSize || offset + sectorSize > file.Length) throw new IOException("Сектор MSG вне границ файла.");
            file.Position = offset; var data = new byte[sectorSize]; file.ReadExactly(data); return data;
        }
        var fat = new List<uint>();
        void Fat(uint id) { if (id != uint.MaxValue) fat.Add(id); }
        for (int i = 76; i < 512; i += 4) Fat(U(header, i));
        var visited = new HashSet<uint>(); uint difat = U(header, 68);
        for (uint i = 0; i < U(header, 72); i++)
        {
            if (!visited.Add(difat)) throw new IOException("Цикл DIFAT в MSG.");
            var block = Sector(difat);
            for (int offset = 0; offset < sectorSize - 4; offset += 4) Fat(U(block, offset));
            difat = U(block, sectorSize - 4);
        }
        uint directory = U(header, 48); visited.Clear();
        var fatCache = new Dictionary<uint, byte[]>();
        while (directory != 0xFFFFFFFE)
        {
            if (!visited.Add(directory)) throw new IOException("Цикл каталога MSG.");
            var block = Sector(directory); bool changed = false;
            for (int offset = 0; offset < sectorSize; offset += 128)
            {
                if (block[offset + 66] == 2 && BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(offset + 120)) == 0
                    && U(block, offset + 116) == uint.MaxValue)
                { BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(offset + 116), 0xFFFFFFFE); changed = true; }
            }
            if (changed) { file.Position = ((long)directory + 1) * sectorSize; file.Write(block); }
            uint fatIndex = directory / (uint)(sectorSize / 4);
            if (fatIndex >= fat.Count) throw new IOException("Неполная FAT в MSG.");
            if (!fatCache.TryGetValue(fatIndex, out var table)) fatCache[fatIndex] = table = Sector(fat[(int)fatIndex]);
            directory = U(table, (int)(directory % (sectorSize / 4)) * 4);
        }
    }
}
