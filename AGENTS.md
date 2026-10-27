# AGENTS.md - squalify

Guidance for coding agents working in this repository. Read [`CONTRIBUTING.md`](CONTRIBUTING.md) first: it has the layout, the build, test and format commands, the test categories, the code rules (determinism, dependencies, SPDX headers) and the docs rendering steps, and it applies to agents as it does to people. This file holds only what is specific to agents. The README is the user-facing front page; product goals, non-goals and the queue live in slicer (`slicer goals`, `slicer list`).

squalify is an Apache-2.0 .NET 10 library (`squalor.squalify`) and CLI (`squalify`). It does not reference DataBall; the descriptor schema in `docs/descriptor.md` is the contract.

Before handing off a change, run `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` on `squalify.sln` (see CONTRIBUTING.md).

## Docs

`docs/how-it-works.md` explains in plain language how squalify works, with Mermaid diagrams; its `.html` and `.pdf` are committed renders (rendering steps and diagram rules are in CONTRIBUTING.md).

- **Update it on every architectural review.** Update `docs/how-it-works.md` and its diagrams whenever an architectural review happens. That covers:
  - the relay architect's review of a slice;
  - a spec or design review;
  - any slice that changes the pipeline, an interface, a descriptor section or a determinism rule.

  Regenerate the renders in the same change.
- **The architect checks for drift.** The architect's brief includes `docs/how-it-works.md`. The architect reports drift between the page and the code as a finding, and the gate does not pass while drift remains.
- **Rendering.** quire may live in `~/.local/bin`, which is not on the non-interactive PATH; prefix the command with `PATH="$HOME/.local/bin:$PATH"`. Commit the `.md`, `.html` and `.pdf` together.
- **Same-commit rule.** `docs/design.md`, `docs/descriptor.md`, the README and the guides change in the same commit as the behavior they describe.

## Tests

- Test-first for slices: write the slice's named tests, watch each one fail for the stated reason, then implement.
- Mutation-check new tests (see CONTRIBUTING.md).
- Real samples never enter the repository; findings from them are written up generically under `docs/reviews/`.

## Tracking (slicer)

- Read and change roadmap, slice, goal and note state only through `slicer` (`slicer ai` explains the workflow). Never open, parse or hand-edit `.slicer/` files or the generated renders.
- `slicer next` gives the next ready slice, and `slicer show <id>` prints its spec. Use `--render` on mutations and finish with `slicer check`.

## Slice relay

Slices normally run as a slice relay (`slicer ai relay`).

- **Worktree:** `.worktrees/<id>` on branch `<id>`, created by the orchestrator before the implementer starts. Every git worktree, relay or not, goes under `./.worktrees/`, which is ignored via `.gitignore`.
- **Scratch:** scratch files go in the session scratchpad, not `/tmp` directly. Include this and the worktree rule in every role's brief.
- **Briefs:** the slice from `slicer show <id> --json --lean`. Reviewers also get `slicer goals --json --lean`, `slicer list --json --lean`, `docs/design.md`, `docs/descriptor.md` and `docs/how-it-works.md`.
- **Architect:** checks `docs/how-it-works.md` and its diagrams against the change, and reports drift as a finding (see Docs).
- **Size S** slices run a lite relay without the architect. M and L slices run the full relay.
- **Relay log:** `docs/process/relay-log.md` holds lessons from slice relays. The orchestrator reads it before a relay and updates it afterwards.
