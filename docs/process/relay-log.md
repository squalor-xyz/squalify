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
| 2026-10-26 | Setup checks the slice against its consumer (prism) for conflicts | sq-16 |
| 2026-10-26 | Docs are required work in the implementer's first round | sq-02, sq-16 |
| 2026-10-27 | Read-only roles are time-boxed; the gate gets an ID'd checklist of earlier findings | sq-16 |
| 2026-10-27 | Non-blocking owner questions are asked as they arise, not at approval | sq-16 |

## Proposed upstream (slicer)

- The role matrix has no orchestrator row, so `relay-roles recommend` reports `orchestrator (no matching row)` on every slice. Add a row, or say explicitly that the outer session orchestrates when the harness cannot nest subagents.
- `slicer show ID --json` reports `ready: false` for a slice that `slicer list` and `slicer next` treat as ready (sq-16, 2026-10-26). Document what the field means, or align it.
- A small fix round is re-checked by "the reviewer" (Order). Allow any read-only role that already holds the findings, such as the gate, when that role is cheaper. In sq-16 that took 30 s instead of a reviewer that had run for 72 min.
- An owner answer that arrives after the finish step needs `slicer set --status started`, then a fix round, a re-gate and `slicer done` again. Consider a contract line that asks non-blocking owner questions before the gate, so the common case avoids the rollback.

## Entries

### sq-16: Value classifier (2026-10-26 to 2026-10-27)

- Lineup: implementer sonnet/high, reviewer opus/medium, architect opus/high, gate opus/medium. No split and no slivers. The main session orchestrated, because Claude Code subagents cannot nest.
- Metrics:
  - **Rounds:** implement, then 3 fix rounds: review fixes, architect fixes, and the owner's 12-hour answer.
  - **Review and gate:** review ACCEPT WITH FIXES; architect SHAPE OK; gate PASS, then a re-gate PASS after the owner's answer.
  - **Findings:** fix 9, all low or trivial; owner 1, not a blocker (hour 0 with AM/PM, answered strict 1 to 12); later 10, filed on sq-04, sq-05, sq-19 and sq-25.
  - **Owner attention:** 3 setup questions and 1 mid-run question. There were 0 permission prompts during the run.
  - **Wall time:**
    - implementer 6 min;
    - reviewer 72 min;
    - fix rounds 2 min, 30 s and 40 s;
    - architect 4 min;
    - gate 1 min, re-gate 30 s.
  - **Orchestrator reads:** no full-diff reads. One byte-level check: an NBSP escape the report rendered invisibly.
- Frictions:
  - The reviewer's 72 minutes were mostly an unbounded probe harness (a console app with about 75 cases). That probing found the real determinism gap: trimming used `char.IsWhiteSpace`, which depends on runtime Unicode tables. But nothing time-boxed it.
  - The spec was silent on about 15 behaviours, including the pattern format, hour 0, Int64 overflow and ASCII digits. The implementer decided them. That went fine here, but it surfaces design choices late.
  - Doc drift still got past the implementer and the reviewer: the how-it-works banner, the AM/PM-after-`T` rule, and a diagram's `<small>` label. The architect caught all three.
  - The owner question reached the owner only at approval, after the finish step. That forced a status rollback (`slicer set --status started`), another fix round and a re-gate.
  - I recorded the first role step without `--parent`.
  - A rendered report can hide invisible characters such as NBSP, so byte-level claims need a check on disk.
- Wins:
  - Checking the slice against prism at setup found the `NaN` conflict (prism D38) before any code was written. The owner answered it in the setup sitting.
  - Making docs required in round 1 meant the implementer shipped them with the code (13 diagrams, 13 svg).
  - The implementer used a skeleton, so its tests went red on assertions rather than on compile errors. It also found and killed its own surviving mutant, a century leap-year case.
  - Listing the implementer's interpretations in the reviewer brief got all of them judged in one pass.
  - The architect had a time box and the instruction not to re-probe, and finished in 4 minutes. It also read the next slices' specs and caught a determinism problem in sq-05 (`char.IsUpper`) before that slice starts.
  - Giving the gate an ID'd checklist of earlier findings (R1-R4, A1-A5) made it a 1-minute verification.
  - For a small fix round, resuming the gate agent instead of the slow reviewer cut the re-check to 30 s.
- Changes, from the next slice on:
  - **Time boxes:** every read-only role gets one (about 20 to 25 minutes), and probes go into scratch xUnit tests, not new apps.
  - **Implementer brief:** a standing docs checklist covering the slice's doc sections, the how-it-works banner, and diagram labels and sources.
  - **Implementer interpretations:** ask for them as a list in the report. The reviewer and the architect judge them, and an interpretation with product impact goes to the owner as an owner question right away.
  - **Owner questions:** ask non-blocking ones as soon as they arise, not only at approval.
  - **Architect brief:** keep the lookahead into the next consumer slices (sq-04, sq-05, sq-25 here).
  - **Re-checks:** after a small fix round, the re-check goes to whichever read-only role holds the context most cheaply, usually the gate.

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
