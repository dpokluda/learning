namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter23
{
    [TestMethod]
    public void Tenant_batch_coalescer_eliminates_per_blob_fanout_flush_amplification()
    {
        TenantBatchCoalescer coalescer = new TenantBatchCoalescer(flushThresholdBytes: 1_000, maxAgeMs: 60_000);

        // Simulate 10 mixed blobs, each containing 50 small tenants (100 bytes per tenant per blob).
        // Naive flushing would perform 10 * 50 = 500 small flushes!
        // Coalescing until 1,000 bytes performs only 50 flushes total (a 10x reduction in I/O calls).
        for (int blobIdx = 0; blobIdx < 10; blobIdx++)
        {
            for (int t = 0; t < 50; t++)
            {
                coalescer.AppendEvent($"tenant-{t}", payloadBytes: 100, nowMs: blobIdx * 1_000);
            }
        }

        Assert.AreEqual(50, coalescer.CompletedFlushCount);
        Assert.AreEqual(50_000L, coalescer.TotalBytesFlushed);
    }

    [TestMethod]
    public void Deficit_round_robin_scheduler_prevents_noisy_mega_tenant_from_starving_small_tenant()
    {
        DeficitRoundRobinScheduler<string> scheduler = new DeficitRoundRobinScheduler<string>();
        scheduler.RegisterTenant("mega-tenant", weightQuantum: 100);
        scheduler.RegisterTenant("small-hospital", weightQuantum: 100);

        for (int i = 0; i < 50; i++)
        {
            scheduler.Enqueue("mega-tenant", $"mega-job-{i}", workCost: 100);
        }

        scheduler.Enqueue("small-hospital", "urgent-patch", workCost: 100);

        Assert.IsTrue(scheduler.TryDequeueNext(out string? firstTenant, out _));
        Assert.AreEqual("mega-tenant", firstTenant);

        // On the very next quantum, small-hospital is served immediately instead of waiting behind 49 mega-tenant jobs!
        Assert.IsTrue(scheduler.TryDequeueNext(out string? secondTenant, out string? secondPayload));
        Assert.AreEqual("small-hospital", secondTenant);
        Assert.AreEqual("urgent-patch", secondPayload);
    }
}

file sealed class TenantBatchCoalescer
{
    private readonly int _flushThresholdBytes;
    private readonly long _maxAgeMs;
    private readonly Dictionary<string, (int Bytes, long FirstSeenMs)> _buffers =
        new Dictionary<string, (int, long)>(StringComparer.Ordinal);

    public int CompletedFlushCount { get; private set; }

    public long TotalBytesFlushed { get; private set; }

    public TenantBatchCoalescer(int flushThresholdBytes, long maxAgeMs)
    {
        _flushThresholdBytes = flushThresholdBytes;
        _maxAgeMs = maxAgeMs;
    }

    public void AppendEvent(string tenantId, int payloadBytes, long nowMs)
    {
        if (!_buffers.TryGetValue(tenantId, out (int Bytes, long FirstSeenMs) current))
        {
            current = (0, nowMs);
        }

        int updatedBytes = current.Bytes + payloadBytes;
        if (updatedBytes >= _flushThresholdBytes || (nowMs - current.FirstSeenMs) >= _maxAgeMs)
        {
            CompletedFlushCount++;
            TotalBytesFlushed += updatedBytes;
            _buffers.Remove(tenantId);
        }
        else
        {
            _buffers[tenantId] = (updatedBytes, current.FirstSeenMs);
        }
    }
}

file sealed class DeficitRoundRobinScheduler<TPayload>
{
    private sealed class TenantQueue
    {
        public string TenantId { get; }
        public int Quantum { get; }
        public int Deficit { get; set; }
        public Queue<(TPayload Item, int Cost)> Items { get; } = new Queue<(TPayload, int)>();

        public TenantQueue(string tenantId, int quantum)
        {
            TenantId = tenantId;
            Quantum = quantum;
        }
    }

    private readonly Dictionary<string, TenantQueue> _queues = new Dictionary<string, TenantQueue>(StringComparer.Ordinal);
    private readonly Queue<TenantQueue> _activeList = new Queue<TenantQueue>();

    public void RegisterTenant(string tenantId, int weightQuantum)
    {
        _queues[tenantId] = new TenantQueue(tenantId, weightQuantum);
    }

    public void Enqueue(string tenantId, TPayload item, int workCost)
    {
        TenantQueue q = _queues[tenantId];
        bool wasEmpty = q.Items.Count == 0;
        q.Items.Enqueue((item, workCost));
        if (wasEmpty)
        {
            _activeList.Enqueue(q);
        }
    }

    public bool TryDequeueNext(out string? tenantId, out TPayload? item)
    {
        int activeCount = _activeList.Count;
        for (int i = 0; i < activeCount; i++)
        {
            TenantQueue current = _activeList.Dequeue();
            current.Deficit += current.Quantum;

            if (current.Items.TryPeek(out (TPayload Item, int Cost) head) && head.Cost <= current.Deficit)
            {
                current.Items.Dequeue();
                current.Deficit -= head.Cost;
                if (current.Items.Count > 0)
                {
                    _activeList.Enqueue(current);
                }
                else
                {
                    current.Deficit = 0;
                }

                tenantId = current.TenantId;
                item = head.Item;
                return true;
            }

            if (current.Items.Count > 0)
            {
                _activeList.Enqueue(current);
            }
            else
            {
                current.Deficit = 0;
            }
        }

        tenantId = null;
        item = default;
        return false;
    }
}
