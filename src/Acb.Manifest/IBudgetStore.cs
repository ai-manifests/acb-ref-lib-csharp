using System.Collections.Immutable;

namespace Acb.Manifest;

/// <summary>
/// The ACB query contract — the analogue of ADJ §7's IJournalStore. A
/// budget store indexes <c>BudgetCommitted</c>, <c>BudgetCancelled</c>, and
/// <c>SettlementRecorded</c> entries by <c>DeliberationId</c> and by
/// <c>BudgetId</c> so deliberation runners and validators can ask "what
/// budget funds this deliberation" or "has this budget been settled yet".
/// </summary>
public interface IBudgetStore
{
    BudgetCommitted? GetBudgetForDeliberation(string deliberationId);
    SettlementRecorded? GetSettlementForDeliberation(string deliberationId);
    BudgetCancelled? GetCancellationForDeliberation(string deliberationId);
    BudgetCommitted? GetBudgetById(string budgetId);
    BudgetState GetBudgetState(string budgetId);
    ImmutableList<AcbEntry> GetAllEntries();
}
