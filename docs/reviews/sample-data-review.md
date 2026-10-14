# Sample-data review

Status: review, 2026-10-13. Not normative. It records what real sample files taught us about the settled design in [design.md](../design.md) and [descriptor.md](../descriptor.md), and the changes it suggested. The owner adopted them the same day; section 3.4 maps each one to its slice, and the design docs now describe the adopted behavior.

This page is deliberately generic. It names no product, program, tool, vendor, site, column, file or person, and it gives sizes and counts only as rounded magnitudes. The source material stays outside the repository.

## 1. Sources

A throwaway prototype profiler followed passes 0–2 of the design and was run over three groups of input:

| Group | What it is | Files |
|---|---|---|
| **Format P** | Production-test export. Fixed-width, right-padded comma table with a key preamble and several column-aligned annotation rows above the header. | 2 real files |
| **Format L** | Lab-bench export. Plain header-plus-rows comma CSV with interleaved record kinds and many file-constant columns. | 6 real files |
| **Generic** | Synthetic fixtures modelled on common spreadsheet and instrument exports: UTF-8 with BOM and quoted newlines, windows-1252, UTF-16 tab text, semicolon with decimal comma, plain TSV, a two-section instrument dump. | 6 synthetic files |

Prototype runs were byte-identical across repeats. That shows the prototype was deterministic; it says nothing about squalify itself. The prototype used its own placeholder rules for scores, float formatting and fingerprint canonicalization, so its numbers illustrate rather than specify.

## 2. What the samples look like

### 2.1 Format P

- **Four layers.** A key preamble (about a dozen `key<padding>:,value` lines), then a few annotation rows (a program line reference, an upper limit, a lower limit, a unit), then one header row, then data.
- **Wide and padded.** A few hundred fields per row. Every cell is left-justified and padded with spaces so that every table line has the same length, tens of KB per line. About 90% of the file bytes are spaces. Processing cost tracks cells, not bytes.
- **Small row counts in the samples, large in production.** The samples have tens of rows. Production files are said to have 10k–100k rows, which puts them in the GB range.
- **Fixed-format numbers.** Every measured value has the same number of decimals.
- **A period-suffixed null token.** Skipped measurements are written `NA.` in data cells, `NA` in the limit rows, and `NULL` in the preamble.
- **Structured header names.** Most names follow `[key=value|key=value|…]Measurement(unit)`, up to about 150 characters. Key order and spelling are not consistent within a file.
- **A corrupted preamble key.** One key carries stray control bytes, and in one file a byte that is not valid UTF-8. It looks like an uninitialized fixed-width buffer. Whether it varies per file is unknown.
- **Column set varies by recipe.** Two files from different products share every structural trait but almost no field names.

### 2.2 Format L

- **Simple physical layout.** Header on line 1, CRLF, no preamble, no footer. Quotes appear only around values that contain a comma.
- **Interleaved record kinds.** Grouping rows by which fields are non-null gives one to six row classes per file: measurement rows, setup rows at the start of each group, error rows, and rows that are entirely `NULL`. All-null rows are optional group separators and can be close to half of a file's rows. Rows switch class thousands of times in a file, so classes cannot be regions.
- **File-constant columns.** A quarter to almost nine tenths of each file's bytes sit in columns that hold one value for the whole file. These columns carry file identity: serial number, product, program, station, software version, operator and date. They play the role Format P's preamble plays.
- **Locale-dependent dates.** The date is written in the station's locale, for example `M/D/YYYY h:mm:ss AM`. With one date per file, day and month order is usually undecidable.
- **Structured strings.** Some string columns hold tuples, `;`-separated vectors, hex, bit strings or Windows paths.
- **Shortest round-trip doubles**, with .NET-style exponents for small magnitudes. No fixed decimal scale.
- **Column set varies** by unit, program and bench. About twenty column names appear in every file; the rest come and go.
- **Long-form trace files.** One file holds a single waveform: a strictly increasing time column with a uniform step and dozens of constant columns describing the stimulus condition.

### 2.3 Generic fixtures

The prototype's first version broke on five of six: UTF-16 crashed it, a BOM plus quoted newlines truncated the table, decimal commas typed numbers as strings, windows-1252 was called binary, and the two-section dump picked the small second table. Most of these are already handled by the settled design (section 3.1).

## 3. Impact on the settled design

### 3.1 Already covered

These failures of the prototype do not apply to the design as written. Each deserves a regression fixture.

| Finding | Where the design covers it |
|---|---|
| Quoted fields containing newlines | RFC 4180 tokenizer with newlines inside quotes (sq-04) |
| Two-section file picks the small table | Table choice by modal field count, not by most fields (sq-04) |
| UTF-16 with and without BOM | BOM rules and the NUL-parity rule (sq-03) |
| One stray control byte must not make a file binary | Binary only on NUL or control bytes above 5% (sq-03) |
| Per-field caps do not bound total memory on wide files | Global memory budget (sq-05) |
| Greenwald-Khanna summaries do not merge exactly across shards | Accumulation is single-threaded in input order (design, determinism rules). This becomes a constraint if parallel profiling is ever added. |
| Constant columns and padded cells | `constant` and `flags.padded` per field (sq-05) |
| Hashing is cheaper than parsing | No change; noted for capacity planning. |

### 3.2 Gaps and conflicts

Ordered by risk. "Proposal" is a suggestion for the owner, not a decision.

| # | Finding | Affects | Proposal |
|---|---|---|---|
| 1 | **Header detection.** In Format P the table's field-count run starts at the first annotation row, several rows above the real header. sq-04 tests only record 0 as header and record 1 as a units row, so record 0 (the line-reference row) becomes the header, the real header and the limit rows fall into data, and every field becomes `string` with no numeric statistics. The descriptor stays well-formed but useless. | sq-04 `HeaderDetector`; region kinds in descriptor.md | Score the first N records of the run (non-empty, non-numeric, unique, type contrast with the rows below) and record every candidate's score. Rows between the run start and the chosen header become a new `annotation` region with labels and per-row type votes. Their values stay values tier. Labels must not be hard-coded. |
| 2 | **Null vocabulary.** `NA.` is not in the list. Without it, columns that hit it type as `string`, and `typed` depends on which rows were skipped. | sq-04 `ValueClassifier` | A fixed, versioned null vocabulary that includes period-suffixed forms, with its version recorded in `limits`. Do not discover null tokens from the data, because that couples one column's type to the others. |
| 3 | **Delimiter scoring on padded files.** The whitespace-run candidate reaches nearly the same field-count consistency as comma, because padded cells split cleanly on spaces. Comma wins today only by a narrow margin and the candidate order. | sq-04 `DialectSelector` | Multiply in a clean-token share: the fraction of tokens that do not contain another candidate delimiter. |
| 4 | **Row kinds.** There is no way to express interleaved record kinds. All-null separator rows dominate every field's null count. | sq-05; new `table` member | `table.rowClasses`: rows grouped by non-null field set, each with a count, field ranges, first and last line and a stable ID, under a cap; plus the all-null row count. Per field, also report nulls that exclude all-null rows. |
| 5 | **Dates and locale.** The date patterns try `MM/dd/yyyy` before `dd/MM/yyyy`, which silently assumes a locale. 12-hour clocks and single-digit day or month are not matched, so Format L dates type as `string`. A locale with a non-ASCII AM/PM marker would still fail. | sq-04 `ValueClassifier`, sq-05 `temporal` | Record every field order the values allow and the clock, flag ambiguity, and never pick a locale. Values that fit no order stay `string`, with evidence. |
| 6 | **Identity lives in values.** Format P's program and product are preamble values; Format L's are file-constant column values. Both are values tier, so a default descriptor cannot support product or program classification for these formats. Meanwhile an integer serial number in a constant column leaks through `numeric.min/max`, while a string one does not. | sq-07 values tier; sq-05 | An allow-list opt-in per preamble key and per column name. No numeric statistics for constant fields unless values are opted in. |
| 7 | **Fingerprints.** In practice all three hashes are unique per file: column sets vary by recipe or unit, `typed` flips when a column is all-null in a short file, and a preamble key with control bytes would make every sibling's `layout` unique if the bytes vary. Nothing identifies a format family. | sq-06 | Add a name-free structure fingerprint (dialect, region-kind sequence, key syntax). Leave names with control or invalid bytes out of fingerprint inputs and raise an issue. Normalize preamble keys (trim, strip the trailing separator) for hashing and keep the raw spelling. Document that fingerprint equality is not a family test: for header-only CSVs the structure fingerprint is shared with any generic CSV, and family needs name containment. |
| 8 | **Charset with stray bytes.** One invalid byte in an otherwise ASCII file demotes the whole file to `latin-1`. Decoding stays lossless, but the charset no longer describes the file. | sq-03 | Either accept as is, or keep the dominant charset under a fixed tolerance and report each stray byte as an issue with offset and line. Either way, names with control bytes need a lossless, unambiguous JSON form. |
| 9 | **Padding and fixed width.** The dialect cannot say "every cell right-padded, every line the same length". That is the strongest family signal in Format P and makes exact row counts and byte-offset sharding possible. | sq-04 (fixed-width is listed as out of scope) | Report dialect facts only: trim rule, padded-cell share, whether line lengths are constant, and the line length. No fixed-width parser. |
| 10 | **Decimal comma.** Only a per-field `commaDecimalLike` flag exists. Semicolon exports with decimal commas type every number as `string`. | sq-04 dialect | A dialect-level decimal separator, detected only when the delimiter is not a comma. |
| 11 | **Line length.** Lines of tens of KB are normal and grow with test count. `limits` has no maximum line length. | `limits` | A recorded maximum line length; longer lines are reported as issues. |
| 12 | **Hints.** `requiredFields` reads like a requirement of the file type but is a per-file observation that changes with skipped measurements. A header `stableLines` regex is tens of KB long. There is no header-prefix hint and no file-name-shape hint, though both are stable across Format P files of different products. | sq-06 | Rename or document `requiredFields` as observed. Emit a header-prefix hint (first few names) and a value-free file-name shape mask. (A regex length cap was proposed and not adopted.) |
| 13 | **Structured header names.** sq-14's `nameParenUnit` leaves the whole bracketed condition list in `base`. A trailing unit is also sometimes doubled. | sq-14 | Either a grammar hint (pattern and key set, no interpretation), sized L, or defer and document the limitation. Format L also uses dotted prefixes, which a grammar hint could report. |
| 14 | **Descriptor size.** Wide files give descriptors of several hundred KB, mostly per-field evidence repeating type votes and long field lists. | sq-02 writer, evidence | A compact form for unanimous evidence and index references for repeated field lists. |
| 15 | **Sampling for header detection.** Format L files can start with a hundred or more error and separator rows. A head sample would mistype many columns; a strided sample did not. | sq-04 `headerSample` | Since pass 2 is a full scan, only header detection samples. Confirm the header test tolerates leading null-heavy rows, with a fixture. |

### 3.3 Considered and not proposed for v1

These came up and are recorded so they are not rediscovered. They do not fit the current goals or are consumer concerns.

- **Sniff mode.** Routing needs only the first lines; Format P's header sits beyond a 64 KiB window. Conflicts with the full-scan goal. A consumer with a bounded probe (DataBall's `FileProbe`) should know the header can start well past 64 KiB.
- **Storage hints:** fixed decimal scale (exact as scaled integers), float32-exact flag, uniform step of monotonic columns, exact duplicate columns.
- **String kinds:** tuple, vector, hex, bits, path, with element counts and types.
- **Parallel profiling** with canonical shard boundaries and mergeable sketches.
- **Cost profile:** cell count, padding share, bytes per cell.

### 3.4 Outcome (owner, 2026-10-13)

| Finding | Adopted into |
|---|---|
| 1 Header scoring and `annotation` region | sq-04 |
| 2 Fixed, versioned null vocabulary | sq-16 (`ValueClassifier`, carved out of sq-04) |
| 3 Delimiter clean share | sq-04 |
| 4 Row classes | sq-17 (new, after sq-05) |
| 5 Dates without a locale; stats omitted when ambiguous | sq-16, sq-05 |
| 6 Allow-list opt-in; constant-field stats withheld | sq-07, sq-05 |
| 7 `structure` fingerprint; name exclusions; not a family test | sq-06 |
| 8 Stray-byte charset tolerance | sq-03 |
| 9 Padding facts | sq-04 |
| 10 Decimal separator | sq-16, sq-04 |
| 11 Line length: reported as `layout.maxLineLength`; no cap, so no record goes unprofiled | sq-04 |
| 12 Hints (`nonNullFields`, header prefix, file-name shape); full header regex at any length | sq-06 |
| 13 Header-name grammar hint | sq-21 (later) |
| 14 Compact evidence | sq-20 (before the schema freeze) |
| 15 Leading null-heavy rows | sq-04 |

Also added: a private-corpus harness for real files kept outside the repo (sq-18) and a GB-scale throughput benchmark (sq-19). Section 3.3 stays out of v1.

## 4. Fixtures to add

Synthetic, value-free equivalents of the traits above. No real file is committed.

- A padded fixed-width table with a key preamble, three or four annotation rows, a header and a period-suffixed null token (findings 1–3, 9).
- The same with one annotation row removed, which must still pick the right header.
- A key with a control byte and one invalid UTF-8 byte (findings 7, 8).
- An interleaved-record-kinds file with all-null separator rows, setup rows and one rare error row (finding 4, 15).
- Constant columns holding an integer ID and a locale date with a 12-hour clock (findings 5, 6).
- Spreadsheet-style generic fixtures: UTF-8 with BOM and quoted newlines, windows-1252, UTF-16 tab text, semicolon with decimal comma, a two-section dump (section 3.1, finding 10).

## 5. Open questions and unverified items

- Does the corrupted preamble key vary from file to file? Comparing several files from one program and one station would settle it.
- Is the Format P header prefix fixed for the whole family? Seen in two of two files.
- What do stations in other locales actually write for dates? The non-ASCII AM/PM case was simulated.
- Do the shared Format L column names hold across products? All samples came from lab runs.
- How does a long-form trace file link to its summary file: through constant condition columns only, or also through the file name? No matched pair was available.
- Caps and sketches were not exercised: no sample reached the distinct or quantile caps, so behavior at 10k–100k rows is untested on real data.
