namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter01
{
    [TestMethod]
    public void Retry_and_idempotency_deduplication_transform_fair_loss_link_into_reliable_link()
    {
        SimulatedFairLossLink link = new SimulatedFairLossLink(dropEveryNthAttempt: 2, duplicateDeliveredPackets: true);
        ReliableReceiver receiver = new ReliableReceiver();
        ReliableSender sender = new ReliableSender(link, receiver);

        bool deliveredFirst = sender.SendWithRetry(new Message("device-1", SequenceNumber: 1, Payload: "temp=72"), maxAttempts: 5);
        bool deliveredSecond = sender.SendWithRetry(new Message("device-1", SequenceNumber: 2, Payload: "temp=73"), maxAttempts: 5);
        bool duplicateResend = sender.SendWithRetry(new Message("device-1", SequenceNumber: 1, Payload: "temp=72"), maxAttempts: 5);

        Assert.IsTrue(deliveredFirst);
        Assert.IsTrue(deliveredSecond);
        Assert.IsTrue(duplicateResend);
        Assert.HasCount(2, receiver.AppliedPayloads);
        Assert.AreEqual("temp=72", receiver.AppliedPayloads[0]);
        Assert.AreEqual("temp=73", receiver.AppliedPayloads[1]);
    }

    [TestMethod]
    public void Tail_latency_amplification_grows_exponentially_with_parallel_fanout()
    {
        double singleShardSlowProb = 0.01;
        double fanout1Prob = TailLatencyCalculator.ComputeFanoutTailProbability(singleShardSlowProb, parallelShards: 1);
        double fanout50Prob = TailLatencyCalculator.ComputeFanoutTailProbability(singleShardSlowProb, parallelShards: 50);
        double fanout100Prob = TailLatencyCalculator.ComputeFanoutTailProbability(singleShardSlowProb, parallelShards: 100);

        Assert.AreEqual(0.01, fanout1Prob, 0.0001);
        Assert.AreEqual(0.39499, fanout50Prob, 0.001);
        Assert.IsGreaterThan(0.63, fanout100Prob);
    }
}

file sealed record Message(string DeviceId, long SequenceNumber, string Payload);

file sealed class SimulatedFairLossLink
{
    private readonly int _dropEveryNthAttempt;
    private readonly bool _duplicateDeliveredPackets;
    private int _attemptCount;

    public SimulatedFairLossLink(int dropEveryNthAttempt, bool duplicateDeliveredPackets)
    {
        _dropEveryNthAttempt = dropEveryNthAttempt;
        _duplicateDeliveredPackets = duplicateDeliveredPackets;
    }

    public bool Transmit(Message message, ReliableReceiver receiver)
    {
        _attemptCount++;
        if (_dropEveryNthAttempt > 0 && (_attemptCount % _dropEveryNthAttempt) == 0)
        {
            return false;
        }

        receiver.Receive(message);
        if (_duplicateDeliveredPackets)
        {
            receiver.Receive(message);
        }

        return true;
    }
}

file sealed class ReliableReceiver
{
    private readonly HashSet<(string DeviceId, long SequenceNumber)> _processedKeys = new HashSet<(string, long)>();
    private readonly List<string> _appliedPayloads = new List<string>();

    public IReadOnlyList<string> AppliedPayloads
    {
        get { return _appliedPayloads; }
    }

    public void Receive(Message message)
    {
        (string, long) idempotencyKey = (message.DeviceId, message.SequenceNumber);
        if (_processedKeys.Add(idempotencyKey))
        {
            _appliedPayloads.Add(message.Payload);
        }
    }
}

file sealed class ReliableSender
{
    private readonly SimulatedFairLossLink _link;
    private readonly ReliableReceiver _receiver;

    public ReliableSender(SimulatedFairLossLink link, ReliableReceiver receiver)
    {
        _link = link;
        _receiver = receiver;
    }

    public bool SendWithRetry(Message message, int maxAttempts)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (_link.Transmit(message, _receiver))
            {
                return true;
            }
        }

        return false;
    }
}

file static class TailLatencyCalculator
{
    public static double ComputeFanoutTailProbability(double singleShardSlowProbability, int parallelShards)
    {
        double allFastProbability = Math.Pow(1.0 - singleShardSlowProbability, parallelShards);
        return 1.0 - allFastProbability;
    }
}
