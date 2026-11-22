namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter18
{
    [TestMethod]
    public void Littles_law_and_headroom_formulas_size_concurrency_servers_and_multi_year_storage()
    {
        // 1. Little's Law: 5,000 RPS * 0.020s (20ms) = 100 in-flight requests
        double inFlight = CapacityMath.ComputeLittlesLawInFlight(arrivalRateRps: 5_000, latencySeconds: 0.020);
        Assert.AreEqual(100.0, inFlight, 0.001);

        // 2. Server sizing at 200ms request time using 30,000 / T_ms rule = 150 RPS per server -> 10 servers for 1,500 RPS
        int servers = CapacityMath.ComputeRequiredServers(targetPeakRps: 1_500, processingTimeMs: 200.0);
        Assert.AreEqual(10, servers);

        // 3. 3-year storage sizing: 2,000 writes/sec * 2,000 bytes * 10^8 sec * 3 replicas = 1.2 PB (1,200 TB -> 1,200 1TB shards)
        long totalBytes = CapacityMath.ComputeThreeYearReplicatedStorageBytes(
            writesPerSecond: 2_000,
            bytesPerWrite: 2_000,
            replicationFactor: 3);
        Assert.AreEqual(1_200_000_000_000_000L, totalBytes);
    }
}

file static class CapacityMath
{
    public static double ComputeLittlesLawInFlight(double arrivalRateRps, double latencySeconds)
    {
        return arrivalRateRps * latencySeconds;
    }

    public static int ComputeRequiredServers(double targetPeakRps, double processingTimeMs)
    {
        double safeRpsPerServer = 30_000.0 / processingTimeMs;
        return (int)Math.Ceiling(targetPeakRps / safeRpsPerServer);
    }

    public static long ComputeThreeYearReplicatedStorageBytes(long writesPerSecond, long bytesPerWrite, int replicationFactor)
    {
        const long ThreeYearsSecondsApprox = 100_000_000L;
        return writesPerSecond * bytesPerWrite * ThreeYearsSecondsApprox * replicationFactor;
    }
}
