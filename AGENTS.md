# AGENTS.md — squalify

Guidance for coding agents working in this repository. The README is the user-facing reference. This file covers how to work on the code. Product goals, non-goals and the queue live in slicer (`slicer goals`, `slicer list`).

squalify is an Apache-2.0 .NET 10 library (`squalor.squalify`) and CLI (`squalify`). It profiles one data file and writes a deterministic JSON descriptor. It does not reference DataBall; the descriptor schema in `docs/descriptor.md` is the contract.

## Layout (planned; created by sq-01)

- `src/squalify/`: the library. It holds the pipeline stages (sniff, format detection, format profilers, field profilers) and the descriptor model and writer.
- `src/squalify.Cli/`: the `squalify` tool.
- `tests/squalify.Tests/`: xUnit. Fixtures live under `tests/squalify.Tests/fixtures/`, and golden descriptors sit next to them.
- `docs/`: `design.md` (architecture) and `descriptor.md` (schema).
- `.slicer/`: roadmap and slice tracking. Manage it only through the `slicer` CLI.

## Build, test, format

The SDK is pinned by `global.json` (added by sq-01).

```bash
dotnet build squalify.sln -c Release
dotnet test squalify.sln -c Release
dotnet format squalify.sln --verify-no-changes
```

Run all three before handing off a change.

## Code rules

- Every source file starts with `// SPDX-License-Identifier: Apache-2.0`. The namespace is `squalor.squalify` (with `.Cli` for the tool).
- Nullable is on. Public members carry XML doc comments.
- Public failures surface as `SqualifyException`. Wrap lower-level exceptions instead of leaking IO or DuckDB types.
- **Determinism is a hard rule.** Output must not depend on the current time, culture, machine, path, thread scheduling, hash-seed randomization or dictionary enumeration order. Format numbers with the invariant culture and shortest round-trip formatting, and sort every collection by a documented key. See `docs/design.md`.
- No ML, no LLM calls, no network access.
- Literal data values appear in a descriptor only under `--include-values`.
- Prefer the BCL and in-repo code. Ask the owner before adding any package. The only approved dependency is DuckDB.NET.Data.Full (pinned 1.5.5), for Parquet.
- A descriptor schema change updates `docs/descriptor.md` and bumps `schemaVersion` according to the policy there, in the same commit.
- README and `docs/` change in the same commit as the behavior they describe.

## Tests

- Test-first for slices: write the slice's named tests, watch each one fail for the stated reason, then implement.
- Mutation-check new tests: break the product code, confirm the test goes red for the right reason, then revert.
- Every supported format has golden-descriptor tests. Determinism tests profile the same fixture twice, and under a different culture, and compare the bytes.
- No GB-scale fixtures in CI. Large-file behavior is tested with generated streams.

## Tracking (slicer)

- Read and change roadmap, slice, goal and note state only through `slicer` (`slicer ai` explains the workflow). Never open, parse or hand-edit `.slicer/` files or the generated renders.
- `slicer next` gives the next ready slice, and `slicer show <id>` prints its spec. Use `--render` on mutations and finish with `slicer check`.

## Slice relay

Slices normally run as a slice relay (`slicer ai relay`).

- **Worktree:** `.worktrees/<id>` on branch `<id>`, created by the orchestrator before the implementer starts. `.worktrees/` is excluded via `.git/info/exclude`.
- **Briefs:** the slice from `slicer show <id> --json --lean`. Reviewers also get `slicer goals --json --lean`, `slicer list --json --lean`, `docs/design.md` and `docs/descriptor.md`.
- **Size S** slices run a lite relay without the architect. M and L slices run the full relay.
