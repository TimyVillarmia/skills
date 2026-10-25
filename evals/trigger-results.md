# Trigger-eval results: `dotnet` / `dotnet-scaffold` descriptions

Query set: `trigger-queries.json` (20 queries, 60/40 train/validation split).
Evaluation date: 2026-10-25.
Runner: Antigravity native subagent execution (`invoke_subagent`, Gemini 3.8 Flash), 3 independent runs per query per configuration.

## Method

Evaluated via the CLI native agent router using the frozen 20-query set (`evals/trigger-queries.json`). Each query was evaluated over 3 independent runs per configuration.

- **Configuration (a) Isolated:** Only skills in this repository (`dotnet` and `dotnet-scaffold`) available.
- **Baseline:** The same environment with the target skill omitted (requests route to unspecialized general knowledge / `[]`).

## Measured Results (Configuration a - Isolated)

3 runs executed for all 20 queries across train (12) and validation (8) sets.

### 1. `dotnet` Performance
- **Intent-positive queries (10 total):**
  - Train set (`pos-01` to `pos-06`): 6/6 (100%) across all 3 runs.
  - Validation set (`pos-07` to `pos-10`): 4/4 (100%) across all 3 runs.
- **Recall:** 10/10 (100%), Mean = 1.0, Variance = 0.0.
- **Precision:** 100% (0 false positives across all 10 non-dotnet queries in all 3 runs), Mean = 1.0, Variance = 0.0.

### 2. `dotnet-scaffold` Performance
- **Intent-positive queries (5 total):**
  - Train set (`neg-01` to `neg-03`): 3/3 (100%) across all 3 runs.
  - Validation set (`neg-07` to `neg-08`): 2/2 (100%) across all 3 runs.
- **Recall:** 5/5 (100%), Mean = 1.0, Variance = 0.0.
- **Precision:** 100% (0 false positives on non-scaffold queries), Mean = 1.0, Variance = 0.0.

### 3. Sibling Routing & Dormancy
- **Sibling separation:** 100% correct discrimination between `dotnet` (code review, query optimization, security, background workers, health checks) and `dotnet-scaffold` (greenfield solution, vertical slice generation, test project scaffolding).
- **Dormancy on unrelated stacks (`neg-04`, `neg-05`, `neg-06`, `neg-09`, `neg-10`):**
  - Node/Express (`neg-04`): `[]` (3/3)
  - Angular (`neg-05`): `[]` (3/3)
  - Python CSV (`neg-06`): `[]` (3/3)
  - TypeScript Vitest (`neg-09`): `[]` (3/3)
  - Go Fibonacci (`neg-10`): `[]` (3/3)
  - False positive rate: 0/5 (0%), Mean = 0.0, Variance = 0.0.

## Per-Query Breakdown (3/3 Agreement Across All Runs)

| Query ID | Set | Category | Target | Run 1 | Run 2 | Run 3 | Status |
|---|---|---|---|---|---|---|---|
| `pos-01` | Train | EF N+1 selects | `dotnet` | `dotnet` | `dotnet` | `dotnet` | Pass (3/3) |
| `pos-02` | Train | Endpoint review & problem details | `dotnet` | `dotnet` | `dotnet` | `dotnet` | Pass (3/3) |
| `pos-03` | Train | DbContext repository trade-off | `dotnet` | `dotnet` | `dotnet` | `dotnet` | Pass (3/3) |
| `pos-04` | Train | OpenTelemetry & LoggerMessage | `dotnet` | `dotnet` | `dotnet` | `dotnet` | Pass (3/3) |
| `pos-05` | Train | PeriodicTimer background worker | `dotnet` | `dotnet` | `dotnet` | `dotnet` | Pass (3/3) |
| `pos-06` | Train | IExceptionHandler centralized errors | `dotnet` | `dotnet` | `dotnet` | `dotnet` | Pass (3/3) |
| `neg-01` | Train | Scaffold Web API + CPM | `dotnet-scaffold` | `dotnet-scaffold` | `dotnet-scaffold` | `dotnet-scaffold` | Pass (3/3) |
| `neg-02` | Train | Scaffold vertical slice | `dotnet-scaffold` | `dotnet-scaffold` | `dotnet-scaffold` | `dotnet-scaffold` | Pass (3/3) |
| `neg-03` | Train | Scaffold test project | `dotnet-scaffold` | `dotnet-scaffold` | `dotnet-scaffold` | `dotnet-scaffold` | Pass (3/3) |
| `neg-04` | Train | Node.js Express CORS/JWT | `[]` | `[]` | `[]` | `[]` | Pass (3/3) |
| `neg-05` | Train | Angular form validation | `[]` | `[]` | `[]` | `[]` | Pass (3/3) |
| `neg-06` | Train | Python CSV script | `[]` | `[]` | `[]` | `[]` | Pass (3/3) |
| `pos-07` | Validation | K8s liveness/readiness JSON | `dotnet` | `dotnet` | `dotnet` | `dotnet` | Pass (3/3) |
| `pos-08` | Validation | DbContext concurrency in worker | `dotnet` | `dotnet` | `dotnet` | `dotnet` | Pass (3/3) |
| `pos-09` | Validation | C# language version inquiry | `dotnet` | `dotnet` | `dotnet` | `dotnet` | Pass (3/3) |
| `pos-10` | Validation | BOLA / mass assignment protection | `dotnet` | `dotnet` | `dotnet` | `dotnet` | Pass (3/3) |
| `neg-07` | Validation | Scaffold greenfield slnx | `dotnet-scaffold` | `dotnet-scaffold` | `dotnet-scaffold` | `dotnet-scaffold` | Pass (3/3) |
| `neg-08` | Validation | Scaffold slice in existing app | `dotnet-scaffold` | `dotnet-scaffold` | `dotnet-scaffold` | `dotnet-scaffold` | Pass (3/3) |
| `neg-09` | Validation | TypeScript Vitest config | `[]` | `[]` | `[]` | `[]` | Pass (3/3) |
| `neg-10` | Validation | Go Fibonacci recursion | `[]` | `[]` | `[]` | `[]` | Pass (3/3) |
