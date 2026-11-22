namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter13
{
    [TestMethod]
    public void Pattern_matched_router_selects_most_specific_microservice_rule_without_hardcoded_addresses()
    {
        PatternMatchingRouter router = new PatternMatchingRouter();
        router.Register(new Dictionary<string, string> { ["role"] = "rollout", ["cmd"] = "evaluate" }, "DefaultRolloutEngine");
        router.Register(
            new Dictionary<string, string> { ["role"] = "rollout", ["cmd"] = "evaluate", ["region"] = "eu", ["tier"] = "critical" },
            "EuCriticalRolloutEngine");

        string? standardMatch = router.Resolve(new Dictionary<string, string>
        {
            ["role"] = "rollout",
            ["cmd"] = "evaluate",
            ["region"] = "us"
        });
        string? euCriticalMatch = router.Resolve(new Dictionary<string, string>
        {
            ["role"] = "rollout",
            ["cmd"] = "evaluate",
            ["region"] = "eu",
            ["tier"] = "critical",
            ["payload"] = "fw-4.2"
        });

        Assert.AreEqual("DefaultRolloutEngine", standardMatch);
        Assert.AreEqual("EuCriticalRolloutEngine", euCriticalMatch);
    }

    [TestMethod]
    public void Transactional_outbox_and_idempotent_inbox_deliver_exactly_once_side_effects_under_duplicate_relay()
    {
        OutboxDatabase db = new OutboxDatabase();
        IdempotentConsumerInbox consumer = new IdempotentConsumerInbox();

        db.SaveRolloutWithOutbox("rollout-1", "evt-100", "Activate:rollout-1");

        // Relay publishes outbox message twice (simulating crashed relay retry before marking published)
        foreach ((string eventId, string payload) in db.GetUnpublishedOutboxEvents())
        {
            consumer.Process(eventId, payload);
            consumer.Process(eventId, payload);
            db.MarkOutboxPublished(eventId);
        }

        Assert.IsEmpty(db.GetUnpublishedOutboxEvents());
        Assert.HasCount(1, consumer.AppliedEvents);
        Assert.AreEqual("Activate:rollout-1", consumer.AppliedEvents[0]);
    }

    [TestMethod]
    public void Saga_orchestrator_runs_compensating_transactions_in_reverse_order_when_step_fails()
    {
        List<string> auditTrail = new List<string>();
        SagaStep[] steps =
        [
            new SagaStep(
                Execute: () => { auditTrail.Add("Do:ReserveBudget"); return true; },
                Compensate: () => auditTrail.Add("Undo:ReleaseBudget")),
            new SagaStep(
                Execute: () => { auditTrail.Add("Do:SignFirmware"); return true; },
                Compensate: () => auditTrail.Add("Undo:RevokeSignature")),
            new SagaStep(
                Execute: () => { auditTrail.Add("Fail:ProvisionCdn"); return false; },
                Compensate: () => auditTrail.Add("Undo:ProvisionCdn"))
        ];

        bool sagaSucceeded = SagaOrchestrator.Run(steps);

        Assert.IsFalse(sagaSucceeded);
        CollectionAssert.AreEqual(
            new[] { "Do:ReserveBudget", "Do:SignFirmware", "Fail:ProvisionCdn", "Undo:RevokeSignature", "Undo:ReleaseBudget" },
            auditTrail);
    }
}

file sealed class PatternMatchingRouter
{
    private readonly List<(Dictionary<string, string> Pattern, string TargetService)> _rules =
        new List<(Dictionary<string, string>, string)>();

    public void Register(Dictionary<string, string> pattern, string targetService)
    {
        _rules.Add((pattern, targetService));
    }

    public string? Resolve(IReadOnlyDictionary<string, string> message)
    {
        string? bestTarget = null;
        int bestSpecificity = -1;

        foreach ((Dictionary<string, string> pattern, string target) in _rules)
        {
            bool allMatch = true;
            foreach ((string k, string v) in pattern)
            {
                if (!message.TryGetValue(k, out string? msgVal) || !string.Equals(v, msgVal, StringComparison.Ordinal))
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch && pattern.Count > bestSpecificity)
            {
                bestSpecificity = pattern.Count;
                bestTarget = target;
            }
        }

        return bestTarget;
    }
}

file sealed class OutboxDatabase
{
    private readonly Dictionary<string, string> _rollouts = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly List<(string EventId, string Payload, bool Published)> _outbox = new List<(string, string, bool)>();

    public void SaveRolloutWithOutbox(string rolloutId, string eventId, string eventPayload)
    {
        _rollouts[rolloutId] = "Active";
        _outbox.Add((eventId, eventPayload, Published: false));
    }

    public IReadOnlyList<(string EventId, string Payload)> GetUnpublishedOutboxEvents()
    {
        List<(string, string)> pending = new List<(string, string)>();
        foreach ((string id, string payload, bool published) in _outbox)
        {
            if (!published)
            {
                pending.Add((id, payload));
            }
        }

        return pending;
    }

    public void MarkOutboxPublished(string eventId)
    {
        for (int i = 0; i < _outbox.Count; i++)
        {
            if (_outbox[i].EventId == eventId)
            {
                _outbox[i] = (_outbox[i].EventId, _outbox[i].Payload, true);
            }
        }
    }
}

file sealed class IdempotentConsumerInbox
{
    private readonly HashSet<string> _processedIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> _appliedEvents = new List<string>();

    public IReadOnlyList<string> AppliedEvents
    {
        get { return _appliedEvents; }
    }

    public void Process(string eventId, string payload)
    {
        if (_processedIds.Add(eventId))
        {
            _appliedEvents.Add(payload);
        }
    }
}

file sealed record SagaStep(Func<bool> Execute, Action Compensate);

file static class SagaOrchestrator
{
    public static bool Run(ReadOnlySpan<SagaStep> steps)
    {
        int completedCount = 0;
        for (int i = 0; i < steps.Length; i++)
        {
            if (!steps[i].Execute())
            {
                for (int j = completedCount - 1; j >= 0; j--)
                {
                    steps[j].Compensate();
                }

                return false;
            }

            completedCount++;
        }

        return true;
    }
}
