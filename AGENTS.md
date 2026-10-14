# AGENTS.md — squalify

Guidance for coding agents working in this repository. The README is the user-facing reference. This file covers how to work on the code. Product goals, non-goals and the queue live in slicer (`slicer goals`, `slicer list`).

squalify is an Apache-2.0 .NET 10 library (`squalor.squalify`) and CLI (`squalify`). It profiles one data file and writes a deterministic JSON descriptor. It does not reference DataBall; the descriptor schema in `docs/descriptor.md` is the contract.

## Layout

- `src/squalify/`: the library. It holds the pipeline stages (sniff, format detection, format profilers, field profilers) and the descriptor model and writer.
- `src/squalify.Cli/`: the `squalify` tool.
- `tests/squalify.Tests/`: xUnit for the library. Fixtures live under `tests/squalify.Tests/fixtures/`, and golden descriptors sit next to them.
- `tests/squalify.Cli.Tests/`: xUnit for the CLI, run in-process through `CommandFactory.InvokeAsync`.
- `docs/`: `how-it-works.md` (diagrams; `.html` and `.pdf` are committed renders of it), `design.md` (architecture) and `descriptor.md` (schema).
- `scripts/`: `render-docs.sh` renders the committed docs (see Docs below).
- `.slicer/`: roadmap and slice tracking. Manage it only through the `slicer` CLI.

## Build, test, format

The SDK is pinned by `global.json`.

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
- Prefer the BCL and in-repo code. Ask the owner before adding any package. Approved dependencies: DuckDB.NET.Data.Full 1.5.5 (Parquet), System.CommandLine 2.0.11 (CLI), and in the test projects only Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3 and xunit.runner.visualstudio 3.1.5.
- Output-affecting math uses only IEEE `+ - * /` and `Math.Sqrt`. Logarithms go through `DetMath`, and sketch hashing through the in-repo `XxHash64`. Never use `Math.Log`, `Exp` or `Pow`, or `string.GetHashCode`, in output paths.
- A descriptor schema change updates `docs/descriptor.md` and bumps `schemaVersion` according to the policy there, in the same commit.
- README and `docs/` change in the same commit as the behavior they describe.

## Docs

`docs/how-it-works.md` explains in plain language how squalify works, with Mermaid diagrams. `docs/how-it-works.html` and `docs/how-it-works.pdf` are generated from it with quire's `squalor` profile (`--config squalor`) and committed.

- **Update it on every architectural review.** Update `docs/how-it-works.md` and its diagrams whenever an architectural review happens. That covers:
  - the relay architect's review of a slice;
  - a spec or design review;
  - any slice that changes the pipeline, an interface, a descriptor section or a determinism rule.

  Regenerate the renders in the same change.
- **The architect checks for drift.** The architect's brief includes `docs/how-it-works.md`. The architect reports drift between the page and the code as a finding, and the gate does not pass while drift remains.
- **Diagram rules.**
  - Labels use plain language, with the type or method name in `<small>` text.
  - Each diagram shows one idea.
  - Each diagram names the source file it comes from, or "(planned)" until the code exists.
- **Rendering.** Run `./scripts/render-docs.sh`. Then check that every Mermaid block drew (the HTML has one `<svg` per diagram) and that the PDF has no clipped diagrams. Commit the `.md`, `.html` and `.pdf` together, and never edit the renders by hand.
- **Requirements.** `render-docs.sh` needs `quire` on PATH, with its Mermaid renderer installed (`python3 install.py --with-mermaid` in the quire checkout) and the `squalor` profile in its user config. quire may live in `~/.local/bin`, which is not on the non-interactive PATH; prefix the command with `PATH="$HOME/.local/bin:$PATH"`. To render another doc, add its name to `DOCS` in the script.
- **Same-commit rule.** `docs/design.md` and `docs/descriptor.md` change in the same commit as the behavior they describe.

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

- **Worktree:** `.worktrees/<id>` on branch `<id>`, created by the orchestrator before the implementer starts. `.worktrees/` is ignored via `.gitignore`.
- **Briefs:** the slice from `slicer show <id> --json --lean`. Reviewers also get `slicer goals --json --lean`, `slicer list --json --lean`, `docs/design.md`, `docs/descriptor.md` and `docs/how-it-works.md`.
- **Architect:** checks `docs/how-it-works.md` and its diagrams against the change, and reports drift as a finding (see Docs).
- **Size S** slices run a lite relay without the architect. M and L slices run the full relay.
