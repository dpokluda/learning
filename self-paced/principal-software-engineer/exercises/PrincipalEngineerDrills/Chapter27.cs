namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter27
{
    [TestMethod]
    public void Mcp_tool_schema_gatekeeper_rejects_mutating_calls_from_readonly_agents_and_catches_hallucinated_args()
    {
        McpToolSchemaGatekeeper queryTool = new McpToolSchemaGatekeeper(
            toolName: "query_stamp_telemetry",
            isMutatingTool: false,
            specs: [new McpToolParameterSpec("lookbackMinutes", Required: true, MinIntValue: 1, MaxIntValue: 120)]);

        McpToolSchemaGatekeeper deleteTool = new McpToolSchemaGatekeeper(
            toolName: "delete_stamp_deployment",
            isMutatingTool: true,
            specs: [new McpToolParameterSpec("deploymentId", Required: true)]);

        // 1. Read-only agent blocked from calling mutating tool:
        (bool mutatingAllowed, string? mutatingErr) = deleteTool.ValidateToolCall(
            new Dictionary<string, int> { ["deploymentId"] = 10 },
            callerIsReadOnlyAgent: true);
        Assert.IsFalse(mutatingAllowed);
        StringAssert.Contains(mutatingErr, "SecurityDenied");

        // 2. Hallucinated parameter rejected:
        (bool hallucinatedAllowed, string? hallucinatedErr) = queryTool.ValidateToolCall(
            new Dictionary<string, int> { ["lookbackMinutes"] = 30, ["inventedParam"] = 1 },
            callerIsReadOnlyAgent: true);
        Assert.IsFalse(hallucinatedAllowed);
        StringAssert.Contains(hallucinatedErr, "HallucinatedParameter");

        // 3. Out-of-bounds parameter rejected with self-correcting guidance:
        (bool unboundedAllowed, string? unboundedErr) = queryTool.ValidateToolCall(
            new Dictionary<string, int> { ["lookbackMinutes"] = 10_000 },
            callerIsReadOnlyAgent: true);
        Assert.IsFalse(unboundedAllowed);
        StringAssert.Contains(unboundedErr, "OutOfBounds");

        // 4. Valid bounded call succeeds:
        (bool validAllowed, _) = queryTool.ValidateToolCall(
            new Dictionary<string, int> { ["lookbackMinutes"] = 60 },
            callerIsReadOnlyAgent: true);
        Assert.IsTrue(validAllowed);
    }

    [TestMethod]
    public void Reciprocal_rank_fusion_ranks_consensus_document_above_single_retriever_outlier()
    {
        // Doc-B is #1 in lexical BM25, but #5 in semantic search (1/61 + 1/65 = 0.03178).
        // Doc-C is #5 in lexical BM25, and #1 in semantic search (1/65 + 1/61 = 0.03178).
        // Doc-A is #2 in BOTH lexical BM25 and semantic search (2/62 = 0.03226) -> Consensus winner!
        string[] lexicalRanking = ["doc-B", "doc-A", "doc-D", "doc-E", "doc-C"];
        string[] semanticRanking = ["doc-C", "doc-A", "doc-D", "doc-E", "doc-B"];

        IReadOnlyList<(string DocId, double RrfScore)> fused = HybridRrfRetriever.FuseRankedLists(
            lexicalRanking,
            semanticRanking,
            k: 60);

        Assert.AreEqual("doc-A", fused[0].DocId);
        Assert.AreEqual(2.0 / 62.0, fused[0].RrfScore, 0.00001);
    }
}

file sealed record McpToolParameterSpec(
    string Name,
    bool Required,
    int? MinIntValue = null,
    int? MaxIntValue = null);

file sealed class McpToolSchemaGatekeeper
{
    private readonly string _toolName;
    private readonly bool _isMutatingTool;
    private readonly Dictionary<string, McpToolParameterSpec> _paramSpecs =
        new Dictionary<string, McpToolParameterSpec>(StringComparer.Ordinal);

    public McpToolSchemaGatekeeper(string toolName, bool isMutatingTool, IEnumerable<McpToolParameterSpec> specs)
    {
        _toolName = toolName;
        _isMutatingTool = isMutatingTool;
        foreach (McpToolParameterSpec spec in specs)
        {
            _paramSpecs[spec.Name] = spec;
        }
    }

    public (bool IsValid, string? ErrorMessage) ValidateToolCall(
        IReadOnlyDictionary<string, int> intArgs,
        bool callerIsReadOnlyAgent)
    {
        if (callerIsReadOnlyAgent && _isMutatingTool)
        {
            return (false, $"SecurityDenied: Tool '{_toolName}' is a mutating tool and cannot be invoked by a read-only agent.");
        }

        foreach (string argName in intArgs.Keys)
        {
            if (!_paramSpecs.ContainsKey(argName))
            {
                return (false, $"HallucinatedParameter: Unknown argument '{argName}' for tool '{_toolName}'.");
            }
        }

        foreach (McpToolParameterSpec spec in _paramSpecs.Values)
        {
            if (!intArgs.TryGetValue(spec.Name, out int val))
            {
                if (spec.Required)
                {
                    return (false, $"MissingRequiredParameter: '{spec.Name}' is required for tool '{_toolName}'.");
                }

                continue;
            }

            if (spec.MinIntValue.HasValue && val < spec.MinIntValue.Value)
            {
                return (false, $"OutOfBounds: '{spec.Name}' ({val}) must be >= {spec.MinIntValue.Value}.");
            }

            if (spec.MaxIntValue.HasValue && val > spec.MaxIntValue.Value)
            {
                return (false, $"OutOfBounds: '{spec.Name}' ({val}) must be <= {spec.MaxIntValue.Value}.");
            }
        }

        return (true, null);
    }
}

file static class HybridRrfRetriever
{
    public static IReadOnlyList<(string DocId, double RrfScore)> FuseRankedLists(
        IReadOnlyList<string> lexicalRankedDocIds,
        IReadOnlyList<string> semanticRankedDocIds,
        int k = 60)
    {
        Dictionary<string, double> scores = new Dictionary<string, double>(StringComparer.Ordinal);

        for (int i = 0; i < lexicalRankedDocIds.Count; i++)
        {
            string docId = lexicalRankedDocIds[i];
            int rank = i + 1;
            scores[docId] = scores.GetValueOrDefault(docId) + (1.0 / (k + rank));
        }

        for (int i = 0; i < semanticRankedDocIds.Count; i++)
        {
            string docId = semanticRankedDocIds[i];
            int rank = i + 1;
            scores[docId] = scores.GetValueOrDefault(docId) + (1.0 / (k + rank));
        }

        return scores
            .Select(static kvp => (DocId: kvp.Key, RrfScore: kvp.Value))
            .OrderByDescending(static item => item.RrfScore)
            .ThenBy(static item => item.DocId, StringComparer.Ordinal)
            .ToList();
    }
}
