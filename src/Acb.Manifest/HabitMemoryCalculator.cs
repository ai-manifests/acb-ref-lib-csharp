namespace Acb.Manifest;

/// <summary>
/// A prior deliberation considered by the habit-memory function. Implementations
/// supply their own similarity function (string match, embedding distance,
/// structured action match) and provide per-prior similarity scores in [0, 1].
/// </summary>
public sealed record HistoricalDeliberation(
    double Similarity,
    bool SuccessfulOutcome
);

/// <summary>
/// Habit memory discount — spec §7.
///
/// The default-v0 discount function is:
///
///     habitDiscount(d) = min(0.80, similarity(d, history) × stability(history))
///
/// A 100% discount would drive familiar decisions to zero cost and remove the
/// federation's incentive to keep checking, which is the analogue of the
/// brain's continued (cheap but non-zero) attention to habitual stimuli.
/// </summary>
public static class HabitMemoryCalculator
{
    public const double MaxHabitDiscount = 0.80;

    /// <summary>
    /// Compute the habit discount from a list of similar prior deliberations.
    /// Returns a value in [0, MaxHabitDiscount].
    /// </summary>
    public static double ComputeHabitDiscount(IReadOnlyList<HistoricalDeliberation> history)
    {
        if (history.Count == 0) return 0.0;

        double weightSum = 0;
        double weightedSuccess = 0;
        double maxSimilarity = 0;
        foreach (var h in history)
        {
            weightSum += h.Similarity;
            if (h.SuccessfulOutcome) weightedSuccess += h.Similarity;
            if (h.Similarity > maxSimilarity) maxSimilarity = h.Similarity;
        }

        if (weightSum == 0) return 0.0;

        // Stability is the success fraction weighted by similarity — a prior
        // that is barely similar contributes proportionally less.
        var stability = weightedSuccess / weightSum;

        var raw = maxSimilarity * stability;
        return Math.Min(MaxHabitDiscount, raw);
    }
}
