namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter05
{
    [TestMethod]
    public void Snapshot_isolation_permits_write_skew_when_concurrent_transactions_mutate_disjoint_keys()
    {
        MvccDatabase db = new MvccDatabase();
        db.Seed("doctor:alice", "on-call");
        db.Seed("doctor:bob", "on-call");

        MvccTransaction txAlice = db.BeginTransaction();
        MvccTransaction txBob = db.BeginTransaction();

        int aliceCount = (txAlice.Read("doctor:alice") == "on-call" ? 1 : 0) + (txAlice.Read("doctor:bob") == "on-call" ? 1 : 0);
        int bobCount = (txBob.Read("doctor:alice") == "on-call" ? 1 : 0) + (txBob.Read("doctor:bob") == "on-call" ? 1 : 0);
        Assert.AreEqual(2, aliceCount);
        Assert.AreEqual(2, bobCount);

        txAlice.Write("doctor:alice", "off-call");
        txBob.Write("doctor:bob", "off-call");

        bool aliceCommitted = db.Commit(txAlice, enforceSerializableSsi: false);
        bool bobCommitted = db.Commit(txBob, enforceSerializableSsi: false);

        Assert.IsTrue(aliceCommitted);
        Assert.IsTrue(bobCommitted);
    }

    [TestMethod]
    public void Serializable_snapshot_isolation_aborts_second_committer_on_read_write_antidependency()
    {
        MvccDatabase db = new MvccDatabase();
        db.Seed("doctor:alice", "on-call");
        db.Seed("doctor:bob", "on-call");

        MvccTransaction txAlice = db.BeginTransaction();
        MvccTransaction txBob = db.BeginTransaction();

        _ = txAlice.Read("doctor:alice");
        _ = txAlice.Read("doctor:bob");
        _ = txBob.Read("doctor:alice");
        _ = txBob.Read("doctor:bob");

        txAlice.Write("doctor:alice", "off-call");
        txBob.Write("doctor:bob", "off-call");

        bool aliceCommitted = db.Commit(txAlice, enforceSerializableSsi: true);
        bool bobCommitted = db.Commit(txBob, enforceSerializableSsi: true);

        Assert.IsTrue(aliceCommitted);
        Assert.IsFalse(bobCommitted);
    }
}

file sealed class MvccTransaction
{
    public long SnapshotTxId { get; }

    public HashSet<string> ReadSet { get; } = new HashSet<string>(StringComparer.Ordinal);

    public Dictionary<string, string> WriteSet { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly MvccDatabase _db;

    public MvccTransaction(long snapshotTxId, MvccDatabase db)
    {
        SnapshotTxId = snapshotTxId;
        _db = db;
    }

    public string? Read(string key)
    {
        ReadSet.Add(key);
        if (WriteSet.TryGetValue(key, out string? localVal))
        {
            return localVal;
        }

        return _db.ReadAtSnapshot(key, SnapshotTxId);
    }

    public void Write(string key, string value)
    {
        WriteSet[key] = value;
    }
}

file sealed class MvccDatabase
{
    private readonly Dictionary<string, List<(long CommitTxId, string Value)>> _versions =
        new Dictionary<string, List<(long, string)>>(StringComparer.Ordinal);
    private long _latestCommitTxId;

    public void Seed(string key, string value)
    {
        _latestCommitTxId++;
        _versions[key] = new List<(long, string)> { (_latestCommitTxId, value) };
    }

    public MvccTransaction BeginTransaction()
    {
        return new MvccTransaction(_latestCommitTxId, this);
    }

    public string? ReadAtSnapshot(string key, long snapshotTxId)
    {
        if (!_versions.TryGetValue(key, out List<(long CommitTxId, string Value)>? history))
        {
            return null;
        }

        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].CommitTxId <= snapshotTxId)
            {
                return history[i].Value;
            }
        }

        return null;
    }

    public bool Commit(MvccTransaction tx, bool enforceSerializableSsi)
    {
        foreach (string writtenKey in tx.WriteSet.Keys)
        {
            if (_versions.TryGetValue(writtenKey, out List<(long CommitTxId, string Value)>? history))
            {
                if (history[^1].CommitTxId > tx.SnapshotTxId)
                {
                    return false;
                }
            }
        }

        if (enforceSerializableSsi)
        {
            foreach (string readKey in tx.ReadSet)
            {
                if (_versions.TryGetValue(readKey, out List<(long CommitTxId, string Value)>? history))
                {
                    if (history[^1].CommitTxId > tx.SnapshotTxId)
                    {
                        return false;
                    }
                }
            }
        }

        _latestCommitTxId++;
        foreach ((string key, string val) in tx.WriteSet)
        {
            if (!_versions.TryGetValue(key, out List<(long CommitTxId, string Value)>? history))
            {
                history = new List<(long, string)>();
                _versions[key] = history;
            }

            history.Add((_latestCommitTxId, val));
        }

        return true;
    }
}
