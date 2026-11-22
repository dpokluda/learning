namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter10
{
    [TestMethod]
    public void Power_of_two_random_choices_selects_less_loaded_healthy_backend()
    {
        BackendServer[] servers =
        [
            new BackendServer("srv-0", ActiveConnections: 45, IsHealthy: true),
            new BackendServer("srv-1", ActiveConnections: 3, IsHealthy: true),
            new BackendServer("srv-2", ActiveConnections: 0, IsHealthy: false)
        ];

        BackendServer chosen = PowerOfTwoLoadBalancer.SelectBackend(servers, candidateIndexA: 0, candidateIndexB: 1);
        Assert.AreEqual("srv-1", chosen.ServerId);

        BackendServer fallbackWhenOneUnhealthy = PowerOfTwoLoadBalancer.SelectBackend(servers, candidateIndexA: 0, candidateIndexB: 2);
        Assert.AreEqual("srv-0", fallbackWhenOneUnhealthy.ServerId);
    }

    [TestMethod]
    public void Server_sent_events_stream_replays_missed_events_after_last_event_id()
    {
        SseStreamBuffer stream = new SseStreamBuffer();
        stream.Publish(new SseEvent(EventId: 101, Data: "phase=1%"));
        stream.Publish(new SseEvent(EventId: 102, Data: "phase=10%"));
        stream.Publish(new SseEvent(EventId: 103, Data: "phase=50%"));

        IReadOnlyList<SseEvent> replayed = stream.ResumeFrom(lastEventId: 101);

        Assert.HasCount(2, replayed);
        Assert.AreEqual(102, replayed[0].EventId);
        Assert.AreEqual(103, replayed[1].EventId);
    }
}

file sealed record BackendServer(string ServerId, int ActiveConnections, bool IsHealthy);

file static class PowerOfTwoLoadBalancer
{
    public static BackendServer SelectBackend(ReadOnlySpan<BackendServer> pool, int candidateIndexA, int candidateIndexB)
    {
        BackendServer a = pool[candidateIndexA];
        BackendServer b = pool[candidateIndexB];

        if (a.IsHealthy && !b.IsHealthy)
        {
            return a;
        }

        if (!a.IsHealthy && b.IsHealthy)
        {
            return b;
        }

        return (a.ActiveConnections <= b.ActiveConnections) ? a : b;
    }
}

file readonly record struct SseEvent(long EventId, string Data);

file sealed class SseStreamBuffer
{
    private readonly List<SseEvent> _events = new List<SseEvent>();

    public void Publish(SseEvent evt)
    {
        _events.Add(evt);
    }

    public IReadOnlyList<SseEvent> ResumeFrom(long lastEventId)
    {
        List<SseEvent> missed = new List<SseEvent>();
        foreach (SseEvent evt in _events)
        {
            if (evt.EventId > lastEventId)
            {
                missed.Add(evt);
            }
        }

        return missed;
    }
}
