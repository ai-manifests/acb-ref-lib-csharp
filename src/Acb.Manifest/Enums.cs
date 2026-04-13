namespace Acb.Manifest;

/// <summary>
/// ACB entry type discriminator. Mirrors the three v0.1 hook entries that
/// extend the ADJ common envelope (spec §3.0).
/// </summary>
public enum AcbEntryType
{
    BudgetCommitted,
    BudgetCancelled,
    SettlementRecorded
}

public enum SettlementMode
{
    Immediate,
    Deferred,
    TwoPhase
}

public enum BudgetState
{
    Posted,
    Active,
    AwaitingOutcome,
    Settled,
    Cancelled,
    Expired
}

public enum Routine
{
    Cheap,
    Expensive
}

public enum TerminationState
{
    Converged,
    PartialCommit,
    Deadlocked
}
