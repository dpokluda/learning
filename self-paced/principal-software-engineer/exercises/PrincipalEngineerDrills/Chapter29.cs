namespace PrincipalEngineerDrills;

[TestClass]
public sealed class Chapter29
{
    [TestMethod]
    public void Tier_dependency_validator_rejects_synchronous_hot_path_calls_to_lower_tier_services()
    {
        ServiceDependencyRule[] dependencies =
        [
            new ServiceDependencyRule(CallerTier: 1, TargetTier: 1, IsSynchronousHotPath: true),
            new ServiceDependencyRule(CallerTier: 1, TargetTier: 2, IsSynchronousHotPath: false),
            new ServiceDependencyRule(CallerTier: 1, TargetTier: 2, IsSynchronousHotPath: true)
        ];

        Assert.IsTrue(PrincipalArchitectureGovernance.IsValidDependency(dependencies[0]));
        Assert.IsTrue(PrincipalArchitectureGovernance.IsValidDependency(dependencies[1]));
        Assert.IsFalse(PrincipalArchitectureGovernance.IsValidDependency(dependencies[2]));
    }

    [TestMethod]
    public void Convergence_roi_evaluator_recommends_cancelling_migration_when_payback_exceeds_horizon()
    {
        ConvergenceProposal badConvergence = new ConvergenceProposal(
            EstimatedEngineeringMonths: 240,
            MonthlyInfraSavingsDollars: 5_000,
            MonthlyEngineeringCostDollars: 20_000,
            Tier1RegressionRiskScore: 0.80);

        Assert.IsFalse(PrincipalArchitectureGovernance.ShouldProceedWithConvergence(badConvergence, maxPaybackMonths: 24));
    }
}

file readonly record struct ServiceDependencyRule(int CallerTier, int TargetTier, bool IsSynchronousHotPath);

file readonly record struct ConvergenceProposal(
    int EstimatedEngineeringMonths,
    double MonthlyInfraSavingsDollars,
    double MonthlyEngineeringCostDollars,
    double Tier1RegressionRiskScore);

file static class PrincipalArchitectureGovernance
{
    public static bool IsValidDependency(ServiceDependencyRule rule)
    {
        if (rule.IsSynchronousHotPath && rule.TargetTier > rule.CallerTier)
        {
            return false;
        }

        return true;
    }

    public static bool ShouldProceedWithConvergence(ConvergenceProposal proposal, int maxPaybackMonths)
    {
        if (proposal.Tier1RegressionRiskScore > 0.50 || proposal.MonthlyInfraSavingsDollars <= 0)
        {
            return false;
        }

        double totalInvestment = proposal.EstimatedEngineeringMonths * proposal.MonthlyEngineeringCostDollars;
        double paybackMonths = totalInvestment / proposal.MonthlyInfraSavingsDollars;
        return paybackMonths <= maxPaybackMonths;
    }
}
