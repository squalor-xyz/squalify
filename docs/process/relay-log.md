# Relay log

A running record of how slice relays go in squalify: what cost time, prompts or rounds, what worked, and what changed as a result. The main session orchestrates every slice and gives each slice its own subagent roles. This log is the input for improving that process. Contract-level changes are proposed upstream to slicer, and project-level changes go into `AGENTS.md`, each with the owner's approval.

## How to log

Add one entry per slice after it lands (or stops), newest first. Use this template:

```
### sq-NN: Title (YYYY-MM-DD)

- Lineup: implementer/reviewer/architect/gate model and effort; split or slivers.
- Metrics: implement rounds, review rounds, gate verdicts in order, findings by kind (fix/owner/later),
  owner prompts (questions + permission prompts), wall time per role, orchestrator full-diff reads.
- Frictions: what cost time, tokens, rounds or owner attention, and the cause.
- Wins: what worked and should be kept.
- Changes: what to do differently from the next slice on (and where it was applied).
```

## Changes adopted

| Date | Change | Source |
|---|---|---|
| 2026-10-13 | The architect checks `docs/how-it-works.md` for drift, and drift fails the gate | owner docs rule |
| 2026-10-20 | M and L slices run the architect; S slices run a lite relay | sq-02 |
| 2026-10-20 | Worktrees under `./.worktrees/`; agents keep temp files in per-role directories | sq-03 |
| 2026-10-26 | The main session orchestrates; each slice gets its own roles | owner |

## Proposed upstream (slicer)

- The role matrix has no orchestrator row, so `relay-roles recommend` reports `orchestrator (no matching row)` on every slice. Add a row, or say explicitly that the outer session orchestrates when the harness cannot nest subagents.
- `slicer show ID --json` reports `ready: false` for a slice that `slicer list` and `slicer next` treat as ready (sq-16, 2026-10-26). Document what the field means, or align it.

## Entries

### sq-03: Byte sniff and generic profile (2026-10-20)

- Lineup: sonnet/high, opus/medium, opus/high, opus/medium; no split.
- Metrics: reviewer ACCEPTED with minor fixes; architect FAIL once (drift in how-it-works section 12 and the README hints line, plus a path-leak guard); gate PASS after one fix round. Four owner decisions at setup.
- Frictions: agents kept temp directories outside the working directories, and every read there prompted the owner (`blockReadsOutsideWorkingDirectories`). `gh` is not installed, so CI on the pushes went unverified.
- Wins: test-first plus mutation checks caught the path-leak guard cleanly; owner decisions batched into one sitting before the worktree.
- Changes: the scratch location is now named explicitly in every brief.

### sq-02: Canonical descriptor writer (2026-10-20)

- Lineup: as sq-03.
- Metrics: architect FAIL once on `design.md` drift, then PASS.
- Frictions: doc drift is the most common architect finding. It is cheaper to ask the implementer to update the docs in the first round than to catch it at the architect.
- Changes: implementer briefs list the doc sections to update (from the slice's Files section) as required work.
