using System.Collections.Immutable;

namespace Acb.Manifest;

/// <summary>
/// Per-agent contribution inputs derived by the deliberation runner from the
/// journal. The settlement calculator consumes these to distribute the
/// epistemic share of a draw under the default-v0 profile.
/// </summary>
public sealed record ParticipantContribution(
    string AgentId,
    bool Participated,
    int AcknowledgedFalsifications,
    bool LoadBearing,
    double? OutcomeBrierDelta,
    bool DissentQualityFlagged
);

/// <summary>
/// A reported substrate-cycle accounting for one provider. ACB v0 does not
/// specify how providers report cycles — implementations may use whatever
/// off-protocol agreement they have with the agent operator.
/// </summary>
public sealed record SubstrateReport(
    string Recipient,
    double Cycles,
    string? ReportRef = null
);

/// <summary>
/// Inputs to building a <c>SettlementRecorded</c> entry.
/// </summary>
public sealed record SettlementInputs(
    string EntryId,
    string DeliberationId,
    DateTimeOffset Timestamp,
    string? PriorEntryHash,
    string BudgetId,
    double AmountTotal,
    double DrawTotal,
    SettlementProfileConfig Settlement,
    IReadOnlyList<ParticipantContribution> Contributions,
    IReadOnlyList<SubstrateReport> SubstrateReports,
    double HabitDiscountApplied,
    bool UnlockTriggered,
    double DisagreementMagnitudeInitial,
    string? OutcomeReferenced,
    string Signature
);

/// <summary>
/// Settlement — spec §6. Distributes a draw across substrate providers and
/// agent identities by their journal-evidenced contribution.
///
/// The default-v0 profile uses four equal-weight epistemic bonus categories:
///   - base_share         (25%)  — equal across all participants
///   - falsification_bonus (25%) — proportional to acknowledged falsifications
///   - load_bearing_bonus  (25%) — equal across load-bearing voters
///   - outcome_correctness_bonus (25%) — inverse Brier delta when outcome known
///
/// Plus a dissent_quality_penalty that subtracts up to 25% of a flagged
/// agent's pre-penalty total and redistributes it to non-flagged agents.
/// </summary>
public static class SettlementCalculator
{
    /// <summary>
    /// Distribute the substrate share proportional to reported cycles. Spec §6.3.
    /// </summary>
    public static ImmutableList<SubstrateDistribution> DistributeSubstrate(
        double pool,
        IReadOnlyList<SubstrateReport> reports)
    {
        if (reports.Count == 0) return ImmutableList<SubstrateDistribution>.Empty;
        var totalCycles = reports.Sum(r => r.Cycles);
        if (totalCycles == 0) return ImmutableList<SubstrateDistribution>.Empty;

        return reports
            .Select(r => new SubstrateDistribution(
                Recipient: r.Recipient,
                Amount: Round2(pool * r.Cycles / totalCycles),
                Basis: "cycles",
                ReportRef: r.ReportRef))
            .ToImmutableList();
    }

    /// <summary>
    /// Distribute the epistemic share by `default-v0` contribution scoring.
    /// </summary>
    public static ImmutableList<EpistemicDistribution> DistributeEpistemic(
        double pool,
        IReadOnlyList<ParticipantContribution> contributions)
    {
        var participants = contributions.Where(c => c.Participated).ToList();
        if (participants.Count == 0) return ImmutableList<EpistemicDistribution>.Empty;

        var perBonus = pool / 4.0;
        var equalShare = perBonus / participants.Count;

        // Base share — equal across all participants
        var baseShare = equalShare;

        // Falsification bonus — proportional to acknowledged falsifications.
        // If nobody acknowledged any falsification, the pool distributes
        // equally so its share is not lost.
        var totalFalsifications = participants.Sum(c => c.AcknowledgedFalsifications);
        double FalsificationFor(ParticipantContribution c) =>
            totalFalsifications == 0
                ? equalShare
                : perBonus * c.AcknowledgedFalsifications / totalFalsifications;

        // Load-bearing bonus — equal across load-bearing agents. If nobody
        // is load-bearing, the pool distributes equally across all
        // participants.
        var loadBearingCount = participants.Count(c => c.LoadBearing);
        double LoadBearingFor(ParticipantContribution c)
        {
            if (loadBearingCount == 0) return equalShare;
            return c.LoadBearing ? perBonus / loadBearingCount : 0;
        }

        // Outcome correctness bonus — inverse Brier delta, normalized. If
        // no outcomes are reported, the pool distributes equally.
        var withOutcomes = participants.Where(c => c.OutcomeBrierDelta.HasValue).ToList();
        var totalInverse = withOutcomes.Sum(c => 1.0 - c.OutcomeBrierDelta!.Value);
        double OutcomeFor(ParticipantContribution c)
        {
            if (withOutcomes.Count == 0 || totalInverse == 0) return equalShare;
            if (!c.OutcomeBrierDelta.HasValue) return 0;
            return perBonus * (1.0 - c.OutcomeBrierDelta.Value) / totalInverse;
        }

        var preRecords = participants.Select(c =>
        {
            var breakdown = new ContributionBreakdown(
                BaseShare: baseShare,
                FalsificationBonus: FalsificationFor(c),
                LoadBearingBonus: LoadBearingFor(c),
                OutcomeCorrectnessBonus: OutcomeFor(c),
                DissentQualityPenalty: 0
            );
            var preTotal =
                breakdown.BaseShare
                + breakdown.FalsificationBonus
                + breakdown.LoadBearingBonus
                + breakdown.OutcomeCorrectnessBonus;
            return new
            {
                Agent = c.AgentId,
                Breakdown = breakdown,
                PreTotal = preTotal,
                Flagged = c.DissentQualityFlagged,
            };
        }).ToList();

        // Apply dissent quality penalty — up to 25% of pre-total, redistributed
        double flaggedRecovered = 0;
        for (int i = 0; i < preRecords.Count; i++)
        {
            if (preRecords[i].Flagged)
            {
                var penalty = preRecords[i].PreTotal * 0.25;
                preRecords[i] = preRecords[i] with
                {
                    Breakdown = preRecords[i].Breakdown with { DissentQualityPenalty = penalty }
                };
                flaggedRecovered += penalty;
            }
        }

        if (flaggedRecovered > 0)
        {
            var nonFlagged = preRecords.Where(r => !r.Flagged).ToList();
            var nonFlaggedTotal = nonFlagged.Sum(r => r.PreTotal);
            if (nonFlaggedTotal > 0)
            {
                for (int i = 0; i < preRecords.Count; i++)
                {
                    if (!preRecords[i].Flagged)
                    {
                        var share = flaggedRecovered * preRecords[i].PreTotal / nonFlaggedTotal;
                        preRecords[i] = preRecords[i] with
                        {
                            Breakdown = preRecords[i].Breakdown with
                            {
                                BaseShare = preRecords[i].Breakdown.BaseShare + share
                            }
                        };
                    }
                }
            }
        }

        return preRecords.Select(r =>
        {
            var b = r.Breakdown;
            var amount =
                b.BaseShare
                + b.FalsificationBonus
                + b.LoadBearingBonus
                + b.OutcomeCorrectnessBonus
                - b.DissentQualityPenalty;
            return new EpistemicDistribution(
                Recipient: r.Agent,
                Amount: Round2(amount),
                ContributionBreakdown: new ContributionBreakdown(
                    BaseShare: Round2(b.BaseShare),
                    FalsificationBonus: Round2(b.FalsificationBonus),
                    LoadBearingBonus: Round2(b.LoadBearingBonus),
                    OutcomeCorrectnessBonus: Round2(b.OutcomeCorrectnessBonus),
                    DissentQualityPenalty: Round2(b.DissentQualityPenalty)
                )
            );
        }).ToImmutableList();
    }

    /// <summary>
    /// Build a <c>SettlementRecorded</c> entry by running the default-v0
    /// distribution pipeline. The resulting record is auditable end-to-end
    /// via acb-validate.
    /// </summary>
    public static SettlementRecorded BuildSettlementRecord(SettlementInputs inputs)
    {
        var substratePool = inputs.DrawTotal * inputs.Settlement.SubstrateShare;
        var epistemicPool = inputs.DrawTotal * inputs.Settlement.EpistemicShare;

        var substrateDistributions = DistributeSubstrate(substratePool, inputs.SubstrateReports);
        if (substrateDistributions.Count == 0 && substratePool > 0)
        {
            // Spec §6.3: if no substrate reports, fold into epistemic pool.
            epistemicPool += substratePool;
            substratePool = 0;
        }

        var epistemicDistributions = DistributeEpistemic(epistemicPool, inputs.Contributions);

        return new SettlementRecorded(
            EntryId: inputs.EntryId,
            DeliberationId: inputs.DeliberationId,
            Timestamp: inputs.Timestamp,
            PriorEntryHash: inputs.PriorEntryHash,
            BudgetId: inputs.BudgetId,
            SettlementProfile: inputs.Settlement.Profile,
            OutcomeReferenced: inputs.OutcomeReferenced,
            DrawTotal: Round2(inputs.DrawTotal),
            AmountTotal: inputs.AmountTotal,
            AmountReturnedToRequester: Round2(inputs.AmountTotal - inputs.DrawTotal),
            SubstrateDistributions: substrateDistributions,
            EpistemicDistributions: epistemicDistributions,
            HabitDiscountApplied: inputs.HabitDiscountApplied,
            UnlockTriggered: inputs.UnlockTriggered,
            DisagreementMagnitudeInitial: inputs.DisagreementMagnitudeInitial,
            Signature: inputs.Signature
        );
    }

    private static double Round2(double n) => Math.Round(n * 100) / 100;
}
