# User guide

Reference for using squalify: the CLI, the library, the formats, the values policy, the guarantees and troubleshooting. New here? Start with [Getting started](getting-started.md); for recipes see [Examples](examples.md); for every descriptor member see [descriptor.md](descriptor.md).

## CLI

```text
squalify profile <path> [options]

  -o, --output <output>                      Write the descriptor to this file instead of stdout
  --include-values                           Include literal data values in the descriptor
  --include-values-for <include-values-for>  Include literal values only for the named preamble key, annotation label,
                                             key=value key or column (repeatable; matched whole, ASCII case-folded)
  --max-bytes <max-bytes>                    Read at most this many bytes, cut at a whole line; adds a scope section, and
                                             no file.sha256 unless the whole file was read (see Reading only a prefix)
  --verbose                                  Print full exception details
  -?, -h, --help                             Show help and usage information

squalify --version
```

- **One file per run.** `<path>` is a file. A directory is an error (see [Examples](examples.md#profile-a-directory) for a loop).
- **Output.** The descriptor is UTF-8 JSON with LF line endings and no BOM. Without `-o` it goes to stdout and nothing else is printed there. With `-o` the file is written to `<file>.tmp` and then moved over the target, so a reader sees the old file or the new one, never half of one; stdout stays empty. A failed run leaves stdout and an existing output file untouched.
- **Exit codes.** 0 on success; 1 on any error: a missing file, a directory, an unreadable file, an unwritable output, or a usage error (an unknown command, a missing argument). The message goes to stderr: for a file or output error it is one line, which includes the underlying reason; a usage error prints the message followed by the help text. `--verbose` adds the underlying exception's type and stack trace when there is one (an unwritable output, for example); for a missing file or a directory it adds nothing. Problems in the file's content are not errors: they are listed in `issues` and the exit code is 0.
- **`--version`** prints the package version. It is the same string as `tool.version` in every descriptor.

## Library

Add `squalor.squalify` (.NET 10). It holds the same profiler the CLI uses.

```csharp
using squalor.squalify;

var options = ProfileOptions.Create(includeValues: false, includeValuesFor: ["Lot"]);
Descriptor descriptor = Profiler.Profile("data.csv", options);
byte[] json = DescriptorWriter.ToBytes(descriptor);   // the exact bytes the CLI writes
```

| Member | Use |
|---|---|
| `Profiler.Profile(string path, ProfileOptions options)` | Profile a file. |
| `Profiler.Profile(IProfileSource source, ProfileOptions options)` | Profile other bytes. An `IProfileSource` has a bare `Name` (no `/` or `\`; it is the only name that reaches the descriptor), a `Length`, and `Open()`, which returns a fresh stream for each pass. |
| `ProfileOptions.Create(includeValues, includeValuesFor, maxBytes)` | The options; the allow-list is stored as a sorted set. `maxBytes` is optional (null reads the whole source); see [Reading only a prefix](#reading-only-a-prefix). |
| `DescriptorWriter.ToBytes(descriptor)`, `DescriptorWriter.Write(descriptor, stream)` | Write the canonical JSON bytes. |
| `Descriptor` | The model: `Tool`, `Options`, `Limits`, `File`, `Encoding`, `Format`, `Kind`, `Layout`, `Fingerprint`, `Hints`, `Issues`, `Evidence`, and `Table`, `Sections`, `Binary`, `Stdf` or `Scope` when they apply. Plain records, mirroring [descriptor.md](descriptor.md). |
| `SqualifyException` | Every public failure: a missing file, a directory, an unreadable source, an invalid source name. Lower-level IO exceptions are wrapped. |

A complete program is in [Examples](examples.md#use-the-library-to-pick-an-import-route). The library reads the file; it does no network access and holds no state between calls.

## Formats

| Family | Today | What is described |
|---|---|---|
| Delimited text | Fully profiled | CSV, TSV, `;`, `\|` or whitespace separated. Delimiter, quoting, padding and decimal separator; preamble, annotation rows (such as limits), header, units row, data and footer regions; repeated header-and-table blocks (one `block` region each, see [descriptor.md](descriptor.md#several-blocks)); row classes; per-field profiles (type, nulls, null tokens and exact null spellings, statistics, shapes, units from header names). |
| Plain text | Generic profile | Encoding, line endings, line kinds, longest line. |
| Binary | Generic profile | Named magic (gzip, zip, pdf, sqlite, hdf5, `parquet` and others), byte histogram, entropy. Nothing is decompressed or unpacked. |
| Empty | Generic profile | A zero-length file has family `empty`. |
| Touchstone (`.sNp`, `.ts`) | Planned | Profiled as text for now. |
| Key=value / INI | Planned | Profiled as text for now. |
| STDF V4 | Planned | Profiled as binary, with the `stdf-le` or `stdf-be` magic hint, for now. |

Parquet is not in v1: a Parquet file gets the binary profile and the `parquet` magic hint. Also planned: the `kind` label beyond `text`, `binary` and `empty`. Malformed content is described, not rejected: problems become `issues` and the descriptor is still written. The issue codes are listed in [descriptor.md](descriptor.md#issues).

## Values policy

By default a descriptor holds names, structure and statistics, but no literal data values.

| Always included | Only with `--include-values` |
|---|---|
| Header names, preamble keys, annotation labels | Preamble values, annotation-row cells, comment text |
| Counts, nulls, null spellings, types, shapes, lengths | Top values and text min/max of a column |
| Numeric and temporal min, max, mean, quantiles (not for a constant column) | Units-row text, statistics of a constant column |
| Units parsed from header names | Value-bearing `hints.stableLines` regexes (comment and text lines) |

`--include-values-for <name>` opens only the named preamble key, annotation label or column, compared whole with ASCII case folding. A name that matches several members opens all of them; a name that matches nothing is reported as the issue `includeValuesForUnmatched`. The complete list is in [descriptor.md](descriptor.md#values-tier), and the reasoning in [design.md](design.md#values-tier). A value that may not be written is never held in memory.

Header names and preamble keys are always included, and can themselves identify a file. Review a descriptor before sharing it.

## Guarantees and stability

- **Deterministic.** The same input bytes, options and squalify version give the same descriptor, byte for byte, on any OS, CPU or culture, within one .NET runtime major version. The JSON writer takes its escaping of unassigned characters from the runtime's Unicode tables, so moving to another runtime major can change those bytes; squalify does not guard against that, so pin the package version and the runtime ([descriptor.md](descriptor.md#canonical-form)). Fingerprint hashes are identical across runtimes. Output never depends on time, path, machine or thread timing, and there is no machine learning, no LLM and no network access.
- **Describe, don't decide.** squalify does not match files against known types, propose configs or convert anything. Consumers do that from the descriptor.
- **Explainable.** Every inference has an `evidence` entry with its candidates and scores.
- **Versions.** The package version, `squalify --version` and `tool.version` are one string. The schema is a draft until the v1 schema freeze: before 1.0 a minor version may change the descriptor. After the freeze, adding optional keys bumps the schema's minor version and removing or renaming one bumps the major. Consumers must ignore unknown keys. See [the versioning policy](descriptor.md#versioning-policy).
- **Fingerprints.** `fingerprint.version` changes whenever a projection changes. Compare hashes only when it matches.
- **Breaking changes in 0.2.0 (library).** `ProfileOptions` and `ProfileOptions.Create` gained a `maxBytes` parameter (optional, so source-compatible; the signatures changed, so a compiled consumer must be recompiled). `FileSection.Sha256` is now nullable: it is absent when only a prefix was read. A descriptor made without a byte limit is unchanged. `FieldProfile` gained the trailing optional members `NullSpellings` (the null values by exact spelling, outside all-null records; never null from the profiler, empty when there is none) and `NullSpellingsOther`; a compiled consumer that constructs a `FieldProfile` must be recompiled, and `NullTokens` is unchanged.

## Limits and large files

The whole file is streamed in bounded memory and nothing is sampled (unless you set `--max-bytes`, which reads only a prefix; see [Reading only a prefix](#reading-only-a-prefix)): counts, min, max, mean, standard deviation and nulls are exact for any size. The expensive per-column statistics are exact up to fixed caps, recorded in the descriptor's `limits`:

| Statistic | Exact up to |
|---|---|
| Distinct values per column | 10,000 |
| Quantiles per column | 100,000 values |
| Shape masks per column | 64 masks, and 53,312 characters of mask text |
| Regions | 1,000 |
| Header-detection sample | 1,000 records |
| Row classes | 64 |
| Null spellings per column | 64 spellings of at most 256 characters (fixed; the rest are counted in `nullSpellingsOther`) |

A global budget shrinks the first two caps, and the mask text, for very wide tables (100 columns or fewer keep the full caps); `limits.distinctPerField`, `limits.quantilePerField` and `limits.shapeCharsPerField` show the effective values. Past a cap a deterministic sketch takes over and the descriptor says so: `distinct` is a HyperLogLog estimate with `approximate: true` and `relativeStdError` (0.008125, one standard error), and `numeric` and `temporal` quantiles come from a Greenwald-Khanna summary with `approximate: true` and `rankError` (a sibling of `quantiles` and `approximate`, 0.001, the worst-case rank error as a fraction of the values). Both members are absent while the value is exact. `min`, `max`, `mean` and `stddev` do not depend on the caps. The sketches have a fixed size per column, so a very wide table pays for them in memory. See [design.md](design.md#bounded-memory-and-sketches).

## Reading only a prefix

`--max-bytes <n>` (library: `ProfileOptions.MaxBytes`) profiles the start of a file instead of all of it, for a quick look at a large file or a classification that a longer read will confirm. squalify reads at most `n` bytes and cuts at the last whole line, so the profile ends on a whole record; a file whose first `n` bytes are binary is cut at `n` exactly. There are two exceptions to "at most `n`": a CRLF that straddles the limit adds one code unit, and **a first line longer than `n` is read to its end, however long, with no ceiling** (a file with no line terminator is read whole). The profile is therefore never empty for a file that has data, and `scope.bytesRead` then exceeds `n`. The descriptor says what it read in a top-level `scope`, and `file.size` stays the length of the whole file:

```bash
squalify profile sweep-preamble.csv --max-bytes 310 | jq -c '{file, scope, rows: .table.rows, regions: [.layout.regions[] | [.kind, .lines[0], .lines[1]]]}'
```

```json
{"file":{"name":"sweep-preamble.csv","size":448},"scope":{"maxBytes":310,"bytesRead":300,"complete":false},"rows":11,"regions":[["preamble",1,7],["header",8,8],["units",9,9],["data",10,20]]}
```

- **Everything describes the prefix.** Every section, including the text-or-binary decision and the charset, is computed from the first `scope.bytesRead` bytes alone, as for a file that holds just those bytes. A `footer` that the file has past the cut is not seen. The record caps in `limits` limit how a record is profiled, not how much of it is read.
- **`file.sha256` is absent unless the whole file was read.** It is absent while `scope.complete` is false, because the hash of the whole file needs a read of all of it. With a limit at or above the file size, `scope.complete` is true, `sha256` is written and the rest of the descriptor equals the unlimited one.
- **No limit, no `scope`.** Without `--max-bytes` the descriptor has no `scope` member and is unchanged.
- **A zero or negative limit is an error.** The library needs `IProfileSource.Length` to be the whole size; a source that cannot report it should not be limited.
- **A quoted field with line breaks** that straddles the limit is cut at a line break inside it, so the last record of the prefix is a short one.

## Troubleshooting

**squalify says my text file is `binary`.** The byte pass calls a file binary when it has a known magic, a NUL byte (other than a UTF-16 or UTF-32 pattern), or more than 5% control bytes. Check `encoding.kind`, `encoding.controlBytes` and `hints.magic`, and the `evidence` entry for `encoding`. A UTF-16 file with a byte order mark is text; one without is text only when its NUL bytes follow the UTF-16 pattern. See [how it works, section 12](how-it-works.md#12-text-binary-or-empty).

**The delimiter or header is wrong.** Read `evidence`, stage `dialect` (topic `delimiter`) and stage `header`: each lists the candidates with scores and reasons. A header-less comma CSV whose first column is multi-word text and whose last column is an integer can be read as whitespace-delimited (a [known limitation](design.md#delimited-text)); a header row or a comma inside a cell usually settles it. A file with no viable delimiter gets the generic text profile, with the issue `tooIrregular` when a candidate was rejected as too irregular. Use `hints.dataStartLine` and `layout.regions` to see where squalify thinks the data begins.

**The encoding is `latin-1`, or I see `invalidUtf8`.** A file that is not valid UTF-8 is called `latin-1` (the evidence notes "possibly windows-1252" when bytes 0x80 to 0x9F occur). A few stray invalid bytes in otherwise valid UTF-8 or ASCII are tolerated (at most 8, and at most one per 64 KiB), reported as the issue `invalidUtf8` with byte offsets, and decoded as U+FFFD.

**I see `invalidUnicode`.** The file is UTF-16 or UTF-32 and holds a lone surrogate, a stray last byte or an impossible UTF-32 value. `encoding.invalidBytes` counts their bytes and the issue lists the byte offsets of the first 10. Unlike `invalidUtf8` there is no allowance: even one invalid unit is reported.

**The file is empty.** A zero-length file gets family `empty` and kind `empty`, with no error.

**I passed a directory.** Directories are not supported: the run fails with exit code 1. Loop over the files ([example](examples.md#profile-a-directory)).

**My STDF, Touchstone or Parquet file looks wrong.** They are not profiled in 0.1.x. STDF and Parquet get the binary profile with a magic hint (`stdf-le`, `stdf-be`, `parquet`), and Touchstone gets the text profile.

**A descriptor changed after an upgrade.** Expected before 1.0 while the schema is a draft. Pin the package version and the .NET runtime major version, and compare `tool.version` and `schemaVersion` in the descriptors.

**Where is the data?** Not in the descriptor, unless you opt in ([values policy](#values-policy)).

Found a bug or a file squalify mishandles? Report it in the [GitHub repository](https://github.com/squalor-xyz/squalify) and include the descriptor, not the file. See [Contributing](../CONTRIBUTING.md) to fix it yourself.
