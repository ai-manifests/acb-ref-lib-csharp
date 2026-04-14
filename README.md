# Acb.Manifest

A .NET 8 implementation of the **Agent Cognitive Budget (ACB)** protocol — the metabolic-budget layer for deliberative multi-agent systems. ACB provides append-only journal entries, pricing models, habit-memory discounts, and settlement distribution that mirror the brain's resource allocation for routine vs. contested decisions.

## Table of Contents

- [Overview](#overview)
- [Key Features](#key-features)
- [Installation](#installation)
- [Architecture](#architecture)
- [Usage](#usage)
  - [Creating a Budget](#creating-a-budget)
  - [Computing Disagreement and Selecting Routines](#computing-disagreement-and-selecting-routines)
  - [Habit Memory Discount](#habit-memory-discount)
  - [Settlement Distribution](#settlement-distribution)
  - [Budget Store](#budget-store)
- [API Reference](#api-reference)
  - [ACB Entry Types](#acb-entry-types)
  - [Pricing Calculator](#pricing-calculator)
  - [Settlement Calculator](#settlement-calculator)
  - [Habit Memory Calculator](#habit-memory-calculator)
  - [Budget Store Interface](#budget-store-interface)
- [Testing](#testing)
- [License](#license)

## Overview

**Acb.Manifest** implements the ACB protocol specification, which extends the ADJ (Agentic Deliberation Journal) common envelope with three hook entries:

1. **BudgetCommitted** — pre-commits an energy budget to a deliberation
2. **BudgetCancelled** — revokes a budget before settlement
3. **SettlementRecorded** — distributes the draw across substrate providers and agent identities

The protocol enforces a **cheap-routine** / **expensive-routine** pricing model inspired by the brain's dual-process theory: familiar, low-disagreement decisions draw minimal energy, while contested or novel decisions unlock exponentially larger draws proportional to the number of belief-update rounds.

## Key Features

- **Append-only journal entries** compatible with ADJ hash-chaining and replay verification
- **Disagreement-magnitude pricing** that prevents agents from manufacturing disagreement to increase their own pay
- **Habit-memory discount** (up to 80%) for familiar deliberations with stable historical outcomes
- **Default-v0 settlement profile** with four equal-weight epistemic bonuses:
  - Base share (equal across all participants)
  - Falsification bonus (proportional to acknowledged falsifications)
  - Load-bearing bonus (equal across load-bearing voters)
  - Outcome correctness bonus (inverse Brier delta when outcome is known)
- **Dissent quality penalty** that redistributes up to 25% of flagged agents' pre-penalty total to non-flagged agents
- **Substrate distribution** proportional to reported cycles
- **In-memory budget store** suitable for testing and prototypes; production deployments typically back the store with the same SQLite/Postgres journal as ADJ entries

## Installation

### Prerequisites

- .NET 8.0 SDK or later

### Add to Your Project

Clone the repository and reference the project:

```bash
git clone https://git.marketally.com/ai-manifests/acb-ref-lib-csharp.git
cd acb-ref-lib-csharp
dotnet build
```

Reference `Acb.Manifest.csproj` from your consuming project:

```xml
<ItemGroup>
  <ProjectReference Include="..\path\to\Acb.Manifest\Acb.Manifest.csproj" />
</ItemGroup>
```

Or publish as a NuGet package and install:

```bash
dotnet add package Acb.Manifest
```

## Architecture

The library is organized into the following modules:

- **Entries.cs** — ACB entry records (`BudgetCommitted`, `BudgetCancelled`, `SettlementRecorded`) and supporting value objects (`Denomination`, `PricingProfile`, `SettlementProfileConfig`, etc.)
- **Enums.cs** — Entry type discriminators and state enumerations
- **PricingCalculator.cs** — Disagreement magnitude computation, routine selection, and draw calculation
- **HabitMemoryCalculator.cs** — Habit-memory discount function
- **SettlementCalculator.cs** — Substrate and epistemic distribution logic
- **IBudgetStore.cs** — Query contract for budget lifecycle tracking
- **InMemoryBudgetStore.cs** — Thread-safe in-memory implementation of `IBudgetStore`

## Usage

### Creating a Budget

A `BudgetCommitted` entry pre-commits energy to a deliberation. It must be written at or before the deliberation's `deliberation_opened` ADJ entry.

```csharp
using Acb.Manifest;

var budget = new BudgetCommitted(
    EntryId: "adj_01HMXM9A",
    DeliberationId: "dlb_01HMXJ3E9R",
    Timestamp: DateTimeOffset.Parse("2026-04-11T14:30:00Z"),
    PriorEntryHash: null,
    BudgetId: "bgt_01HMXJ3E9R",
    BudgetAuthority: "did:requester:acme-platform",
    PostedAt: DateTimeOffset.Parse("2026-04-11T14:30:00Z"),
    Denomination: new Denomination(
        Unit: "EU",
        ExternalUnit: "USD",
        ExternalRate: 0.0001
    ),
    AmountTotal: 12000,
    Pricing: new PricingProfile(
        Profile: "default-v0",
        CheapRoutineRate: 50,
        ExpensiveRoutineRate: 200,
        RoundMultiplier: 1.5,
        UnlockThreshold: 0.30,
        HabitMemoryDiscount: "default-v0"
    ),
    Settlement: new SettlementProfileConfig(
        Profile: "default-v0",
        Mode: SettlementMode.Deferred,
        SubstrateShare: 0.20,
        EpistemicShare: 0.80,
        UnspentReturnsTo: "did:requester:acme-platform",
        OutcomeWindowSeconds: 604800
    ),
    Constraints: new BudgetConstraints(
        MaxParticipants: 8,
        MaxRounds: 4,
        Irrevocable: false
    ),
    Signature: "ed25519:6f3a"
);
```

### Computing Disagreement and Selecting Routines

The **disagreement magnitude** is computed from the initial weighted tally:

```csharp
var initialTally = new Tally(
    ApproveWeight: 0.71,
    RejectWeight: 0.64,
    AbstainWeight: 0.18
);

double magnitude = PricingCalculator.ComputeDisagreementMagnitude(initialTally);
// magnitude ≈ 0.948 (high disagreement)
```

The routine is selected based on disagreement magnitude, round count, and termination state:

```csharp
Routine routine = PricingCalculator.SelectRoutine(
    pricing: budget.Pricing,
    initialTally: initialTally,
    roundCount: 1,
    termination: TerminationState.Converged
);
// routine == Routine.Expensive (disagreement ≥ 0.30 or roundCount > 0)
```

Compute the draw:

```csharp
double draw = PricingCalculator.ComputeExpensiveDraw(
    pricing: budget.Pricing,
    participantCount: 3,
    roundCount: 1,
    habitDiscount: 0.80
);
// draw = 200 × 3 × 1.5^1 × (1 - 0.80) = 180 EU
```

### Habit Memory Discount

The habit-memory function computes a discount (up to 80%) based on similarity to historical deliberations and their outcome stability:

```csharp
var history = new List<HistoricalDeliberation>
{
    new(Similarity: 0.85, SuccessfulOutcome: true),
    new(Similarity: 0.85, SuccessfulOutcome: true),
    new(Similarity: 0.85, SuccessfulOutcome: false),
    // ... 47 total, 45 successful
};

double discount = HabitMemoryCalculator.ComputeHabitDiscount(history);
// discount = min(0.80, 0.85 × (45/47)) ≈ 0.80 (capped at MaxHabitDiscount)
```

### Settlement Distribution

After deliberation closes, the budget authority writes a `SettlementRecorded` entry. The settlement calculator distributes the draw across substrate providers (proportional to cycles) and agents (via the default-v0 contribution scoring):

```csharp
var contributions = new List<ParticipantContribution>
{
    new(
        AgentId: "did:adp:test-runner-v2",
        Participated: true,
        AcknowledgedFalsifications: 2,
        LoadBearing: true,
        OutcomeBrierDelta: 0.0196,
        DissentQualityFlagged: false
    ),
    new(
        AgentId: "did:adp:security-scanner-v3",
        Participated: true,
        AcknowledgedFalsifications: 1,
        LoadBearing: false,
        OutcomeBrierDelta: 0.0441,
        DissentQualityFlagged: false
    ),
    new(
        AgentId: "did:adp:style-linter-v1",
        Participated: true,
        AcknowledgedFalsifications: 0,
        LoadBearing: false,
        OutcomeBrierDelta: 0.1444,
        DissentQualityFlagged: false
    ),
};

var substrateReports = new List<SubstrateReport>
{
    new("did:substrate:acme-cluster-eu", Cycles: 200, ReportRef: "cluster/8821443"),
    new("did:substrate:openai-azure", Cycles: 100, ReportRef: "openai/run-9912"),
};

var settlement = SettlementCalculator.BuildSettlementRecord(new SettlementInputs(
    EntryId: "adj_01HMZQ7K",
    DeliberationId: "dlb_01HMXJ3E9R",
    Timestamp: DateTimeOffset.Parse("2026-04-14T09:30:00Z"),
    PriorEntryHash: null,
    BudgetId: "bgt_01HMXJ3E9R",
    AmountTotal: 12000,
    DrawTotal: 180,
    Settlement: budget.Settlement,
    Contributions: contributions,
    SubstrateReports: substrateReports,
    HabitDiscountApplied: 0.80,
    UnlockTriggered: true,
    DisagreementMagnitudeInitial: 0.948,
    OutcomeReferenced: "adj_01HMZP2D",
    Signature: "ed25519:7a4b"
));

// settlement.SubstrateDistributions[0].Amount == 24 EU (200/300 × 36 EU)
// settlement.SubstrateDistributions[1].Amount == 12 EU (100/300 × 36 EU)
// settlement.EpistemicDistributions.Sum(d => d.Amount) == 144 EU (80% of 180 EU)
// settlement.AmountReturnedToRequester == 11820 EU (12000 - 180)
```

### Budget Store

The `IBudgetStore` interface provides queries by deliberation ID and budget ID:

```csharp
var store = new InMemoryBudgetStore();
store.Append(budget);

BudgetCommitted? retrieved = store.GetBudgetForDeliberation("dlb_01HMXJ3E9R");
BudgetState state = store.GetBudgetState("bgt_01HMXJ3E9R");
// state == BudgetState.Active

store.Append(settlement);
state = store.GetBudgetState("bgt_01HMXJ3E9R");
// state == BudgetState.Settled
```

Cancel a budget before settlement:

```csharp
var cancellation = new BudgetCancelled(
    EntryId: "adj_cancel",
    DeliberationId: "dlb_01HMXJ3E9R",
    Timestamp: DateTimeOffset.Parse("2026-04-11T14:31:00Z"),
    PriorEntryHash: null,
    BudgetId: "bgt_01HMXJ3E9R",
    BudgetAuthority: "did:requester:acme-platform",
    Reason: "no longer needed",
    Signature: "ed25519:9c8d"
);
store.Append(cancellation);
// GetBudgetState("bgt_01HMXJ3E9R") == BudgetState.Cancelled
```

## API Reference

### ACB Entry Types

All ACB entries extend the `AcbEntry` abstract record and mirror the ADJ §3.0 common envelope:

```csharp
public abstract record AcbEntry(
    string EntryId,
    AcbEntryType EntryType,
    string DeliberationId,
    DateTimeOffset Timestamp,
    string? PriorEntryHash
);
```

#### BudgetCommitted

Spec §3.1 — pre-commits an energy budget to a deliberation.

**Properties:**
- `BudgetId` — unique budget identifier
- `BudgetAuthority` — DID of the budget authority
- `PostedAt` — timestamp when the budget was posted
- `Denomination` — energy unit and optional external currency mapping
- `AmountTotal` — total energy committed
- `Pricing` — pricing profile (rates, unlock threshold, habit-memory function)
- `Settlement` — settlement profile (mode, substrate/epistemic shares, unspent return policy)
- `Constraints` — optional budget-time constraints (max participants, max rounds, irrevocability)
- `Signature` — cryptographic signature from the budget authority

#### BudgetCancelled

Spec §3.3 — cancels a budget before settlement. Mutually exclusive with `SettlementRecorded`.

**Properties:**
- `BudgetId` — budget identifier
- `BudgetAuthority` — DID of the budget authority
- `Reason` — human-readable cancellation reason
- `Signature` — cryptographic signature from the budget authority

#### SettlementRecorded

Spec §6.4 — distributes the draw across substrate providers and agent identities.

**Properties:**
- `BudgetId` — budget identifier
- `SettlementProfile` — settlement profile name (e.g., "default-v0")
- `OutcomeReferenced` — optional ADJ entry ID referencing the observed outcome
- `DrawTotal` — total energy drawn from the budget
- `AmountTotal` — original budget total
- `AmountReturnedToRequester` — unspent energy returned to the requester
- `SubstrateDistributions` — list of substrate provider allocations
- `EpistemicDistributions` — list of agent identity allocations with contribution breakdowns
- `HabitDiscountApplied` — habit-memory discount applied (0.0 to 0.80)
- `UnlockTriggered` — whether the expensive routine was unlocked
- `DisagreementMagnitudeInitial` — disagreement magnitude from the initial tally
- `Signature` — cryptographic signature from the budget authority

### Pricing Calculator

#### ComputeDisagreementMagnitude

```csharp
public static double ComputeDisagreementMagnitude(Tally tally)
```

Computes disagreement magnitude as:

```
magnitude = 1 − |approve − reject| / (approve + reject)
```

Returns `1.0` if all agents abstained (total disagreement).

#### SelectRoutine

```csharp
public static Routine SelectRoutine(
    PricingProfile pricing,
    Tally initialTally,
    int roundCount,
    TerminationState termination)
```

Selects `Routine.Cheap` when **all** of:
- `roundCount == 0`
- disagreement magnitude `< pricing.UnlockThreshold`
- termination is `Converged`

Otherwise selects `Routine.Expensive`.

#### ComputeCheapDraw

```csharp
public static double ComputeCheapDraw(
    PricingProfile pricing,
    int participantCount,
    double habitDiscount = 0.0)
```

Computes:

```
draw = CheapRoutineRate × participantCount × (1 − habitDiscount)
```

#### ComputeExpensiveDraw

```csharp
public static double ComputeExpensiveDraw(
    PricingProfile pricing,
    int participantCount,
    int roundCount,
    double habitDiscount = 0.0)
```

Computes:

```
draw = ExpensiveRoutineRate × participantCount × RoundMultiplier^roundCount × (1 − habitDiscount)
```

### Settlement Calculator

#### DistributeSubstrate

```csharp
public static ImmutableList<SubstrateDistribution> DistributeSubstrate(
    double pool,
    IReadOnlyList<SubstrateReport> reports)
```

Distributes the substrate pool proportional to reported cycles. If no reports are provided, returns an empty list (the pool is folded into the epistemic pool per spec §6.3).

#### DistributeEpistemic

```csharp
public static ImmutableList<EpistemicDistribution> DistributeEpistemic(
    double pool,
    IReadOnlyList<ParticipantContribution> contributions)
```

Distributes the epistemic pool via the default-v0 contribution scoring:
- **Base share** (25%) — equal across all participants
- **Falsification bonus** (25%) — proportional to acknowledged falsifications
- **Load-bearing bonus** (25%) — equal across load-bearing voters
- **Outcome correctness bonus** (25%) — inverse Brier delta when outcome is known

If a bonus category has no eligible recipients, the pool distributes equally across all participants so the share is not lost.

After the four bonuses are computed, the **dissent quality penalty** subtracts up to 25% of flagged agents' pre-penalty total and redistributes it proportionally to non-flagged agents.

#### BuildSettlementRecord

```csharp
public static SettlementRecorded BuildSettlementRecord(SettlementInputs inputs)
```

Builds a complete `SettlementRecorded` entry by running the substrate and epistemic distribution pipelines. The resulting record is auditable end-to-end via `acb-validate`.

### Habit Memory Calculator

#### ComputeHabitDiscount

```csharp
public static double ComputeHabitDiscount(
    IReadOnlyList<HistoricalDeliberation> history)
```

Computes:

```
habitDiscount = min(0.80, maxSimilarity × stability)
```

where `stability = weightedSuccess / weightSum` (success fraction weighted by similarity).

Returns `0.0` if `history` is empty.

### Budget Store Interface

```csharp
public interface IBudgetStore
{
    BudgetCommitted? GetBudgetForDeliberation(string deliberationId);
    SettlementRecorded? GetSettlementForDeliberation(string deliberationId);
    BudgetCancelled? GetCancellationForDeliberation(string deliberationId);
    BudgetCommitted? GetBudgetById(string budgetId);
    BudgetState GetBudgetState(string budgetId);
    ImmutableList<AcbEntry> GetAllEntries();
}
```

**InMemoryBudgetStore** provides a thread-safe, append-only implementation suitable for testing and prototypes. Production deployments typically back the store with the same SQLite/Postgres journal as ADJ entries.

## Testing

The test suite includes a full worked example from ACB spec §8 — a contested PR-merge deliberation with a 12,000 EU budget, one round of belief updates, maximum habit discount (80%), and a 180 EU draw distributed across two substrate providers and three agents.

Run tests:

```bash
cd tests/Acb.Manifest.Tests
dotnet test
```

Key test cases:
- Disagreement magnitude computation (50/50 split, full agreement, total abstention)
- Routine selection (cheap vs. expensive, unlock threshold, round count, termination state)
- Cheap and expensive draw calculation with habit discount
- Habit-memory discount (maximum discount, unstable history, empty history)
- Settlement distribution (substrate proportional to cycles, epistemic default-v0 scoring, empty bonus pools, dissent quality penalty)
- Budget lifecycle states (Posted, Active, Settled, Cancelled)

## License

Apache-2.0 — see [`LICENSE`](LICENSE) for the full license text and [`NOTICE`](NOTICE) for attribution.