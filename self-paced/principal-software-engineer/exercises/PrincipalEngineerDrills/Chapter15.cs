namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter15
{
    [TestMethod]
    public void Gfs_primary_chunk_server_pads_chunk_at_64mb_boundary_and_assigns_deterministic_append_offsets()
    {
        GfsChunkPrimary primary = new GfsChunkPrimary(maxChunkBytes: 100);

        (bool RollToNextChunk, int Offset) append1 = primary.RecordAppend(recordBytes: 60);
        (bool RollToNextChunk, int Offset) append2ExceedingBoundary = primary.RecordAppend(recordBytes: 50);

        Assert.IsFalse(append1.RollToNextChunk);
        Assert.AreEqual(0, append1.Offset);

        // 60 + 50 > 100: Primary pads current chunk to 100 and instructs client to retry on next chunk!
        Assert.IsTrue(append2ExceedingBoundary.RollToNextChunk);
        Assert.AreEqual(100, primary.CurrentLengthBytes);
    }

    [TestMethod]
    public void Chubby_session_enters_jeopardy_grace_window_on_master_failover_and_validates_sequencers()
    {
        ChubbyCoordinator cell = new ChubbyCoordinator(jeopardyGraceMs: 45_000);
        ChubbySequencer seq1 = cell.AcquireExclusiveLock("/ls/cell/bigtable/master", "server-a", leaseExpiresAtMs: 10_000);

        // At t = 12,000ms (2s after local lease expiry during Master failover), client is within 45s Jeopardy window
        Assert.IsTrue(cell.IsSessionSavedByJeopardy("/ls/cell/bigtable/master", nowMs: 12_000));

        // Once lease + jeopardy expires (at t = 60,000ms), server-b acquires the lock with higher sequencer generation
        ChubbySequencer seq2 = cell.AcquireExclusiveLock("/ls/cell/bigtable/master", "server-b", leaseExpiresAtMs: 70_000, nowMs: 60_000);
        Assert.IsFalse(cell.CheckSequencer(seq1));
        Assert.IsTrue(cell.CheckSequencer(seq2));
    }
}

file sealed class GfsChunkPrimary
{
    private readonly int _maxChunkBytes;

    public int CurrentLengthBytes { get; private set; }

    public GfsChunkPrimary(int maxChunkBytes)
    {
        _maxChunkBytes = maxChunkBytes;
    }

    public (bool RollToNextChunk, int Offset) RecordAppend(int recordBytes)
    {
        if (CurrentLengthBytes + recordBytes > _maxChunkBytes)
        {
            CurrentLengthBytes = _maxChunkBytes;
            return (RollToNextChunk: true, Offset: -1);
        }

        int assignedOffset = CurrentLengthBytes;
        CurrentLengthBytes += recordBytes;
        return (RollToNextChunk: false, Offset: assignedOffset);
    }
}

file readonly record struct ChubbySequencer(string LockPath, long GenerationNumber);

file sealed class ChubbyCoordinator
{
    private readonly long _jeopardyGraceMs;
    private readonly Dictionary<string, (string Owner, long LeaseExpiresAtMs, long Generation)> _locks =
        new Dictionary<string, (string, long, long)>(StringComparer.Ordinal);
    private long _generationCounter;

    public ChubbyCoordinator(long jeopardyGraceMs)
    {
        _jeopardyGraceMs = jeopardyGraceMs;
    }

    public ChubbySequencer AcquireExclusiveLock(string path, string owner, long leaseExpiresAtMs, long nowMs = 0)
    {
        if (_locks.TryGetValue(path, out (string Owner, long LeaseExpiresAtMs, long Generation) existing))
        {
            if (nowMs < existing.LeaseExpiresAtMs + _jeopardyGraceMs)
            {
                throw new InvalidOperationException("Lock is still held or inside Jeopardy grace window.");
            }
        }

        _generationCounter++;
        _locks[path] = (owner, leaseExpiresAtMs, _generationCounter);
        return new ChubbySequencer(path, _generationCounter);
    }

    public bool IsSessionSavedByJeopardy(string path, long nowMs)
    {
        if (!_locks.TryGetValue(path, out (string Owner, long LeaseExpiresAtMs, long Generation) existing))
        {
            return false;
        }

        return nowMs <= existing.LeaseExpiresAtMs + _jeopardyGraceMs;
    }

    public bool CheckSequencer(ChubbySequencer sequencer)
    {
        return _locks.TryGetValue(sequencer.LockPath, out (string Owner, long LeaseExpiresAtMs, long Generation) active) &&
               active.Generation == sequencer.GenerationNumber;
    }
}
