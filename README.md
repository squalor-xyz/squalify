# squalify

squalify profiles one data file and writes a **descriptor**: a versioned JSON document that says what the file is (encoding, format, dialect), how it is laid out (preamble, header, units row, data, footer), what each column holds (type, null tokens, statistics, units), what looks wrong (issues), and a structural fingerprint. Every inference carries the evidence behind it.

Other tools read the descriptor to decide what to do with the file: recognize a known file type, pick an import route, or draft a loader config. squalify only describes the file; it never converts or matches it.

> **Status: v0.1.0, early.** Delimited text (CSV, TSV, `;`, `|`, whitespace) is fully profiled. Every other file gets the generic text or binary profile. Touchstone, key=value and STDF profilers, sketches for very large columns, repeated blocks and the `kind` label are still to come. The descriptor schema is a **draft** until the v1 schema freeze; before 1.0 a minor version may change it.

## Install

Both packages are on nuget.org (.NET 10):

```bash
dotnet tool install -g squalor.squalify.Cli  # the CLI; the command is `squalify`
dotnet add package squalor.squalify          # the library
```

## Quick start

```bash
squalify profile data.csv                      # descriptor to stdout
squalify profile data.csv -o data.squalify.json
```

The exit code is 0 on success and 1 on any error (a missing file, a directory, an unreadable file), with a one-line message on stderr.

## What you get

A lab export (`tests/squalify.Tests/fixtures/lab/sweep-preamble.csv`): a 7-line preamble (`Lot=A12`, `Wafer: 7`, ...), the header `Freq(MHz),Pout(dBm),Gain(dB)`, a units row `MHz,dBm,dB`, 20 data rows and a 2-line footer. `squalify profile sweep-preamble.csv` writes about 620 lines of JSON; the most telling parts, compacted (`...` marks cuts):

```json
"format": { "family": "delimited",
            "dialect": { "delimiter": ",", "hasHeader": true, "unitsRow": true, "decimalSeparator": ".", ... }, ... },
"layout": { "regions": [
  { "kind": "preamble", "lines": [1, 7], "keys": ["Lot", "Wafer", "Temp", "Operator"], ... },
  { "kind": "header", "lines": [8, 8], ... }, { "kind": "units", "lines": [9, 9], ... },
  { "kind": "data", "lines": [10, 29], ... }, { "kind": "footer", "lines": [30, 31], ... } ], ... },
"table": { "rows": 20, "fields": [ ...,
  { "name": "Pout(dBm)", "nameParts": { "base": "Pout", "unit": "dBm", "convention": "nameParenUnit", ... },
    "type": "float", "nulls": 0, "nullTokens": {},
    "numeric": { "min": 10, "max": 19.5, "mean": 14.75, "quantiles": { "p50": 14.5, ... }, ... },
    "shapes": [{ "mask": "9{2}.9", "count": 20 }], "monotonic": "increasing", "unique": true,
    "top": null, "text": null, ... }, ... ] },
"fingerprint": { "version": 1, "structure": "sha256:4bb113e3...", "typed": "sha256:9f8de819...", ... },
"issues": [],
"evidence": [ ...,
  { "stage": "dialect", "topic": "delimiter", "chosen": ",",
    "candidates": [{ "value": ",", "score": 0.7096774193548387, "reason": "k=3; 22/31 records; clean=1; ..." }, ...] }, ... ]
```

- **No data values by default.** Preamble keys and header names are included; `A12`, units-row text, top values and text min/max are not.
- Region lines are 1-based and inclusive. `kind` is an interim `text`, `binary` or `empty` label until the kind rules land.

Top-level sections: `file` (name, size, SHA-256), `encoding`, `format`, `kind`, `layout`, `table` (delimited) or `binary`, `fingerprint`, `hints` (inputs for detect rules), `issues` and `evidence`. The full schema is in [`docs/descriptor.md`](https://github.com/squalor-xyz/squalify/blob/main/docs/descriptor.md).

## Examples

**A padded export with a limits block and a broken row.** `tests/squalify.Tests/fixtures/delimited/padded-annotated.csv` has `Key :,value` lines, `Upper_limit`/`Lower_limit`/`Unit` rows above the header, `NA.` for missing values and one row missing a comma:

```json
"padding": { "trim": true, "constantLineLength": true, "lineLength": 74, ... }
{ "kind": "annotation", "lines": [6, 9], "rows": [{ "line": 6, "label": "Prog_line", "type": "int", ... }, ...] }
{ "name": "Gain(dB)", "type": "float", "nulls": 2, "nullTokens": { "NA.": 2 }, ... }
"issues": [{ "code": "raggedRows", "count": 1, "firstLines": [17] }]
```

A loader for this file needs: skip 10 lines, trim padding, treat `NA.` as null, expect line 17 to be short.

**Opting in to values.** `--include-values` adds preamble values (`"values": ["A12", "7", "25", "jr"]`), units-row text (`"unit": "MHz"`), top values and text min/max. `--include-values-for` opens only named keys, labels and columns:

```bash
squalify profile lab-sweep.csv --include-values-for Mode
```

```json
{ "name": "Mode", "top": { "items": [{ "value": "OFDM", "count": 5 }, { "value": "CW", "count": 3 }], ... },
  "text": { "min": "CW", "max": "OFDM" }, ... }
```

A name that matches nothing is reported as the issue `includeValuesForUnmatched`.

**Schema-drift check.** Fingerprints hash a value-free projection, so same-structure files hash the same whatever their values:

```bash
for f in monday.csv tuesday.csv wednesday.csv; do echo "$f $(squalify profile "$f" | jq -r .fingerprint.typed)"; done
```

```text
monday.csv sha256:9f8de819f1eaca5a6436ec7e9a99f8f52dfb2cc089a841a48fe3812586742e3d
tuesday.csv sha256:9f8de819f1eaca5a6436ec7e9a99f8f52dfb2cc089a841a48fe3812586742e3d
wednesday.csv sha256:ab6a7325f88d25e61fb3d22251d2a93b80d36e29e9b8d0b8291796894241f283
```

`tuesday.csv` has a different lot and readings (same hash); `wednesday.csv` adds an `Icc(mA)` column (new hash). `structure` ignores names; `names` tracks only the column-name set. Compare hashes only when `fingerprint.version` matches.

**Triage an unknown file** (`tests/squalify.Tests/fixtures/binary/sample.gz`, renamed; nothing is decompressed):

```bash
squalify profile mystery.bin | jq -c '{family: .format.family, magic: .hints.magic}'
# {"family":"binary","magic":{"name":"gzip","offset":0,"hex":"1f8b"}}
```

**CI gate.** `squalify profile data.csv | jq -e '.issues == []' > /dev/null` exits non-zero when any issue is found.

## Formats

| Family | In 0.1.0 | What is described |
|---|---|---|
| Delimited text | Yes | CSV, TSV, `;`, `\|` or whitespace separated; delimiter, quoting, padding and decimal separator; preamble, annotation rows, header, units row, data and footer; row classes; per-field profiles; header-unit convention |
| Plain text | Yes | Encoding, line endings, line kinds, longest line |
| Binary | Yes | Named magic (gzip, zip, pdf, sqlite, hdf5, `parquet` and others), byte histogram, entropy |
| Touchstone (`.sNp`, `.ts`) | Planned | Profiled as text for now |
| Key=value / INI | Planned | Profiled as text for now |
| STDF V4 | Planned | Profiled as binary, with the `stdf-le` or `stdf-be` magic hint, for now |

Parquet is not in v1: a Parquet file gets the binary profile and the `parquet` magic hint.

## CLI reference

```text
squalify profile <path> [options]

  -o, --output <file>          Write the descriptor to a file instead of stdout
  --include-values             Include literal data values
  --include-values-for <name>  Include values only for this preamble key, annotation label,
                               key=value key or column (repeatable; whole name, ASCII case-folded)
  --verbose                    Print full exception details on error

squalify --version
```

## Library

The `squalor.squalify` package holds the same profiler the CLI uses:

```csharp
using squalor.squalify;

var descriptor = Profiler.Profile("data.csv", ProfileOptions.Create(includeValues: false, includeValuesFor: []));
byte[] json = DescriptorWriter.ToBytes(descriptor);   // the exact bytes the CLI writes

Console.WriteLine($"{descriptor.Format.Family} {descriptor.Fingerprint.Typed} issues={descriptor.Issues.Count}");
```

Failures (missing file, directory, unreadable file) throw `SqualifyException`. `Profiler.Profile(IProfileSource, ProfileOptions)` profiles a source other than a file path.

## Determinism and principles

- **Deterministic.** The same bytes and the same squalify version give the same descriptor, byte for byte, on any OS, CPU or culture. No machine learning, no LLM, no network.
- **Describe, don't decide.** No matching against known types, no config proposals, no conversion. Consumers do that from the descriptor.
- **Full scan, bounded memory.** The whole file is streamed. Statistics are exact up to fixed caps (`limits`); past a cap the descriptor says so rather than guessing.
- **No data values by default, and explainable.** Literal values appear only with `--include-values` or `--include-values-for`; every inference has an `evidence` entry.

**Versions.** The package version, `squalify --version` and `tool.version` in every descriptor are the same string. Before 1.0, and until the schema freeze, a minor version may change the descriptor schema.

## Build from source

```bash
dotnet build squalify.sln -c Release
dotnet test squalify.sln -c Release
dotnet run --project src/squalify.Cli -c Release -- profile <file>
dotnet pack src/squalify/squalify.csproj -c Release -o ./nupkg   # local feed
```

Releases: [`docs/release.md`](https://github.com/squalor-xyz/squalify/blob/main/docs/release.md). Contributing: [`AGENTS.md`](https://github.com/squalor-xyz/squalify/blob/main/AGENTS.md).

## Relationship to DataBall

squalify has no dependency on [DataBall](https://github.com/squalor-xyz/DataBall); the descriptor schema is the contract. Using descriptors in DataBall (matching files, proposing file types) is tracked in DataBall's roadmap.

## Docs

- [How it works](https://github.com/squalor-xyz/squalify/blob/main/docs/how-it-works.md): diagrams of a profile run ([PDF](https://github.com/squalor-xyz/squalify/blob/main/docs/how-it-works.pdf)).
- [Design](https://github.com/squalor-xyz/squalify/blob/main/docs/design.md): architecture, pipeline, determinism rules, memory bounds.
- [Descriptor schema](https://github.com/squalor-xyz/squalify/blob/main/docs/descriptor.md): schema v1 (draft).
- [Reviews](https://github.com/squalor-xyz/squalify/tree/main/docs/reviews): generic findings from real sample data.

## License

Apache-2.0. See [LICENSE](https://github.com/squalor-xyz/squalify/blob/main/LICENSE).
