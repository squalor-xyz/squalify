# Contributing to squalify

squalify is an Apache-2.0 .NET 10 library (`squalor.squalify`) and .NET tool (`squalify`, package `squalor.squalify.Cli`). It profiles one data file and writes a deterministic JSON descriptor. It does not reference DataBall; the schema in [`docs/descriptor.md`](docs/descriptor.md) is the contract. If you use squalify rather than change it, start with the [user guide](docs/user-guide.md).

Coding agents: [`AGENTS.md`](AGENTS.md) adds the agent-specific rules on top of this page.

## Prerequisites

The .NET SDK pinned by [`global.json`](global.json) (10.0.x). Nothing else is needed to build and test. Rendering the diagram docs also needs quire (see [Docs](#docs)).

## Layout

- `src/squalify/`: the library: pipeline stages (sniff, format detection, format profilers, field profilers), the descriptor model and the writer.
- `src/squalify.Cli/`: the `squalify` tool.
- `tests/squalify.Tests/`: xUnit tests for the library. Fixtures are under `tests/squalify.Tests/fixtures/`, with golden descriptors (`<fixture>.squalify.json`) next to them.
- `tests/squalify.Cli.Tests/`: xUnit tests for the CLI (run in-process through `CommandFactory.InvokeAsync`), the NuGet packages and the Markdown docs.
- `docs/`: the guides, `descriptor.md` (schema), `design.md` (architecture), `how-it-works.md` (diagrams; the `.html` and `.pdf` are committed renders), `release.md`. `docs/reviews/` holds generic write-ups of real-sample findings; `docs/process/` holds process notes.
- `scripts/`: `render-docs.sh` and `corpus-check.sh`.
- `.github/workflows/`: `ci.yml` (build, test, format on Windows, macOS and Linux) and `release.yml`.

## Build, test, format

```bash
dotnet build squalify.sln -c Release
dotnet test squalify.sln -c Release
dotnet format squalify.sln --verify-no-changes
dotnet run --project src/squalify.Cli -c Release -- profile <file>   # run the CLI from source
```

Run the first three before you send a change; CI runs them on all three operating systems.

## Tests

<<<<<<< HEAD
- **Categories.** `Category=Package` tests pack both NuGet packages and check them (they run `dotnet`, so CI runs them as their own step). `Category=Corpus` tests profile a private directory and are excluded from CI. `Category=Large` tests stream gigabytes to bound process memory; they take a long time and are run by hand. Everything else is plain xUnit. CI's test step is `--filter "Category!=Corpus&Category!=Package&Category!=Large"`; locally, `--filter "Category!=Corpus&Category!=Large"` runs everything CI runs.
- **Throughput benchmark.** `tests/squalify.Tests/Bench/ThroughputTests.cs` measures squalify on generated streams (no file on disk): a plain 12-column table and a wide padded one (256 columns of 64-byte right-padded cells, a key preamble, annotation rows and a header, so about 89 percent of its bytes are padding; 256 fields is at most 547, so the sketches run at full precision, level 0 of [Bounded memory and sketches](docs/design.md#bounded-memory-and-sketches); the wide shape therefore does not exercise sq-38's precision scaling, which the 2,000-column test in the suite does). For each shape it times reading the source alone, the byte pass alone and the full profile, and records wall time, MB/s (10^6 bytes), cells per second (data rows x columns) and the peak working set, each in a fresh child process. Run it by hand, alone, with `dotnet test tests/squalify.Tests/squalify.Tests.csproj -c Release --filter "FullyQualifiedName~ThroughputTests&Category=Large" --logger "console;verbosity=detailed"`: `Bench_PaddedWide_1Gb` and `Bench_Plain_1Gb` assert that the growth of the peak working set over a small-stream baseline stays within `MemoryBudget.CeilingBytes`; `Bench_PaddedWide_4Gb` only records. Timings are never asserted. The memory assertions assume the default workstation concurrent GC: under server or non-concurrent GC the growth can exceed the ceiling by GC slack (seen at 16 MiB), so the ceiling is asserted only by the Large tests. The same code runs at 16 MiB in ordinary tests, which check that every pass counted every byte and that the peak could be read (and that the ceiling assertion itself fails for a low ceiling), and by hand at any size with `dotnet tests/squalify.Tests/bin/Release/net10.0/squalify.Tests.dll --throughput-probe Full PaddedWide 268435456` (passes `Drain`, `BytePass`, `Full`; shapes `Plain`, `PaddedWide`). Each result prints a readable line, a raw `key=value` line to paste into a note, and a context line (OS, architecture, runtime, CPU count, GC mode, library version, commit); the commit is not in the assembly, so set `SQUALIFY_BENCH_COMMIT=$(git rev-parse --short HEAD)` for the run to record it. The figures inform any decision to fuse the passes; the benchmark changes no design.
=======
- **Categories.** `Category=Package` tests pack both NuGet packages and check them: each nupkg holds the README (the repository's bytes), the XML doc file and the license expression, plus the icon, the symbols, the tool command, and a consumer that restores the library from a local feed and writes the CLI's bytes (they run `dotnet`, so CI runs them as their own step). `Category=Corpus` tests profile a private directory and are excluded from CI. `Category=Large` tests stream gigabytes to bound process memory; they take a long time and are run by hand. Everything else is plain xUnit. CI's test step is `--filter "Category!=Corpus&Category!=Package&Category!=Large"`; locally, `--filter "Category!=Corpus&Category!=Large"` runs everything CI runs.
>>>>>>> main
- **Golden descriptors.** Every supported format has golden tests: the descriptor for a fixture is compared byte for byte with `<fixture>.squalify.json`. After an intended output change, rewrite them with `SQUALIFY_UPDATE_GOLDEN=1 dotnet test squalify.sln -c Release`, then review the diff before you commit it.
- **Determinism tests** profile the same fixture twice, and under a different culture, and compare the bytes.
- **Fixtures.** No GB-scale fixtures in the repository; large-file behavior is tested with generated streams. Real samples never enter the repository: write findings up generically under `docs/reviews/`.
- **Private corpus.** `scripts/corpus-check.sh <dir>` profiles a directory outside the repository (determinism, repeat runs, goldens next to the files). It runs by hand.
- **Docs tests.** Relative links in `docs/*.md`, `CONTRIBUTING.md` and `AGENTS.md` must resolve, fixtures named in the guides must exist, and the README, which ships in both packages and renders on nuget.org, must use only absolute URLs.
- **Mutation-check new tests:** break the product code, confirm the test goes red for the right reason, then revert.

## Code rules

- Every source file starts with `// SPDX-License-Identifier: Apache-2.0`. The namespace is `squalor.squalify` (with `.Cli` for the tool). Source is ASCII.
- Nullable is on. Public members carry XML doc comments.
- Public failures surface as `SqualifyException`. Wrap lower-level exceptions instead of leaking IO types.
- **Determinism is a hard rule.** Output must not depend on the current time, culture, machine, path, thread scheduling, hash-seed randomization or dictionary enumeration order. Format numbers with the invariant culture and shortest round-trip formatting, and sort every collection by a documented key. Output-affecting math uses only IEEE `+ - * /` and `Math.Sqrt`; logarithms go through `DetMath` and sketch hashing through `XxHash64`; never use `Math.Log`, `Exp` or `Pow`, or `string.GetHashCode`, in output paths. The one runtime dependency is the descriptor writer's escaping of unassigned code points, so descriptor bytes are guaranteed only within one .NET runtime major version; fingerprints are runtime-independent. The full rules are in [`docs/design.md`](docs/design.md#determinism-rules).
- No ML, no LLM calls, no network access.
- Literal data values appear in a descriptor only under `--include-values` or `--include-values-for` (see `ValuesPolicy` and [the values tier](docs/design.md#values-tier)).
- Prefer the BCL and in-repo code. Ask the owner before adding any package. Approved dependencies: System.CommandLine 2.0.11 (CLI), and in the test projects only Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3 and xunit.runner.visualstudio 3.1.5.
- A descriptor schema change updates `docs/descriptor.md` and bumps `schemaVersion` according to the policy there, in the same commit.
- README and `docs/` change in the same commit as the behavior they describe.

## Docs

- The README is a front page and is packed into both NuGet packages: keep it short, and link to the guides with absolute `https://github.com/squalor-xyz/squalify/blob/main/...` URLs. Other docs use relative links.
- Guides ([getting started](docs/getting-started.md), [user guide](docs/user-guide.md), [examples](docs/examples.md)): every command and output excerpt must come from a real run on a committed fixture. Move content instead of duplicating it.
- `docs/how-it-works.md` explains the pipeline with Mermaid diagrams, and its `.html` and `.pdf` are generated from it with quire's `squalor` profile and committed together. Run `./scripts/render-docs.sh`, then check that the HTML has one `<svg` per Mermaid block and that the PDF shows no clipped diagram, and never edit the renders by hand. To render another doc, add its name to `DOCS` in the script.
- Rendering needs quire on `PATH` (it may live in `~/.local/bin`, which a non-interactive shell does not have: prefix the command with `PATH="$HOME/.local/bin:$PATH"`), its Mermaid renderer (`python3 install.py --with-mermaid` in the quire checkout), and the `squalor` profile in quire's user config.
- Diagram rules: plain-language labels with the type or method name in `<small>` text; one idea per diagram; each diagram names its source file, or "(planned)" until the code exists.

## Tracking and slices

Roadmap, slices and goals are tracked with the `slicer` CLI (`slicer ai` explains the workflow): `slicer next` shows the next ready slice and `slicer show <id>` prints its spec. Change that state only through `slicer`, never by editing `.slicer/`. Work for a slice happens on a branch `<id>` in a git worktree under `.worktrees/<id>` (git-ignored). Write the slice's named tests first and watch them fail for the stated reason.

## Releases

A release is a `vX.Y.Z` tag equal to `<Version>` in `Directory.Build.props`; the release workflow publishes both packages to nuget.org. Steps and the workflow's checks are in [`docs/release.md`](docs/release.md).

## License

Contributions are licensed under Apache-2.0, as in section 5 of the [license](LICENSE).
