namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter25
{
    [TestMethod]
    public void Inverted_index_and_bloom_filter_prune_catalog_and_multi_criteria_ranker_selects_best_driver()
    {
        HighSpeedApplicabilityEngine engine = new HighSpeedApplicabilityEngine();

        // Populate 1,000 unrelated catalog rules + 3 candidate drivers for hardware symbol 42:
        List<CatalogDriverRule> catalog = new List<CatalogDriverRule>();
        for (int i = 100; i < 1_100; i++)
        {
            catalog.Add(new CatalogDriverRule(
                DriverId: $"unrelated-{i}",
                RequiredHardwareSymbolId: i,
                RuleAst: new AttributeCheckNode(AttributeSymbolId: 1, ComparisonOp.GreaterThanOrEqual, TargetValue: 26100),
                FeatureScore: 1,
                HardwareRank: 0,
                DriverVersionSortable: 100));
        }

        // Candidate 1: Generic inbox driver (FeatureScore = 10)
        catalog.Add(new CatalogDriverRule(
            DriverId: "drv-generic",
            RequiredHardwareSymbolId: 42,
            RuleAst: new AttributeCheckNode(AttributeSymbolId: 1, ComparisonOp.GreaterThanOrEqual, TargetValue: 22000),
            FeatureScore: 10,
            HardwareRank: 2,
            DriverVersionSortable: 500));

        // Candidate 2: OEM certified driver v1 (FeatureScore = 1, Exact HardwareRank = 0, Version = 100)
        catalog.Add(new CatalogDriverRule(
            DriverId: "drv-oem-v1",
            RequiredHardwareSymbolId: 42,
            RuleAst: new LogicalAndNode(
            [
                new AttributeCheckNode(AttributeSymbolId: 1, ComparisonOp.GreaterThanOrEqual, TargetValue: 22000),
                new AttributeCheckNode(AttributeSymbolId: 2, ComparisonOp.Equals, TargetValue: 64)
            ]),
            FeatureScore: 1,
            HardwareRank: 0,
            DriverVersionSortable: 100));

        // Candidate 3: OEM certified driver v2 (FeatureScore = 1, Exact HardwareRank = 0, Version = 200 -> WINNER!)
        catalog.Add(new CatalogDriverRule(
            DriverId: "drv-oem-v2",
            RequiredHardwareSymbolId: 42,
            RuleAst: new LogicalAndNode(
            [
                new AttributeCheckNode(AttributeSymbolId: 1, ComparisonOp.GreaterThanOrEqual, TargetValue: 22000),
                new AttributeCheckNode(AttributeSymbolId: 2, ComparisonOp.Equals, TargetValue: 64)
            ]),
            FeatureScore: 1,
            HardwareRank: 0,
            DriverVersionSortable: 200));

        engine.IndexCatalog(catalog);

        Dictionary<int, int> deviceAttrs = new Dictionary<int, int>
        {
            [1] = 26100, // OsBuild
            [2] = 64     // ArchBits
        };

        CatalogDriverRule? best = engine.EvaluateAndSelectBestDriver(
            deviceHardwareSymbols: [42, 9999],
            deviceAttributes: deviceAttrs,
            out int evaluatedCount);

        Assert.IsNotNull(best);
        Assert.AreEqual("drv-oem-v2", best.DriverId);
        // Only the 3 rules matching hardware symbol 42 were evaluated out of 1,003 rules!
        Assert.AreEqual(3, evaluatedCount);
    }
}

file enum ComparisonOp : byte
{
    Equals = 1,
    GreaterThanOrEqual = 2
}

file abstract record ApplicabilityNode;

file sealed record AttributeCheckNode(int AttributeSymbolId, ComparisonOp Op, int TargetValue) : ApplicabilityNode;

file sealed record LogicalAndNode(IReadOnlyList<ApplicabilityNode> Children) : ApplicabilityNode;

file sealed record CatalogDriverRule(
    string DriverId,
    int RequiredHardwareSymbolId,
    ApplicabilityNode RuleAst,
    int FeatureScore,
    int HardwareRank,
    long DriverVersionSortable);

file sealed class HighSpeedApplicabilityEngine
{
    private readonly HashSet<int> _hardwareBloomFilter = new HashSet<int>();
    private readonly Dictionary<int, List<CatalogDriverRule>> _invertedHardwareIndex =
        new Dictionary<int, List<CatalogDriverRule>>();

    public void IndexCatalog(IEnumerable<CatalogDriverRule> catalogRules)
    {
        foreach (CatalogDriverRule rule in catalogRules)
        {
            _hardwareBloomFilter.Add(rule.RequiredHardwareSymbolId);
            if (!_invertedHardwareIndex.TryGetValue(rule.RequiredHardwareSymbolId, out List<CatalogDriverRule>? list))
            {
                list = new List<CatalogDriverRule>();
                _invertedHardwareIndex[rule.RequiredHardwareSymbolId] = list;
            }

            list.Add(rule);
        }
    }

    public CatalogDriverRule? EvaluateAndSelectBestDriver(
        ReadOnlySpan<int> deviceHardwareSymbols,
        IReadOnlyDictionary<int, int> deviceAttributes,
        out int rulesEvaluatedCount)
    {
        rulesEvaluatedCount = 0;
        CatalogDriverRule? bestMatch = null;

        foreach (int hwSymbol in deviceHardwareSymbols)
        {
            if (!_hardwareBloomFilter.Contains(hwSymbol))
            {
                continue;
            }

            if (!_invertedHardwareIndex.TryGetValue(hwSymbol, out List<CatalogDriverRule>? candidates))
            {
                continue;
            }

            foreach (CatalogDriverRule candidate in candidates)
            {
                rulesEvaluatedCount++;
                if (!EvaluateAst(candidate.RuleAst, deviceAttributes))
                {
                    continue;
                }

                if (bestMatch is null || IsBetterCandidate(candidate, bestMatch))
                {
                    bestMatch = candidate;
                }
            }
        }

        return bestMatch;
    }

    private static bool EvaluateAst(ApplicabilityNode node, IReadOnlyDictionary<int, int> attrs)
    {
        switch (node)
        {
            case AttributeCheckNode check:
                if (!attrs.TryGetValue(check.AttributeSymbolId, out int actual))
                {
                    return false;
                }

                return check.Op switch
                {
                    ComparisonOp.Equals => actual == check.TargetValue,
                    ComparisonOp.GreaterThanOrEqual => actual >= check.TargetValue,
                    _ => false
                };

            case LogicalAndNode andNode:
                foreach (ApplicabilityNode child in andNode.Children)
                {
                    if (!EvaluateAst(child, attrs))
                    {
                        return false;
                    }
                }

                return true;

            default:
                return false;
        }
    }

    private static bool IsBetterCandidate(CatalogDriverRule candidate, CatalogDriverRule currentBest)
    {
        if (candidate.FeatureScore != currentBest.FeatureScore)
        {
            return candidate.FeatureScore < currentBest.FeatureScore;
        }

        if (candidate.HardwareRank != currentBest.HardwareRank)
        {
            return candidate.HardwareRank < currentBest.HardwareRank;
        }

        return candidate.DriverVersionSortable > currentBest.DriverVersionSortable;
    }
}
