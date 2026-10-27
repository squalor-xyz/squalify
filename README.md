# squalify

squalify profiles a data file and writes a **descriptor**. The descriptor is a versioned JSON document that summarizes the file's encoding, format, layout, fields, statistics and structural fingerprint, along with the evidence behind each of those inferences.

Other tools read the descriptor to decide what to do with the file. Examples: classifying it as a known file type, picking the right transform or import route, or drafting a [DataBall](../../DataBall/) file type. squalify itself only describes the file. It suggests a generic `kind` label (for example `labSweepTable` or `stdfTestLog`) from a fixed set of built-in rules, and it reports how header names carry units (for example `Vcc(V)` or `ICC_A`).

> Status: early build. The solution skeleton, the descriptor model, the canonical JSON writer (with its determinism kit), the byte pass, the value classifier, the generic profiles and the delimited-text profiler exist. `squalify profile <file>` already writes a valid descriptor for any file (file hash, encoding, text, binary or empty, line endings, byte statistics for binary files). For a delimited table it also finds the delimiter and quoting, the preamble, annotation rows, header, units row, data and footer regions (with line and byte ranges), the padding and decimal separator, and a profile of every field: its type with the votes, null tokens, numeric and date statistics (exact up to fixed caps), lengths, shape masks, monotonicity and the constant and unique flags. Every file gets four structural fingerprints (hashes of a value-free projection) and the detect-rule hints: the extension, the structured magic, and for a delimited table the header names, a header-prefix, the columns without nulls, and portable regexes for the header and preamble lines. A directory is an error. The other format profilers (Parquet, Touchstone, key=value, STDF), the sketches that replace the exact distinct counts and quantiles past their caps and the kind label are still to come; until then those descriptor sections hold fixed placeholder values (the Parquet and STDF hints are null). The roadmap lives in slicer (`slicer goals`, `slicer list`).

## Principles

- **Deterministic.** The same input bytes and the same squalify version always give the same descriptor, byte for byte. squalify uses no machine learning, no LLM and no network.
- **Describe, don't decide.** squalify does not match files against a library of known types, propose configs or convert data. Consumers do that from the descriptor. The built-in `kind` label is a generic hint, and the evidence shows how it was chosen.
- **Full scan, bounded memory.** squalify streams the whole file. The statistics are exact where that is affordable and come from deterministic, documented approximations beyond fixed caps. The descriptor marks every approximate value as approximate.
- **No data values by default.** The default descriptor holds structure, types, counts, ranges, shape masks and hashes. Literal values (top-k values, min/max text, preamble text, and the statistics of columns that hold one value for the whole file) appear only with `--include-values`, or for named keys and columns with `--include-values-for`. Column header names are always included, because matching needs them.
- **Explainable.** Every inference (encoding, delimiter, header row, field type, format) has an evidence entry that lists the candidates considered and why the winner won.

## Usage

```bash
squalify profile data.csv                     # descriptor to stdout
squalify profile data.csv -o data.squalify.json
squalify profile data.csv --include-values     # include literal values (preamble values, annotation cells, units-row text)
squalify profile data.csv --include-values-for Program   # values of named keys/columns only (planned; the library already honors it for delimited files)
squalify profile data.csv --verbose            # print full exception details on error
squalify --version
```

## Formats in v1

| Family | Examples | What is described |
|---|---|---|
| Delimited text | CSV, TSV, `;` and `\|` separated, with or without a lab preamble, annotation rows above the header, a units row, padding or repeated blocks | encoding, delimiter/quoting/padding, preamble keys, annotation, header and units rows, data region, row classes, per-field profiles, header-unit convention |
| Parquet | `.parquet` | schema, row groups, per-column profiles (through DuckDB) |
| Touchstone | `.s1p` … `.sNp`, `.ts` (v1 and v2) | option line, ports, network and noise blocks, per-parameter profiles |
| Key=value documents | INI-style `[section]` + `key=value` files | sections, keys, value types |
| STDF V4 | `.stdf`, `.std` | endianness, record-type counts and order, truncation, test catalog (numbers and names) |
| Other binary | gzip, zip, unknown | named magic, byte histogram, entropy profile |

## Relationship to DataBall

DataBall picks a handler from a bounded 64 KiB `FileProbe`, and its file types declare match rules plus a config. squalify is a separate tool with no DataBall dependency: **the descriptor schema is the contract between them**. Using descriptors from DataBall (matching, or proposing file types) is DataBall's work and is tracked in DataBall's roadmap.

## Docs

- [`docs/how-it-works.md`](docs/how-it-works.md): diagrams of how a profile run works ([PDF](docs/how-it-works.pdf)).
- [`docs/design.md`](docs/design.md): architecture, pipeline, determinism rules and memory bounds.
- [`docs/descriptor.md`](docs/descriptor.md): descriptor schema v1 (draft).
- [`docs/reviews/`](docs/reviews/): generic reviews of real sample data and what they changed.
- [`profiler-idea.md`](profiler-idea.md): the original idea prompt.
- [`AGENTS.md`](AGENTS.md): how to work on this repo.

## License

Apache-2.0. See [LICENSE](LICENSE).
