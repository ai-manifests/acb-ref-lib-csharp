using System.Collections.Immutable;
using Acb.Manifest;

namespace Acb.Manifest.Tests;

/// <summary>
/// ACB spec §8 worked example as an executable test.
///
/// Same dlb_01HMXJ3E9R PR merge as ADJ §9, with a 12,000 EU budget posted,
/// the contested deliberation running for one round, the maximum habit
/// discount applying, and a 180 EU draw distributed across two substrate
/// providers and three agents per default-v0.
/// </summary>
public class PrMergeBudgetTests
{
    private const string Dlb = "dlb_01HMXJ3E9R";
    private const string Bgt = "bgt_01HMXJ3E9R";
    private const string Authority = "did:requester:acme-platform";
    private const string TestRunner = "did:adp:test-runner-v2";
    private const string Scanner = "did:adp:security-scanner-v3";
    private const string Linter = "did:adp:style-linter-v1";

    private static readonly PricingProfile Pricing = new(
        Profile: "default-v0",
        CheapRoutineRate: 50,
        ExpensiveRoutineRate: 200,
        RoundMultiplier: 1.5,
        UnlockThreshold: 0.30,
        HabitMemoryDiscount: "default-v0"
    );

    private static readonly SettlementProfileConfig Settlement = new(
        Profile: "default-v0",
        Mode: SettlementMode.Deferred,
        SubstrateShare: 0.20,
        EpistemicShare: 0.80,
        UnspentReturnsTo: Authority,
        OutcomeWindowSeconds: 604800
    );

    private static BudgetCommitted MakeBudget() => new(
        EntryId: "adj_01HMXM9A",
        DeliberationId: Dlb,
        Timestamp: DateTimeOffset.Parse("2026-04-11T14:30:00Z"),
        PriorEntryHash: null,
        BudgetId: Bgt,
        BudgetAuthority: Authority,
        PostedAt: DateTimeOffset.Parse("2026-04-11T14:30:00Z"),
        Denomination: new Denomination(Unit: "EU", ExternalUnit: "USD", ExternalRate: 0.0001),
        AmountTotal: 12000,
        Pricing: Pricing,
        Settlement: Settlement,
        Constraints: new BudgetConstraints(MaxParticipants: 8, MaxRounds: 4, Irrevocable: false),
        Signature: "ed25519:6f3a"
    );

    [Fact]
    public void Disagreement_magnitude_50_50_is_one()
    {
        var tally = new Tally(0.71, 0.71, 0);
        Assert.Equal(1.0, PricingCalculator.ComputeDisagreementMagnitude(tally), 5);
    }

    [Fact]
    public void Disagreement_magnitude_full_agreement_is_zero()
    {
        var tally = new Tally(0.89, 0, 0.18);
        Assert.Equal(0.0, PricingCalculator.ComputeDisagreementMagnitude(tally));
    }

    [Fact]
    public void Disagreement_magnitude_total_abstention_is_one()
    {
        var tally = new Tally(0, 0, 1.5);
        Assert.Equal(1.0, PricingCalculator.ComputeDisagreementMagnitude(tally));
    }

    [Fact]
    public void Low_signal_outlier_stays_under_threshold()
    {
        var tally = new Tally(0.9, 0.1, 0);
        var magnitude = PricingCalculator.ComputeDisagreementMagnitude(tally);
        Assert.True(magnitude < Pricing.UnlockThreshold);
    }

    [Fact]
    public void Cheap_routine_on_agreement_no_rounds()
    {
        var tally = new Tally(0.95, 0.05, 0);
        Assert.Equal(Routine.Cheap,
            PricingCalculator.SelectRoutine(Pricing, tally, 0, TerminationState.Converged));
    }

    [Fact]
    public void Expensive_routine_on_disagreement()
    {
        var tally = new Tally(0.71, 0.64, 0.18);
        Assert.Equal(Routine.Expensive,
            PricingCalculator.SelectRoutine(Pricing, tally, 0, TerminationState.Converged));
    }

    [Fact]
    public void Expensive_routine_when_rounds_run_even_with_low_magnitude()
    {
        var tally = new Tally(0.95, 0.05, 0);
        Assert.Equal(Routine.Expensive,
            PricingCalculator.SelectRoutine(Pricing, tally, 1, TerminationState.Converged));
    }

    [Fact]
    public void Expensive_routine_on_deadlock()
    {
        var tally = new Tally(0.5, 0.5, 0);
        Assert.Equal(Routine.Expensive,
            PricingCalculator.SelectRoutine(Pricing, tally, 0, TerminationState.Deadlocked));
    }

    [Fact]
    public void Cheap_draw_matches_spec_4_3()
    {
        Assert.Equal(150.0, PricingCalculator.ComputeCheapDraw(Pricing, 3, 0));
    }

    [Fact]
    public void Cheap_draw_with_80_pct_discount()
    {
        Assert.Equal(30.0, PricingCalculator.ComputeCheapDraw(Pricing, 3, 0.80), 5);
    }

    [Fact]
    public void Expensive_draw_one_round_matches_spec()
    {
        Assert.Equal(900.0, PricingCalculator.ComputeExpensiveDraw(Pricing, 3, 1, 0));
    }

    [Fact]
    public void Expensive_draw_three_rounds_compounds()
    {
        // 200 × 4 × 1.5^3 = 800 × 3.375 = 2700
        Assert.Equal(2700.0, PricingCalculator.ComputeExpensiveDraw(Pricing, 4, 3, 0));
    }

    [Fact]
    public void Habit_discount_caps_at_080()
    {
        var history = Enumerable.Range(0, 100)
            .Select(_ => new HistoricalDeliberation(1.0, true))
            .ToList();
        Assert.Equal(HabitMemoryCalculator.MaxHabitDiscount,
            HabitMemoryCalculator.ComputeHabitDiscount(history));
    }

    [Fact]
    public void Habit_discount_unstable_history_shrinks()
    {
        var history = Enumerable.Range(0, 100)
            .Select(i => new HistoricalDeliberation(0.9, i < 50))
            .ToList();
        var discount = HabitMemoryCalculator.ComputeHabitDiscount(history);
        // 0.9 max similarity × 0.5 stability = 0.45
        Assert.InRange(discount, 0.44, 0.46);
    }

    [Fact]
    public void Habit_discount_zero_when_empty()
    {
        Assert.Equal(0.0, HabitMemoryCalculator.ComputeHabitDiscount(new List<HistoricalDeliberation>()));
    }

    [Fact]
    public void Full_spec_8_worked_example_produces_180_eu_draw()
    {
        var budget = MakeBudget();
        var initialTally = new Tally(0.71, 0.64, 0.18);
        var roundCount = 1;

        var magnitude = PricingCalculator.ComputeDisagreementMagnitude(initialTally);
        Assert.True(magnitude > budget.Pricing.UnlockThreshold);

        var routine = PricingCalculator.SelectRoutine(
            budget.Pricing, initialTally, roundCount, TerminationState.Converged);
        Assert.Equal(Routine.Expensive, routine);

        var history = Enumerable.Range(0, 47)
            .Select(i => new HistoricalDeliberation(0.85, i < 45))
            .ToList();
        var habitDiscount = HabitMemoryCalculator.ComputeHabitDiscount(history);
        Assert.Equal(HabitMemoryCalculator.MaxHabitDiscount, habitDiscount);

        var draw = PricingCalculator.ComputeExpensiveDraw(
            budget.Pricing, 3, roundCount, habitDiscount);
        Assert.Equal(180.0, draw, 5);
    }

    [Fact]
    public void Settlement_record_returns_unspent_to_requester()
    {
        var budget = MakeBudget();
        const double drawTotal = 180;

        var contributions = new List<ParticipantContribution>
        {
            new(TestRunner, true, 2, true, 0.0196, false),
            new(Scanner, true, 1, false, 0.0441, false),
            new(Linter, true, 0, false, 0.1444, false),
        };
        var reports = new List<SubstrateReport>
        {
            new("did:substrate:acme-cluster-eu", 200, "cluster/8821443"),
            new("did:substrate:openai-azure", 100, "openai/run-9912"),
        };

        var record = SettlementCalculator.BuildSettlementRecord(new SettlementInputs(
            EntryId: "adj_01HMZQ7K",
            DeliberationId: Dlb,
            Timestamp: DateTimeOffset.Parse("2026-04-14T09:30:00Z"),
            PriorEntryHash: null,
            BudgetId: Bgt,
            AmountTotal: budget.AmountTotal,
            DrawTotal: drawTotal,
            Settlement: budget.Settlement,
            Contributions: contributions,
            SubstrateReports: reports,
            HabitDiscountApplied: 0.80,
            UnlockTriggered: true,
            DisagreementMagnitudeInitial: 0.948,
            OutcomeReferenced: "adj_01HMZP2D",
            Signature: "ed25519:7a4b"
        ));

        Assert.Equal(11820, record.AmountReturnedToRequester);
        Assert.Equal(180, record.DrawTotal);

        Assert.Equal(2, record.SubstrateDistributions.Count);
        Assert.Equal(24, record.SubstrateDistributions[0].Amount);
        Assert.Equal(12, record.SubstrateDistributions[1].Amount);

        var subSum = record.SubstrateDistributions.Sum(d => d.Amount);
        var epiSum = record.EpistemicDistributions.Sum(d => d.Amount);
        Assert.True(Math.Abs(subSum + epiSum - drawTotal) < 0.5);

        var tr = record.EpistemicDistributions.First(d => d.Recipient == TestRunner);
        var lt = record.EpistemicDistributions.First(d => d.Recipient == Linter);
        Assert.True(tr.Amount > lt.Amount);
    }

    [Fact]
    public void Store_tracks_lifecycle_states()
    {
        var store = new InMemoryBudgetStore();
        var budget = MakeBudget();
        store.Append(budget);
        Assert.Equal(BudgetState.Active, store.GetBudgetState(Bgt));

        var settlement = SettlementCalculator.BuildSettlementRecord(new SettlementInputs(
            EntryId: "adj_01HMZQ7K",
            DeliberationId: Dlb,
            Timestamp: DateTimeOffset.Parse("2026-04-14T09:30:00Z"),
            PriorEntryHash: null,
            BudgetId: Bgt,
            AmountTotal: budget.AmountTotal,
            DrawTotal: 150,
            Settlement: Settlement,
            Contributions: new List<ParticipantContribution>
            {
                new(TestRunner, true, 0, true, 0.05, false),
            },
            SubstrateReports: new List<SubstrateReport>(),
            HabitDiscountApplied: 0,
            UnlockTriggered: false,
            DisagreementMagnitudeInitial: 0.1,
            OutcomeReferenced: null,
            Signature: "ed25519:7a4b"
        ));
        store.Append(settlement);
        Assert.Equal(BudgetState.Settled, store.GetBudgetState(Bgt));
        Assert.NotNull(store.GetSettlementForDeliberation(Dlb));
    }

    [Fact]
    public void Cancellation_locks_budget()
    {
        var store = new InMemoryBudgetStore();
        store.Append(new BudgetCancelled(
            EntryId: "adj_cancel",
            DeliberationId: Dlb,
            Timestamp: DateTimeOffset.Parse("2026-04-11T14:31:00Z"),
            PriorEntryHash: null,
            BudgetId: Bgt,
            BudgetAuthority: Authority,
            Reason: "no longer needed",
            Signature: "ed25519:9c8d"
        ));
        store.Append(MakeBudget());
        Assert.Equal(BudgetState.Cancelled, store.GetBudgetState(Bgt));
    }
}
