namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter14
{
    [TestMethod]
    public void Composite_applicability_expression_evaluates_local_boolean_rules_without_external_service()
    {
        // Rule: Region == "EU" AND (Sku == "Industrial" OR Sku == "Automotive") AND NOT BatteryUnder30
        IApplicabilityRule rule = new AndRule(
            new EqualsRule("Region", "EU"),
            new AndRule(
                new OrRule(new EqualsRule("Sku", "Industrial"), new EqualsRule("Sku", "Automotive")),
                new NotRule(new EqualsRule("BatteryLow", "true"))));

        Dictionary<string, string> eligibleDevice = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Region"] = "EU",
            ["Sku"] = "Industrial",
            ["BatteryLow"] = "false"
        };
        Dictionary<string, string> lowBatteryDevice = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Region"] = "EU",
            ["Sku"] = "Industrial",
            ["BatteryLow"] = "true"
        };

        Assert.IsTrue(rule.IsApplicable(eligibleDevice));
        Assert.IsFalse(rule.IsApplicable(lowBatteryDevice));
    }

    [TestMethod]
    public void Regional_privacy_mapping_table_pseudonymizes_eu_customer_identifiers_before_global_logging()
    {
        RegionalPrivacyMappingTable euVault = new RegionalPrivacyMappingTable();

        Guid opaqueToken1 = euVault.GetOrCreateOpaqueToken("eu-customer-berlin-gmbh");
        Guid opaqueToken2 = euVault.GetOrCreateOpaqueToken("eu-customer-berlin-gmbh");

        Assert.AreEqual(opaqueToken1, opaqueToken2);
        Assert.AreEqual("eu-customer-berlin-gmbh", euVault.ResolveInsideRegion(opaqueToken1));
    }
}

file interface IApplicabilityRule
{
    bool IsApplicable(IReadOnlyDictionary<string, string> deviceAttributes);
}

file sealed class EqualsRule : IApplicabilityRule
{
    private readonly string _key;
    private readonly string _expectedValue;

    public EqualsRule(string key, string expectedValue)
    {
        _key = key;
        _expectedValue = expectedValue;
    }

    public bool IsApplicable(IReadOnlyDictionary<string, string> deviceAttributes)
    {
        return deviceAttributes.TryGetValue(_key, out string? actual) &&
               string.Equals(actual, _expectedValue, StringComparison.Ordinal);
    }
}

file sealed class AndRule : IApplicabilityRule
{
    private readonly IApplicabilityRule _left;
    private readonly IApplicabilityRule _right;

    public AndRule(IApplicabilityRule left, IApplicabilityRule right)
    {
        _left = left;
        _right = right;
    }

    public bool IsApplicable(IReadOnlyDictionary<string, string> deviceAttributes)
    {
        return _left.IsApplicable(deviceAttributes) && _right.IsApplicable(deviceAttributes);
    }
}

file sealed class OrRule : IApplicabilityRule
{
    private readonly IApplicabilityRule _left;
    private readonly IApplicabilityRule _right;

    public OrRule(IApplicabilityRule left, IApplicabilityRule right)
    {
        _left = left;
        _right = right;
    }

    public bool IsApplicable(IReadOnlyDictionary<string, string> deviceAttributes)
    {
        return _left.IsApplicable(deviceAttributes) || _right.IsApplicable(deviceAttributes);
    }
}

file sealed class NotRule : IApplicabilityRule
{
    private readonly IApplicabilityRule _inner;

    public NotRule(IApplicabilityRule inner)
    {
        _inner = inner;
    }

    public bool IsApplicable(IReadOnlyDictionary<string, string> deviceAttributes)
    {
        return !_inner.IsApplicable(deviceAttributes);
    }
}

file sealed class RegionalPrivacyMappingTable
{
    private readonly Dictionary<string, Guid> _cidToToken = new Dictionary<string, Guid>(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _tokenToCid = new Dictionary<Guid, string>();

    public Guid GetOrCreateOpaqueToken(string customerIdentifiableData)
    {
        if (_cidToToken.TryGetValue(customerIdentifiableData, out Guid existing))
        {
            return existing;
        }

        Guid token = Guid.NewGuid();
        _cidToToken[customerIdentifiableData] = token;
        _tokenToCid[token] = customerIdentifiableData;
        return token;
    }

    public string? ResolveInsideRegion(Guid opaqueToken)
    {
        if (_tokenToCid.TryGetValue(opaqueToken, out string? cid))
        {
            return cid;
        }

        return null;
    }
}
