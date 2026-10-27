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
| 2026-10-27 | Slices with disjoint Files run in parallel, each in its own worktree; they land one at a time | sq-04, sq-18 |
| 2026-10-27 | Owner questions are asked while the next role runs, not between roles | sq-04 |
| 2026-10-27 | Role time is read from the harness duration, not the role's own report | sq-04 |
| 2026-10-27 | The implementer brief includes a local real-sample sanity table (no names or values) | sq-04 |
| 2026-10-27 | Owner decisions are quoted verbatim in briefs, never paraphrased | sq-06 |
| 2026-10-27 | Parallel landing: commit the branch and merge main before the last fix round, then gate once on the merged result | sq-05, sq-06 |
| 2026-10-27 | Clones run `slicer setup-git` and its git config, so `.slicer` files merge without conflicts | sq-26 |
| 2026-10-27 | Check CI with `gh run list` after every push to main | sq-29 |
| 2026-10-27 | Gates and implementers also run the suite with `GITHUB_ACTIONS=true` | sq-29 |

## Proposed upstream (slicer)

- The role matrix has no orchestrator row, so `relay-roles recommend` reports `orchestrator (no matching row)` on every slice. Add a row, or say explicitly that the outer session orchestrates when the harness cannot nest subagents.
- `slicer show ID --json` reports `ready: false` for a slice that `slicer list` and `slicer next` treat as ready (sq-16, 2026-10-26). Document what the field means, or align it.
- A small fix round is re-checked by "the reviewer" (Order). Allow any read-only role that already holds the findings, such as the gate, when that role is cheaper. In sq-16 that took 30 s instead of a reviewer that had run for 72 min.
- An owner answer that arrives after the finish step needs `slicer set --status started`, then a fix round, a re-gate and `slicer done` again. Consider a contract line that asks non-blocking owner questions before the gate, so the common case avoids the rollback.
- Withdrawn (2026-10-27): the render merge conflicts came from this clone never running `slicer setup-git` (slicer S121 already provides the drivers). Filed upstream instead: notes on slicer s412 and the new item s416.
- `slicer land` rejects `--render`, unlike the other mutations (2026-10-27).
- A Check item that needs another slice to land first (sq-18's corpus run after sq-04) has no place in the contract. The finish step was deferred until after the merge.

## Entries

### sq-29, sq-07, sq-31: Release workflow, values tier, drop Parquet; v0.1.0 published (2026-10-27)

- Lineup: implementer sonnet/high on all three; reviewer opus/medium; gate opus/medium. sq-07 (M) also ran the architect; sq-29 and sq-31 ran the lite relay.
- Metrics:
  - **Wall time (harness):** sq-29 implementer 3.5 min plus 2 fix rounds under 1 min each, reviewer 11 min, gate under 1 min. sq-07 implementer 15 min, reviewer 6 min (ACCEPT WITH FIXES; all interpretations accepted). sq-31 reviewer 1.5 min (ACCEPT, no fixes), gate 1 min (PASS first time).
  - **Owner decisions:** sq-29 R6 leave the release ungated, R7 bump the version and check it against the tag; sq-07 R4 drop duplicate allow-list names; scope: "Drop Parquet from v1" and "Tag after sq-07 (Recommended)", which turned parked sq-08 into the new sq-31.
  - **Release:** v0.1.0 tagged at c2a8d38 after the owner confirmed; the Release run passed (build 47 s, publish 10 s, GitHub release 13 s) and both packages were pushed through trusted publishing.
- Frictions:
  - **CI-only failure after sq-29 landed.** `ContinuousIntegrationBuild`, set in the props whenever `GITHUB_ACTIONS` was true, mapped source paths to `/_/` and broke 3 `CallerFilePath` tests on all three CI OSes. Every local run (implementer, reviewer, gate) passed because none set `GITHUB_ACTIONS`. The post-push `gh` check caught it; fixed on main in 065f656 by passing the property only to the release pack steps.
  - **slicer syntax misses by the orchestrator:** `note` needs `--text`; `add` rejects `--group` (use `set --group`); `relay-run step --step` is only for existing steps; `slicer land` after `slicer done` on a fast-forwarded branch has nothing left to commit.
- Wins:
  - **Asking the owner about scope mid-run paid off.** A dependency-size question (DuckDB's native bundle is about 110 MB) led the owner to drop Parquet before v1, which removed the heaviest dependency and an unbuilt slice from the release path.
  - **Committing the branch before the gate** let the gate check an exact commit and made landing a fast-forward.
- Changes: gates and implementers run the suite once more with `GITHUB_ACTIONS=true`; CI is checked after every push (both in Changes adopted).

### Parallel run: sq-14, sq-05, sq-17, sq-06, sq-25, sq-27, sq-26 (2026-10-27)

Seven slices landed in one session, with two running at any time. The prism blockers (sq-05, sq-14, sq-17, sq-25, sq-26) are all landed.

- Lineup: M slices implementer sonnet/high, reviewer opus/medium, architect opus/high, gate opus/medium. S slices: lite relay, implementer haiku/xhigh (sq-18, sq-26) or sonnet/high (sq-25, sq-27, chosen for judgment-heavy work).
- Metrics:
  - **Wall time (harness), implementer:** 7 to 19 min for M slices, 7 to 11 min for S slices (sq-26: 38 min including cross-platform test plumbing).
  - **Read-only roles:** 1 to 5 min each, every time. Gates passed first time on every slice except sq-25 (one confidentiality line) and the sq-06 gate's flaky-test observation.
  - **Owner attention:** about 15 short sittings, each asked while another role ran. The owner overrode a recommendation twice (null-token keys by vocabulary entry; parsed-value `constant`).
  - **Merges:** sq-05 (after sq-14), sq-06 (after sq-17), sq-25 (after sq-06), sq-26 (after sq-27).
- Frictions:
  - **My briefs and specs had errors** that roles caught: a paraphrased owner decision (`Count > 0` instead of non-null values, sq-06 K2), a naive run-start rule in the sq-27 spec I wrote (absorbed preamble lines), an invented "CLI stays non-packable" (sq-26), and an owner option worded without testing its own example (sq-04 clean share).
  - **Generated renders conflicted on every parallel merge** until `slicer setup-git`'s merge drivers were configured (this clone never had them). The first merge afterwards (sq-26) had zero conflicts.
  - **A flaky test landed on main** (sq-17's allocation bound, hit by JIT tier-up). The sq-06 gate saw one failure in 21 runs; the orchestrator reproduced it and fixed the bound at land.
  - **Gate-merge-regate** (sq-05) cost an extra role run; committing the branch and merging main before the fix round (sq-06 onward) let one gate cover the merged result.
- Wins:
  - **Asking owner questions while the next role runs** cost no wall time and avoided every post-finish rollback.
  - **Architects reading the parallel slice's in-progress diff** found a cross-slice product issue (sq-06 `nonNullFields` empty on ATE export files) before that slice reached review, and predicted merge conflicts, both semantic ones included, correctly.
  - **Consumer probing:** the sq-25 reviewer tested each contract formula against the code and the consumer's decisions, and found three overclaims plus internal decision IDs in an Apache-licensed doc.
  - **Implementers verified and pushed back with evidence:** the 11-of-13 arithmetic (sq-14), the `nulls == 0` never-null test (sq-25), the lead rule (sq-27).
  - **Real-sample sanity tables** (local, aggregates only) on every slice caught nothing wrong, and made regressions cheap to rule out.
- Changes, from the next slice on:
  - **Quote filed owner decisions verbatim** in briefs; never paraphrase.
  - **Treat orchestrator-written specs like any other:** check a proposed rule against the existing fixtures before filing it.
  - **Parallel landing:** commit the branch and merge main before the last fix round, then gate once on the merged result.
  - **Every re-gate brief keeps the standard scans** (non-ASCII, banned APIs) and runs the library suite at least 3 times.
  - **New clones run `slicer setup-git`** and the git config it prints.

### sq-18: Private-corpus harness (2026-10-27)

- Lineup: implementer haiku/xhigh (matrix r1), reviewer opus/medium, gate opus/medium. Lite relay (size S), run in parallel with sq-04.
- Metrics:
  - **Rounds:** implement, then one fix round.
  - **Review and gate:** review ACCEPT WITH FIXES (S1-S3 fix, S4-S5 owner); gate PASS first time.
  - **Owner attention:** 1 setup question and 1 mid-run sitting (2 questions). 0 permission prompts.
  - **Wall time (harness):** implementer 7 min, reviewer 2 min, fix round 4 min, gate 1 min. About 35 minutes start to gate.
  - **Land:** corpus run on the private samples after merging sq-04: 8 pass, 0 fail.
- Frictions:
  - Merging main conflicted in the generated ROADMAP renders (see Proposed upstream).
  - The Check item needed sq-04 landed first, so `slicer done` waited until land time.
- Wins:
  - Haiku at xhigh produced a complete, accurate report on an S slice, including the interpretations.
  - The 2-minute review found two real defects by probing: update mode leaking into the suite, and a directory symlink loop.
- Changes: haiku/xhigh is fine for well-specified S implementers. Defer the finish step when a Check item depends on another slice.

### sq-04: Delimited dialect and regions (2026-10-27)

- Lineup: implementer sonnet/high, reviewer opus/medium, architect opus/high, gate opus/medium. No split. Ran in parallel with sq-18 from fix round 1 on.
- Metrics:
  - **Rounds:** implement, then 2 fix rounds (review, architect).
  - **Review and gate:** review ACCEPT WITH FIXES; architect SHAPE OK with 3 drift blockers; gate PASS first time.
  - **Findings:** fix 8 (R1, R6, A2-A4, A6-A8); owner 10 decisions in 4 sittings (2 at setup, 4 after the implementer, 2 after the review, 2 after the architect); later 9, filed on sq-05, sq-07, sq-09, sq-13, sq-17 and sq-25.
  - **Wall time (harness):** implementer 33 min, reviewer 3 min, fix rounds 6 and 4 min, architect 5 min, gate 2 min. About 95 minutes start to gate, against about 3 hours for sq-16 (size M).
  - **Orchestrator reads:** no full-diff reads.
- Frictions:
  - The implementer marked 15 of 19 interpretations as product-affecting. The spec still left real choices open: the viability floor, the line-length unit, the evidence volume, and the clean-share rule.
  - I phrased owner option 6 ("contains anywhere") without testing it on its own example (`1,5;2,3`). The implementer had to add a decimal-comma exemption.
  - Fix round 1 wrote its tests after the code. Fix round 2 was told "test-first" and complied.
  - The reviewer reported "about 25 minutes" when the harness measured 3.
- Wins:
  - Checking prism at setup found the ATE export unescaped-quote rule. A grep of the real samples showed it was latent, and the owner settled it before code was written.
  - Asking owner questions while the next role ran cost no wall time.
  - The real-sample sanity table showed the slice working on production files, without exposing names or values.
  - Time boxes held: the read-only roles took 2 to 5 minutes, against 72 for the sq-16 reviewer.
  - The ID'd checklist (owner 1-10, R, A) made the gate a 2-minute verification again.
  - The architect's lookahead found concrete obstacles for sq-05, sq-09 and sq-13, and a fingerprint instability (quote) that was fixed in-slice.
- Changes, from the next slice on:
  - **Owner options:** check each proposed rule against its own example before asking.
  - **Fix rounds:** the brief says test-first explicitly.
  - **L slices:** consider a short spec-check pass before the implementer, to surface choices like the viability floor at setup.

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
- Frictions: agents kept temp directories outside the working directories, and every read there prompted the owner (`blockReadsOutsideWorkingDirectories`). CI on the pushes went unverified (`gh` is installed at `/opt/homebrew/bin/gh` but was not on the agent's PATH; corrected 2026-10-27).
- Wins: test-first plus mutation checks caught the path-leak guard cleanly; owner decisions batched into one sitting before the worktree.
- Changes: the scratch location is now named explicitly in every brief.

### sq-02: Canonical descriptor writer (2026-10-20)

- Lineup: as sq-03.
- Metrics: architect FAIL once on `design.md` drift, then PASS.
- Frictions: doc drift is the most common architect finding. It is cheaper to ask the implementer to update the docs in the first round than to catch it at the architect.
- Changes: implementer briefs list the doc sections to update (from the slice's Files section) as required work.
