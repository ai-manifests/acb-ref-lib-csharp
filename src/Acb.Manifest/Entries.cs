using System.Collections.Immutable;

namespace Acb.Manifest;

/// <summary>
/// Common envelope shared by all ACB entries. Mirrors ADJ §3.0 so ACB entries
/// can be appended to the same journal as ADJ entries and inherit hash
/// chaining, append-only guarantees, and replay verification.
/// </summary>
public abstract record AcbEntry(
    string EntryId,
    AcbEntryType EntryType,
    string DeliberationId,
    DateTimeOffset Timestamp,
    string? PriorEntryHash
);

/// <summary>
/// Spec §3.1 — written when the requester pre-commits an energy budget to
/// a deliberation. One per deliberation. MUST be written at or before the
/// deliberation's <c>deliberation_opened</c> entry.
/// </summary>
public sealed record BudgetCommitted(
    string EntryId,
    string DeliberationId,
    DateTimeOffset Timestamp,
    string? PriorEntryHash,
    string BudgetId,
    string BudgetAuthority,
    DateTimeOffset? PostedAt,
    Denomination Denomination,
    double AmountTotal,
    PricingProfile Pricing,
    SettlementProfileConfig Settlement,
    BudgetConstraints? Constraints,
    string Signature
) : AcbEntry(EntryId, AcbEntryType.BudgetCommitted, DeliberationId, Timestamp, PriorEntryHash);

/// <summary>
/// Spec §3.3 — written if the budget authority cancels a budget before
/// settlement. Mutually exclusive with <c>SettlementRecorded</c>.
/// </summary>
public sealed record BudgetCancelled(
    string EntryId,
    string DeliberationId,
    DateTimeOffset Timestamp,
    string? PriorEntryHash,
    string BudgetId,
    string BudgetAuthority,
    string Reason,
    string Signature
) : AcbEntry(EntryId, AcbEntryType.BudgetCancelled, DeliberationId, Timestamp, PriorEntryHash);

/// <summary>
/// Spec §6.4 — the terminal write that distributes the drawn amount across
/// substrate providers and agent identities. Written by the budget authority's
/// journal after <c>deliberation_closed</c> (and after <c>outcome_observed</c>
/// when settlement mode is <c>deferred</c> or <c>two_phase</c>).
/// </summary>
public sealed record SettlementRecorded(
    string EntryId,
    string DeliberationId,
    DateTimeOffset Timestamp,
    string? PriorEntryHash,
    string BudgetId,
    string SettlementProfile,
    string? OutcomeReferenced,
    double DrawTotal,
    double AmountTotal,
    double AmountReturnedToRequester,
    ImmutableList<SubstrateDistribution> SubstrateDistributions,
    ImmutableList<EpistemicDistribution> EpistemicDistributions,
    double HabitDiscountApplied,
    bool UnlockTriggered,
    double DisagreementMagnitudeInitial,
    string Signature
) : AcbEntry(EntryId, AcbEntryType.SettlementRecorded, DeliberationId, Timestamp, PriorEntryHash);

/// <summary>
/// Energy-unit denomination. <c>Unit</c> is always <c>"EU"</c>; the optional
/// external mapping records how EU is pegged to off-protocol currency at
/// posting time.
/// </summary>
public sealed record Denomination(
    string Unit = "EU",
    string? ExternalUnit = null,
    double? ExternalRate = null,
    string? RateSource = null
);

/// <summary>
/// Pricing profile — spec §3.1 / §4. The pricing parameters baked into the
/// budget at posting time.
/// </summary>
public sealed record PricingProfile(
    string Profile,
    double CheapRoutineRate,
    double ExpensiveRoutineRate,
    double RoundMultiplier,
    double UnlockThreshold,
    string? HabitMemoryDiscount = null
);

/// <summary>
/// Settlement profile configuration — spec §3.1 / §6. How the draw is
/// distributed and when settlement is written.
/// </summary>
public sealed record SettlementProfileConfig(
    string Profile,
    SettlementMode Mode,
    double SubstrateShare,
    double EpistemicShare,
    string UnspentReturnsTo,
    int? OutcomeWindowSeconds = null
);

/// <summary>
/// Optional budget-time constraints honored by ACB-aware deliberation runners.
/// </summary>
public sealed record BudgetConstraints(
    int? MaxParticipants = null,
    int? MaxRounds = null,
    bool Irrevocable = false
);

/// <summary>
/// A single substrate-provider allocation in a settlement record.
/// </summary>
public sealed record SubstrateDistribution(
    string Recipient,
    double Amount,
    string Basis,
    string? ReportRef = null
);

/// <summary>
/// A single agent-identity allocation in a settlement record, with a full
/// per-bonus breakdown for audit.
/// </summary>
public sealed record EpistemicDistribution(
    string Recipient,
    double Amount,
    ContributionBreakdown? ContributionBreakdown = null
);

/// <summary>
/// Default-v0 contribution breakdown — four equal-weight bonuses minus a
/// dissent quality penalty. Spec §6.2.
/// </summary>
public sealed record ContributionBreakdown(
    double BaseShare,
    double FalsificationBonus,
    double LoadBearingBonus,
    double OutcomeCorrectnessBonus,
    double DissentQualityPenalty
);
