namespace Acb.Manifest;

/// <summary>
/// A weighted tally suitable for the unlock signal — spec §5.1.
/// </summary>
public sealed record Tally(
    double ApproveWeight,
    double RejectWeight,
    double AbstainWeight
);

/// <summary>
/// Pricing model — spec §4 and §5. Computes disagreement magnitude, selects
/// the appropriate routine, and computes the cheap- or expensive-routine draw.
///
/// The unlock signal is computed mechanically from the initial tally so that
/// agents cannot manufacture disagreement to drive up their own pay. This is
/// the load-bearing piece of ACB and the place where the brain analogy is
/// enforced by the protocol rather than by agent honesty.
/// </summary>
public static class PricingCalculator
{
    /// <summary>
    /// Compute disagreement magnitude from a weighted tally. Spec §5.1.
    ///
    /// magnitude = 1 − |approve − reject| / (approve + reject)
    ///
    /// If non_abstaining_weight is 0 (everyone abstained) magnitude is 1.0 —
    /// total abstention is treated as maximal disagreement because the
    /// cheap routine has failed to find anyone willing to commit.
    /// </summary>
    public static double ComputeDisagreementMagnitude(Tally tally)
    {
        var nonAbstaining = tally.ApproveWeight + tally.RejectWeight;
        if (nonAbstaining == 0) return 1.0;
        return 1.0 - Math.Abs(tally.ApproveWeight - tally.RejectWeight) / nonAbstaining;
    }

    /// <summary>
    /// Decide which routine applies. Spec §4.1 / §4.2 / §5.2.
    ///
    /// Cheap routine MUST apply when ALL of:
    ///  - <paramref name="roundCount"/> == 0
    ///  - disagreement magnitude on initial tally is &lt; <c>pricing.UnlockThreshold</c>
    ///  - termination is <c>Converged</c>
    ///
    /// Expensive routine MUST apply when ANY of:
    ///  - disagreement magnitude on initial tally is ≥ <c>pricing.UnlockThreshold</c>
    ///  - <paramref name="roundCount"/> &gt; 0
    ///  - termination is <c>PartialCommit</c> or <c>Deadlocked</c>
    /// </summary>
    public static Routine SelectRoutine(
        PricingProfile pricing,
        Tally initialTally,
        int roundCount,
        TerminationState termination)
    {
        if (roundCount > 0) return Routine.Expensive;
        if (termination != TerminationState.Converged) return Routine.Expensive;
        var magnitude = ComputeDisagreementMagnitude(initialTally);
        if (magnitude >= pricing.UnlockThreshold) return Routine.Expensive;
        return Routine.Cheap;
    }

    /// <summary>
    /// Cheap-routine draw. Spec §4.1.
    ///
    ///     draw = CheapRoutineRate × participantCount × (1 − habitDiscount)
    /// </summary>
    public static double ComputeCheapDraw(
        PricingProfile pricing,
        int participantCount,
        double habitDiscount = 0.0)
        => pricing.CheapRoutineRate * participantCount * (1.0 - habitDiscount);

    /// <summary>
    /// Expensive-routine draw. Spec §4.2.
    ///
    ///     draw = ExpensiveRoutineRate × participantCount
    ///          × RoundMultiplier^roundCount
    ///          × (1 − habitDiscount)
    ///
    /// The exponential round multiplier reflects that each additional
    /// belief-update round addresses, by selection, the disagreement the
    /// prior round failed to resolve — the remaining work is harder.
    /// </summary>
    public static double ComputeExpensiveDraw(
        PricingProfile pricing,
        int participantCount,
        int roundCount,
        double habitDiscount = 0.0)
    {
        var basePool = pricing.ExpensiveRoutineRate * participantCount;
        return basePool * Math.Pow(pricing.RoundMultiplier, roundCount) * (1.0 - habitDiscount);
    }

    /// <summary>
    /// Convenience wrapper around the two routine helpers.
    /// </summary>
    public static double ComputeDraw(
        PricingProfile pricing,
        Routine routine,
        int participantCount,
        int roundCount,
        double habitDiscount = 0.0)
        => routine == Routine.Cheap
            ? ComputeCheapDraw(pricing, participantCount, habitDiscount)
            : ComputeExpensiveDraw(pricing, participantCount, roundCount, habitDiscount);
}
