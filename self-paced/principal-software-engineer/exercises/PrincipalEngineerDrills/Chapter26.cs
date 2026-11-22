namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter26
{
    [TestMethod]
    public void Emergency_kill_switch_halts_scan_download_and_pre_install_commit_stages_immediately()
    {
        EmergencyKillSwitchAndRingGate gate = new EmergencyKillSwitchAndRingGate();

        Assert.IsTrue(gate.IsOperationPermitted("pkg-bad-driver", "sas-token-99", RolloutLifecycleStage.ScanDiscovery));
        Assert.IsTrue(gate.IsOperationPermitted("pkg-bad-driver", "sas-token-99", RolloutLifecycleStage.ContentDownload));
        Assert.IsTrue(gate.IsOperationPermitted("pkg-bad-driver", "sas-token-99", RolloutLifecycleStage.PreInstallCommit));

        // Canary ring breaches error rate threshold (2.5% > 0.5% max allowed): auto-triggers kill-switch!
        GateDecision decision = gate.EvaluateRingProgression(
            new RingHealthMetrics(RingIndex: 1, SampleCount: 5_000, ElapsedBakeMinutes: 15, ObservedErrorRate: 0.025, ObservedP99LatencyMs: 40),
            packageId: "pkg-bad-driver",
            valetKeyPrefix: "sas-token-99",
            minRequiredSamples: 10_000,
            minRequiredBakeMinutes: 60,
            maxAllowedErrorRate: 0.005,
            maxAllowedP99LatencyMs: 200);

        Assert.AreEqual(GateDecision.AutoRollbackAndKill, decision);

        // All three client lifecycle stages are now blocked (including devices that already downloaded the file!)
        Assert.IsFalse(gate.IsOperationPermitted("pkg-bad-driver", "sas-token-99", RolloutLifecycleStage.ScanDiscovery));
        Assert.IsFalse(gate.IsOperationPermitted("pkg-bad-driver", "sas-token-99", RolloutLifecycleStage.ContentDownload));
        Assert.IsFalse(gate.IsOperationPermitted("pkg-bad-driver", "sas-token-99", RolloutLifecycleStage.PreInstallCommit));
    }

    [TestMethod]
    public void Canary_ring_gate_holds_until_both_minimum_sample_size_and_bake_time_are_met()
    {
        EmergencyKillSwitchAndRingGate gate = new EmergencyKillSwitchAndRingGate();

        GateDecision earlyDecision = gate.EvaluateRingProgression(
            new RingHealthMetrics(RingIndex: 0, SampleCount: 500, ElapsedBakeMinutes: 120, ObservedErrorRate: 0.001, ObservedP99LatencyMs: 25),
            packageId: "pkg-good",
            valetKeyPrefix: "sas-ok",
            minRequiredSamples: 10_000,
            minRequiredBakeMinutes: 60,
            maxAllowedErrorRate: 0.005,
            maxAllowedP99LatencyMs: 200);

        Assert.AreEqual(GateDecision.HoldForMoreSamples, earlyDecision);

        GateDecision readyDecision = gate.EvaluateRingProgression(
            new RingHealthMetrics(RingIndex: 0, SampleCount: 15_000, ElapsedBakeMinutes: 65, ObservedErrorRate: 0.001, ObservedP99LatencyMs: 25),
            packageId: "pkg-good",
            valetKeyPrefix: "sas-ok",
            minRequiredSamples: 10_000,
            minRequiredBakeMinutes: 60,
            maxAllowedErrorRate: 0.005,
            maxAllowedP99LatencyMs: 200);

        Assert.AreEqual(GateDecision.Proceed, readyDecision);
    }
}

file enum RolloutLifecycleStage : byte
{
    ScanDiscovery = 1,
    ContentDownload = 2,
    PreInstallCommit = 3
}

file enum GateDecision : byte
{
    Proceed = 1,
    HoldForMoreSamples = 2,
    AutoRollbackAndKill = 3
}

file sealed record RingHealthMetrics(
    int RingIndex,
    long SampleCount,
    int ElapsedBakeMinutes,
    double ObservedErrorRate,
    double ObservedP99LatencyMs);

file sealed class EmergencyKillSwitchAndRingGate
{
    private readonly HashSet<string> _killedPackageIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _revokedValetKeyPrefixes = new HashSet<string>(StringComparer.Ordinal);

    public long LastKillSwitchVersion { get; private set; }

    public void TriggerEmergencyKillSwitch(string packageId, string valetKeyPrefix, long killVersion)
    {
        _killedPackageIds.Add(packageId);
        _revokedValetKeyPrefixes.Add(valetKeyPrefix);
        if (killVersion > LastKillSwitchVersion)
        {
            LastKillSwitchVersion = killVersion;
        }
    }

    public bool IsOperationPermitted(string packageId, string? valetKeyPrefix, RolloutLifecycleStage stage)
    {
        if (_killedPackageIds.Contains(packageId))
        {
            return false;
        }

        if (stage == RolloutLifecycleStage.ContentDownload &&
            valetKeyPrefix is not null &&
            _revokedValetKeyPrefixes.Contains(valetKeyPrefix))
        {
            return false;
        }

        return true;
    }

    public GateDecision EvaluateRingProgression(
        RingHealthMetrics metrics,
        string packageId,
        string valetKeyPrefix,
        long minRequiredSamples,
        int minRequiredBakeMinutes,
        double maxAllowedErrorRate,
        double maxAllowedP99LatencyMs)
    {
        if (metrics.SampleCount >= 100 &&
            (metrics.ObservedErrorRate > maxAllowedErrorRate || metrics.ObservedP99LatencyMs > maxAllowedP99LatencyMs))
        {
            TriggerEmergencyKillSwitch(packageId, valetKeyPrefix, LastKillSwitchVersion + 1);
            return GateDecision.AutoRollbackAndKill;
        }

        if (metrics.SampleCount < minRequiredSamples || metrics.ElapsedBakeMinutes < minRequiredBakeMinutes)
        {
            return GateDecision.HoldForMoreSamples;
        }

        return GateDecision.Proceed;
    }
}
