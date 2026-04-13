using System.Collections.Immutable;

namespace Acb.Manifest;

/// <summary>
/// In-memory ACB store. Append-only. Suitable for testing, prototypes, and
/// single-process runners. Production deployments will typically back a
/// budget store with the same SQLite/Postgres journal that stores ADJ
/// entries — ACB entries follow the ADJ common envelope precisely so they
/// share storage.
/// </summary>
public sealed class InMemoryBudgetStore : IBudgetStore
{
    private readonly List<AcbEntry> _entries = [];
    private readonly object _lock = new();

    /// <summary>
    /// Append an entry to the store. Append-only — entries cannot be
    /// removed or modified after writing.
    /// </summary>
    public void Append(AcbEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
        }
    }

    public void AppendRange(IEnumerable<AcbEntry> entries)
    {
        lock (_lock)
        {
            _entries.AddRange(entries);
        }
    }

    /// <inheritdoc />
    public BudgetCommitted? GetBudgetForDeliberation(string deliberationId)
    {
        lock (_lock)
        {
            return _entries
                .OfType<BudgetCommitted>()
                .FirstOrDefault(e => e.DeliberationId == deliberationId);
        }
    }

    /// <inheritdoc />
    public SettlementRecorded? GetSettlementForDeliberation(string deliberationId)
    {
        lock (_lock)
        {
            return _entries
                .OfType<SettlementRecorded>()
                .Where(e => e.DeliberationId == deliberationId)
                .OrderByDescending(e => e.Timestamp)
                .FirstOrDefault();
        }
    }

    /// <inheritdoc />
    public BudgetCancelled? GetCancellationForDeliberation(string deliberationId)
    {
        lock (_lock)
        {
            return _entries
                .OfType<BudgetCancelled>()
                .FirstOrDefault(e => e.DeliberationId == deliberationId);
        }
    }

    /// <inheritdoc />
    public BudgetCommitted? GetBudgetById(string budgetId)
    {
        lock (_lock)
        {
            return _entries
                .OfType<BudgetCommitted>()
                .FirstOrDefault(e => e.BudgetId == budgetId);
        }
    }

    /// <inheritdoc />
    public BudgetState GetBudgetState(string budgetId)
    {
        lock (_lock)
        {
            var budget = _entries.OfType<BudgetCommitted>().FirstOrDefault(e => e.BudgetId == budgetId);
            if (budget is null) return BudgetState.Posted;

            if (_entries.OfType<BudgetCancelled>().Any(e => e.BudgetId == budgetId))
                return BudgetState.Cancelled;

            if (_entries.OfType<SettlementRecorded>().Any(e => e.BudgetId == budgetId))
                return BudgetState.Settled;

            return BudgetState.Active;
        }
    }

    /// <inheritdoc />
    public ImmutableList<AcbEntry> GetAllEntries()
    {
        lock (_lock)
        {
            return _entries.ToImmutableList();
        }
    }
}
