namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter17
{
    [TestMethod]
    public void Kafka_partition_exposes_records_only_up_to_high_water_mark_and_deduplicates_idempotent_producer()
    {
        KafkaPartitionLeader partition = new KafkaPartitionLeader(isrCount: 2);

        long offset0 = partition.ProduceIdempotent(producerId: 99, sequenceNumber: 0, payload: "msg-0");
        long offset0Retry = partition.ProduceIdempotent(producerId: 99, sequenceNumber: 0, payload: "msg-0");
        long offset1 = partition.ProduceIdempotent(producerId: 99, sequenceNumber: 1, payload: "msg-1");

        Assert.AreEqual(0, offset0);
        Assert.AreEqual(0, offset0Retry);
        Assert.AreEqual(1, offset1);

        // Before Follower fetches, High-Water Mark is -1, so consumers see 0 records
        Assert.IsEmpty(partition.FetchConsumerVisible());

        // Follower fetches up to offset 0 -> HWM advances to 0 -> Consumer sees only msg-0
        partition.AckFollowerReplication(followerReplicatedOffset: 0);
        IReadOnlyList<string> visibleAfterFirstAck = partition.FetchConsumerVisible();
        Assert.HasCount(1, visibleAfterFirstAck);
        Assert.AreEqual("msg-0", visibleAfterFirstAck[0]);
    }

    [TestMethod]
    public void Consumer_group_assignor_caps_active_consumers_at_partition_count()
    {
        IReadOnlyDictionary<string, List<int>> assignment =
            KafkaConsumerGroupAssignor.AssignPartitions(partitionCount: 3, consumerIds: ["c1", "c2", "c3", "c4"]);

        Assert.HasCount(1, assignment["c1"]);
        Assert.HasCount(1, assignment["c2"]);
        Assert.HasCount(1, assignment["c3"]);
        Assert.IsEmpty(assignment["c4"]);
    }
}

file sealed class KafkaPartitionLeader
{
    private readonly int _isrCount;
    private readonly List<string> _log = new List<string>();
    private readonly Dictionary<long, int> _lastProducerSeq = new Dictionary<long, int>();
    private readonly Dictionary<(long Pid, int Seq), long> _producerOffsetMap = new Dictionary<(long, int), long>();
    private long _highWaterMark = -1;

    public KafkaPartitionLeader(int isrCount)
    {
        _isrCount = isrCount;
    }

    public long ProduceIdempotent(long producerId, int sequenceNumber, string payload)
    {
        if (_lastProducerSeq.TryGetValue(producerId, out int lastSeq) && sequenceNumber <= lastSeq)
        {
            return _producerOffsetMap[(producerId, sequenceNumber)];
        }

        long assignedOffset = _log.Count;
        _log.Add(payload);
        _lastProducerSeq[producerId] = sequenceNumber;
        _producerOffsetMap[(producerId, sequenceNumber)] = assignedOffset;

        if (_isrCount == 1)
        {
            _highWaterMark = assignedOffset;
        }

        return assignedOffset;
    }

    public void AckFollowerReplication(long followerReplicatedOffset)
    {
        if (followerReplicatedOffset < _log.Count && followerReplicatedOffset > _highWaterMark)
        {
            _highWaterMark = followerReplicatedOffset;
        }
    }

    public IReadOnlyList<string> FetchConsumerVisible()
    {
        List<string> visible = new List<string>();
        for (int i = 0; i <= _highWaterMark && i < _log.Count; i++)
        {
            visible.Add(_log[i]);
        }

        return visible;
    }
}

file static class KafkaConsumerGroupAssignor
{
    public static IReadOnlyDictionary<string, List<int>> AssignPartitions(int partitionCount, ReadOnlySpan<string> consumerIds)
    {
        Dictionary<string, List<int>> map = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (string cid in consumerIds)
        {
            map[cid] = new List<int>();
        }

        for (int p = 0; p < partitionCount; p++)
        {
            string targetConsumer = consumerIds[p % consumerIds.Length];
            map[targetConsumer].Add(p);
        }

        return map;
    }
}
