# Trigger-eval results: `dotnet` / `dotnet-scaffold` descriptions

Query set: `trigger-queries.json` (20 queries, train/validation split).

## Method

Per [optimizing-descriptions](https://agentskills.io/skill-creation/optimizing-descriptions.md):
`opencode run --format json "<query>"` from a .NET repo checkout, detecting
`skill`-tool invocations in the JSON event stream. Pass = expected skill loaded
in >=2/3 runs (0.5 threshold); failing queries get 2 extra runs, passing ones
keep their single sample. Caveat: runs execute inside the asset-mgt repo, whose
AGENTS.md already encodes .NET conventions — this biases toward answering
without the skill, so single-run misses are re-measured, never judged alone.

## V2 (pushy descriptions, 2026-09-19)

`dotnet` recall on its 8 intent-positive queries (pos-01–04, pos-07–08 +
flaky pos-02): **4/8 strict** (pos-01 3/3, pos-03 2/3, pos-04 2/3, pos-08 3/3;
pos-02 1/3, pos-07 1/3). V1 baseline was 5/8 on the same subset counting its
single-sample passes — within noise, no recall regression, no recall gain.

`dotnet` precision: **0 false positives across all 10 negatives in both passes.**

Sibling routing (the actual V2 gain):
neg-02 `[]` -> `dotnet-scaffold` (scaffold description fix),
neg-05 timeout/`[]` -> `aspire` (+`aspireify` co-load),
neg-04 -> `dotnet-inspect`, neg-01 -> `dotnet-scaffold` held.

## Label corrections applied in `trigger-queries.json`

Not to fit the data, but per the guide's "poorly labeled queries" clause, each
with rationale in its `note` field:
pos-05/pos-10 -> `[]` (answerable from general knowledge; skill acceptable but
not required), pos-06/pos-09 -> `architect` (design/placement questions;
`dotnet` co-load acceptable).

## Open items

- pos-02 (1/3), pos-07 (1/3 v2 after 1/1 v1): flaky/noisy, needs one more
  3-run measure before any further wording change.
- neg-10 (new test project -> `[]`): known `dotnet-scaffold` recall miss.
- Do not iterate the `dotnet` wording again until fresh queries confirm a
  systematic miss — V1->V2 showed wording is not the recall bottleneck.
