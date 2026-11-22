namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter28
{
    [TestMethod]
    public void Atlas_canary_cohort_hasher_is_monotonic_as_rollout_phase_expands()
    {
        // Every device included in the 1% canary must remain included when the rollout expands to 10% and 50%!
        int countAt1Pct = 0;
        int countAt10Pct = 0;

        for (int i = 0; i < 10_000; i++)
        {
            string deviceId = $"dev-{i}";
            bool in1 = AtlasRolloutPlane.IsDeviceInCanaryBucket("rollout-2026-11", deviceId, targetPercent: 1);
            bool in10 = AtlasRolloutPlane.IsDeviceInCanaryBucket("rollout-2026-11", deviceId, targetPercent: 10);
            bool in50 = AtlasRolloutPlane.IsDeviceInCanaryBucket("rollout-2026-11", deviceId, targetPercent: 50);

            if (in1)
            {
                countAt1Pct++;
                Assert.IsTrue(in10, "A device in the 1% cohort must remain in the 10% cohort.");
            }

            if (in10)
            {
                countAt10Pct++;
                Assert.IsTrue(in50, "A device in the 10% cohort must remain in the 50% cohort.");
            }
        }

        Assert.IsTrue(countAt1Pct is > 50 and < 150);
        Assert.IsTrue(countAt10Pct is > 850 and < 1_150);
    }

    [TestMethod]
    public void Priority_load_shedder_preserves_p0_rollback_traffic_when_host_utilization_is_critical()
    {
        Assert.IsTrue(AtlasRolloutPlane.ShouldAdmitRequest(priorityTier: 0, hostUtilization: 0.95));
        Assert.IsFalse(AtlasRolloutPlane.ShouldAdmitRequest(priorityTier: 2, hostUtilization: 0.85));
        Assert.IsTrue(AtlasRolloutPlane.ShouldAdmitRequest(priorityTier: 2, hostUtilization: 0.60));
    }
}

file static class AtlasRolloutPlane
{
    public static bool IsDeviceInCanaryBucket(string rolloutId, string deviceId, int targetPercent)
    {
        if (targetPercent <= 0)
        {
            return false;
        }

        if (targetPercent >= 100)
        {
            return true;
        }

        uint hash = Hash32($"{rolloutId}:{deviceId}");
        int bucket = (int)(hash % 100u);
        return bucket < targetPercent;
    }

    private static uint Hash32(string text)
    {
        uint h = 2166136261u;
        foreach (char c in text)
        {
            h ^= c;
            h *= 16777619u;
        }

        h ^= h >> 16;
        h *= 0x85ebca6bu;
        h ^= h >> 13;
        h *= 0xc2b2ae35u;
        h ^= h >> 16;
        return h;
    }

    public static bool ShouldAdmitRequest(int priorityTier, double hostUtilization)
    {
        return priorityTier switch
        {
            0 => hostUtilization < 0.99,
            1 => hostUtilization < 0.90,
            _ => hostUtilization < 0.80
        };
    }
}
