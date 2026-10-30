<img src="https://raw.githubusercontent.com/squalor-xyz/squalify/main/assets/squalify-512.png" alt="squalify" width="96">

# squalify

squalify profiles one data file and writes a **descriptor**: a versioned JSON document that says what the file is (encoding, format, dialect), how it is laid out (preamble, header, units row, data, footer), what each column holds (type, null tokens, statistics, units), what looks wrong (issues), and a structural fingerprint. Every inference carries the evidence behind it.

Other tools read the descriptor to decide what to do with the file: recognize a known file type, pick an import route, or draft a loader config. squalify only describes the file; it never converts or matches it.

> **Status: early (0.3.x).** Delimited text (CSV, TSV, `;`, `|`, whitespace) is fully profiled; statistics are exact up to fixed caps, then deterministic sketches marked approximate. Every other file gets the generic text or binary profile. Touchstone, key=value and STDF profilers and the `kind` label are still to come. The descriptor schema is a **draft** until the v1 schema freeze; before 1.0 a minor version may change it.

## Install

Both packages are on nuget.org (.NET 10):

```bash
dotnet tool install -g squalor.squalify.Cli  # the CLI; the command is `squalify`
dotnet add package squalor.squalify          # the library
```

## Use as a library

`squalor.squalify` runs the same profiler as the CLI. Descriptor bytes are identical for the same input, options and squalify version only within one .NET runtime major version: the JSON writer takes its text escaping of unassigned characters from the runtime, so moving to a new runtime major can change those bytes; pin the package version and the runtime. Fingerprint hashes do not depend on the runtime. Before 1.0 a minor squalify version may also change the descriptor. See the [user guide](https://github.com/squalor-xyz/squalify/blob/main/docs/user-guide.md#guarantees-and-stability).

## Quick start

The file is [`tests/squalify.Tests/fixtures/lab/sweep-preamble.csv`](https://github.com/squalor-xyz/squalify/blob/main/tests/squalify.Tests/fixtures/lab/sweep-preamble.csv): a lab export with a 7-line preamble, a header, a units row, 20 data rows and a footer.

```bash
squalify profile sweep-preamble.csv | jq -c '{family: .format.family, rows: .table.rows, regions: [.layout.regions[] | [.kind, .lines[0], .lines[1]]], issues}'
```

```json
{"family":"delimited","rows":20,"regions":[["preamble",1,7],["header",8,8],["units",9,9],["data",10,29],["footer",30,31]],"issues":[]}
```

`squalify profile data.csv` alone writes the whole descriptor (about 620 lines for this 31-line file) to stdout; `-o descriptor.json` writes it to a file. The exit code is 0 on success and 1 on any error.

## What it is for

- **Understand a file before loading it:** where the data starts, the delimiter, the header and units rows, the null tokens, the type of each column.
- **Catch schema drift:** fingerprints are the same for files with the same structure, whatever their values.
- **Gate a pipeline:** fail a CI step when a file has issues such as ragged rows.
- **Triage unknown files:** named magic (gzip, zip, pdf, sqlite, ...), encoding and line endings, without opening the file.
- **Look at a large file cheaply:** `--max-bytes` profiles just the start, cut at the last whole line, and the descriptor says how much it read. `--max-first-record-bytes` refuses a file whose first line is longer than that, without reading it. In code, `Profiler.ProfilePrefix` returns the descriptor and the exact prefix bytes it describes, from one read; the cut rule (decided from the bytes inside the limit) is documented in the user guide as a contract.
- **Feed other tools:** the same deterministic JSON on any OS (within one .NET runtime major version; see [Use as a library](#use-as-a-library)), with no data values unless you opt in.

## Docs

| Page | For |
|---|---|
| [Getting started](https://github.com/squalor-xyz/squalify/blob/main/docs/getting-started.md) | Install, profile a first file, read the result |
| [User guide](https://github.com/squalor-xyz/squalify/blob/main/docs/user-guide.md) | CLI and library reference, formats, values policy, guarantees, troubleshooting |
| [Examples](https://github.com/squalor-xyz/squalify/blob/main/docs/examples.md) | Task recipes with real output |
| [Contributing](https://github.com/squalor-xyz/squalify/blob/main/CONTRIBUTING.md) | Build, test, format, docs, releases |
| [Descriptor schema](https://github.com/squalor-xyz/squalify/blob/main/docs/descriptor.md) | Every member of the descriptor (schema v1, draft) |
| [Design](https://github.com/squalor-xyz/squalify/blob/main/docs/design.md) | Architecture, pipeline, determinism rules, memory bounds |
| [How it works](https://github.com/squalor-xyz/squalify/blob/main/docs/how-it-works.md) | Diagrams of a profile run ([PDF](https://github.com/squalor-xyz/squalify/blob/main/docs/how-it-works.pdf)) |
| [Releasing](https://github.com/squalor-xyz/squalify/blob/main/docs/release.md) | How a release is cut |

squalify has no dependency on [DataBall](https://github.com/squalor-xyz/DataBall); the descriptor schema is the contract.

## License

Apache-2.0. See [LICENSE](https://github.com/squalor-xyz/squalify/blob/main/LICENSE).
