using System.Buffers.Binary;
using System.Text;

namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter06
{
    [TestMethod]
    public void Write_ahead_log_validates_crc_and_truncates_torn_tail_record_on_recovery()
    {
        using MemoryStream walStream = new MemoryStream();
        BinaryWal wal = new BinaryWal(walStream);

        wal.Append(lsn: 1, key: "k1", value: "v1");
        wal.Append(lsn: 2, key: "k2", value: "v2");

        walStream.Write([0xDE, 0xAD, 0xBE, 0xEF, 0x01]);

        IReadOnlyList<(long Lsn, string Key, string Value)> recovered = wal.RecoverValidRecords();

        Assert.HasCount(2, recovered);
        Assert.AreEqual("v1", recovered[0].Value);
        Assert.AreEqual("v2", recovered[1].Value);
    }

    [TestMethod]
    public void Lsm_engine_reads_across_memtable_and_sstables_and_honors_tombstone_deletes()
    {
        MiniLsmEngine lsm = new MiniLsmEngine(memTableFlushThreshold: 2);

        lsm.Put("device-1", "v1");
        lsm.Put("device-2", "v1");
        lsm.Put("device-1", "v2");
        lsm.Delete("device-2");

        Assert.AreEqual("v2", lsm.Get("device-1"));
        Assert.IsNull(lsm.Get("device-2"));

        lsm.CompactAll();
        Assert.AreEqual(1, lsm.SsTableCount);
        Assert.AreEqual("v2", lsm.Get("device-1"));
        Assert.IsNull(lsm.Get("device-2"));
    }
}

file sealed class BinaryWal
{
    private readonly MemoryStream _stream;

    public BinaryWal(MemoryStream stream)
    {
        _stream = stream;
    }

    public void Append(long lsn, string key, string value)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        byte[] valBytes = Encoding.UTF8.GetBytes(value);
        byte[] payload = new byte[8 + 4 + 4 + keyBytes.Length + valBytes.Length];

        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(0, 8), lsn);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), keyBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12, 4), valBytes.Length);
        keyBytes.CopyTo(payload.AsSpan(16));
        valBytes.CopyTo(payload.AsSpan(16 + keyBytes.Length));

        uint crc = ComputeFnv1a32(payload);
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(0, 4), crc);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(4, 4), payload.Length);

        _stream.Position = _stream.Length;
        _stream.Write(header);
        _stream.Write(payload);
    }

    public IReadOnlyList<(long Lsn, string Key, string Value)> RecoverValidRecords()
    {
        List<(long, string, string)> result = new List<(long, string, string)>();
        _stream.Position = 0;
        Span<byte> header = stackalloc byte[8];

        while (_stream.Position + 8 <= _stream.Length)
        {
            long recordStart = _stream.Position;
            int readHeader = _stream.Read(header);
            if (readHeader < 8)
            {
                _stream.SetLength(recordStart);
                break;
            }

            uint expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(0, 4));
            int payloadLen = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4, 4));
            if (payloadLen <= 0 || _stream.Position + payloadLen > _stream.Length)
            {
                _stream.SetLength(recordStart);
                break;
            }

            byte[] payload = new byte[payloadLen];
            _ = _stream.Read(payload, 0, payloadLen);
            if (ComputeFnv1a32(payload) != expectedCrc)
            {
                _stream.SetLength(recordStart);
                break;
            }

            long lsn = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(0, 8));
            int keyLen = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(8, 4));
            int valLen = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(12, 4));
            string key = Encoding.UTF8.GetString(payload, 16, keyLen);
            string val = Encoding.UTF8.GetString(payload, 16 + keyLen, valLen);
            result.Add((lsn, key, val));
        }

        return result;
    }

    private static uint ComputeFnv1a32(ReadOnlySpan<byte> bytes)
    {
        uint hash = 2166136261u;
        foreach (byte b in bytes)
        {
            hash ^= b;
            hash *= 16777619u;
        }

        return hash;
    }
}

file readonly record struct LsmEntry(string? Value, bool IsTombstone);

file sealed class MiniLsmEngine
{
    private readonly int _flushThreshold;
    private readonly SortedDictionary<string, LsmEntry> _memTable = new SortedDictionary<string, LsmEntry>(StringComparer.Ordinal);
    private readonly List<SortedDictionary<string, LsmEntry>> _ssTables = new List<SortedDictionary<string, LsmEntry>>();

    public MiniLsmEngine(int memTableFlushThreshold)
    {
        _flushThreshold = memTableFlushThreshold;
    }

    public int SsTableCount
    {
        get { return _ssTables.Count; }
    }

    public void Put(string key, string value)
    {
        _memTable[key] = new LsmEntry(value, IsTombstone: false);
        FlushIfFull();
    }

    public void Delete(string key)
    {
        _memTable[key] = new LsmEntry(null, IsTombstone: true);
        FlushIfFull();
    }

    public string? Get(string key)
    {
        if (_memTable.TryGetValue(key, out LsmEntry memEntry))
        {
            return memEntry.IsTombstone ? null : memEntry.Value;
        }

        for (int i = _ssTables.Count - 1; i >= 0; i--)
        {
            if (_ssTables[i].TryGetValue(key, out LsmEntry sstEntry))
            {
                return sstEntry.IsTombstone ? null : sstEntry.Value;
            }
        }

        return null;
    }

    public void CompactAll()
    {
        SortedDictionary<string, LsmEntry> merged = new SortedDictionary<string, LsmEntry>(StringComparer.Ordinal);
        foreach (SortedDictionary<string, LsmEntry> sst in _ssTables)
        {
            foreach ((string k, LsmEntry v) in sst)
            {
                merged[k] = v;
            }
        }

        foreach ((string k, LsmEntry v) in _memTable)
        {
            merged[k] = v;
        }

        _memTable.Clear();
        _ssTables.Clear();

        SortedDictionary<string, LsmEntry> compacted = new SortedDictionary<string, LsmEntry>(StringComparer.Ordinal);
        foreach ((string k, LsmEntry v) in merged)
        {
            if (!v.IsTombstone)
            {
                compacted[k] = v;
            }
        }

        _ssTables.Add(compacted);
    }

    private void FlushIfFull()
    {
        if (_memTable.Count >= _flushThreshold)
        {
            _ssTables.Add(new SortedDictionary<string, LsmEntry>(_memTable, StringComparer.Ordinal));
            _memTable.Clear();
        }
    }
}
