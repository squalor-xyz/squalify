# Descriptor schema v1 (draft)

Status: draft. The schema is frozen by sq-12, which also publishes `schema/descriptor-v1.schema.json`. Until then, fields may change without a version bump (sq-05 made `table.fields[]` normative without one).

A descriptor is one UTF-8 JSON object. Keys appear in the order shown here, and from sq-02 on that order is normative: `DescriptorWriter` (`src/squalify/Json/DescriptorWriter.cs`) writes every object with explicit keys in this order, and golden files pin it. The determinism rules are in [design.md](design.md#determinism-rules). Members marked **values tier** appear only with `--include-values` (see [Values tier](#values-tier)).

The JSON examples below are compacted onto fewer lines to be readable. The real output has one array element or object member per line (see [Canonical form](#canonical-form)).

## Canonical form

From sq-02 the writer produces these bytes, and they are identical on every OS, CPU and culture.

- **Layout.** UTF-8 without a BOM, LF line endings, two-space indentation, and exactly one LF after the closing brace. Every object member and every array element is on its own line; `[]` and `{}` are written for empty collections.
- **Text.** Printable characters in the Basic Multilingual Plane are written literally (`µA`, `Ω`, `é`; a U+FFFD that is already in the text is written literally too). The writer uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, so these are escaped: `\"`, `\\`, the short forms `\b \f \n \r \t`, and `\uXXXX` with upper-case hex for other control characters (including U+007F), for BMP code points that the runtime's Unicode tables leave unassigned (for example U+0378, written `\u0378`), and for a few invisible or non-character code points (for example U+0085, U+00A0, U+2028, U+FEFF). Characters outside the BMP are written as surrogate pairs: U+1F600 becomes the 12 characters `\uD83D\uDE00`. A lone UTF-16 surrogate cannot occur in text decoded from bytes; if one reaches the writer it is written as the escape `\uFFFD`. The set of unassigned code points comes from the .NET runtime, so a runtime major upgrade can change this output; that is an output-affecting change (see [design.md](design.md#determinism-rules)). A test pins the behavior.
- **Numbers.** A count or offset is an integer. A double is written in the shortest form that parses back to the same double (.NET `"R"`, invariant culture), for example `0.1`, `1700`, `1E-07` or `1.5E+300`. `-0` is written as `0`. NaN, `Infinity` and `-Infinity` have no JSON number form and are written as the strings `"NaN"`, `"Infinity"` and `"-Infinity"`. Statistics of `int` fields (`min`, `max`, quantiles) are integers; the others are doubles.
- **Key order.** The order of the tables and examples in this document. Where a member is not in an example, its place is listed under [Key order of nested objects](#key-order-of-nested-objects).
- **Collections.** Producers must build every collection sorted by the documented key, and the writer never sorts. Maps (`typeVotes`, `nullTokens`, `lineKinds`, `headerConvention.counts`, `patterns`) are ordered by key, ordinally (UTF-16 code unit order); `valuesPerLine` is ordered by its integer key, ascending. Arrays keep the order documented where they are described (`issues` by code, `shapes` by count then mask, `includeValuesFor` ordinally, and so on). `stdf.records` is ordered by `typ`, then `sub`, ascending. The Parquet dialect's `kvKeys`, `rowGroups.compression` and `rowGroups.encodings` are ordered ordinally. Evidence `candidates` are never in insertion order; see [`evidence[]`](#evidence). An array whose order is not yet documented (for example `stdf.tests` and `hints.stableLines`) gets one from the slice that first produces it, in that slice's change to this document.
- **Aligned values lists.** A values-tier list that lines up with another list (`regions[].values` with `keys`, `sections[].values` with `keys`, `rows[].values` with the row's cells) has one entry per aligned item. An entry that is not opted in (for example by `--include-values-for`) is `null`.
- **Optional versus null.** A member documented as "when tabular", "keyValue", "when binary", "stdf" and the like is omitted when it does not apply. Members that appear in the examples with the value `null` (for example `fields[].numeric`, `fields[].top`, `hints.magic`, `format.dialect`) are always written, as `null` when they do not apply. Within a values-tier member (`top`, `text`, `unit`, `regions[].values`) the rule of that member applies: a values-tier member that the schema always writes is `null` without `--include-values`. Members written only in some cases are listed below (`nameHex`, `index`, `label`, `lineKinds`, `keys`, `values`, `rows`, `headerConvention`, `maxLineLength`, `rowClasses`, `distinct.approximate`, `distinct.errorBound`, `distinct.exceeds`, `numeric.exceeds`, `temporal.exceeds`, `top.maxError`, `firstLines`, `offsets`, `sections[].values`, `annotation.rows[].values`). `binary.blocks` and `binary.segments` are also written only in some cases: they are omitted until sq-10 adds them.

### Key order of nested objects

| Object | Keys, in order |
|---|---|
| `layout` | `regions`, `headerConvention` (tabular), `maxLineLength` (text and delimited; in characters) |
| `layout.regions[]` | `kind`, `index` (block), `label` (block, optional), `lines`, `bytes`, `lineKinds`, `keys`, `values` (values tier), `rows` (annotation) |
| `layout.regions[].rows[]` | `line`, `label` (null when the first field is numeric), `blankFields`, `nullTokens`, `typeVotes`, `type`, `values` (values tier) |
| `table` | `rows`, `fieldCount`, `rowClasses` (sq-17), `fields` |
| `table.rowClasses` | `count`, `transitions`, `allNullRows`, `truncated`, `classes` |
| `table.fields[]` | `index`, `name`, `nameHex` (only when the name contains U+FFFD), `nameParts`, `type`, `dominant`, `typeVotes`, `patterns`, `count`, `nulls`, `nullsExcludingAllNullRows`, `nullTokens`, `flags`, `distinct`, `numeric`, `temporal`, `statsWithheld`, `length`, `shapes`, `monotonic`, `constant`, `unique`, `sourceType`, `unit`, `top`, `text` |
| `fields[].nameParts` | `base`, `unit` (null when none), `convention` |
| `fields[].distinct` | `value`, then `approximate`, `errorBound`, `exceeds` (each only when applicable) |
| `fields[].numeric` | `min`, `max`, `mean`, `stddev` (`null` for fewer than two values), `quantiles` (`p01`, `p05`, `p25`, `p50`, `p75`, `p95`, `p99`; `null` past the cap), `approximate` (`null` past the cap), `exceeds` (only past the cap) |
| `fields[].temporal` | `orders`, `ambiguous`, `clock`, `min`, `max`, `quantiles` (same keys as numeric); the last three are `null` when ambiguous, and `quantiles` is also `null` past the cap; then `exceeds` (only past the cap) |
| `fields[].top` | `items` (`[{ value, count }]`), `approximate`, `maxError` (only when approximate) |
| `sections[]` | `name`, `line`, `keys`, `values` (values tier) |
| `binary` | `bytes` (`histogram`, `nul`, `printableAscii`, `high`, `entropy`), `blocks` (`count`, `min`, `max`, `mean`; sq-10), `segments` (sq-10) |
| `stdf.records[]` | `name`, `typ`, `sub`, `count` |
| `stdf.tests[]` | `testNum`, `kind`, `name`, `occurrences` |
| `hints` | as in the example; every member is always written, `null` when it does not apply (`stableLines` is `[]`) |
| `hints.stdf` | `cpuType`, `version` |
| `issues[]` | `code`, `count`, then `firstLines` or `offsets` (one of them, when the issue has locations) |
| `evidence[]` | `stage`, `topic`, `chosen`, `candidates`; each candidate is `value`, `score`, `reason` |

`encoding.charset` is `null` for files that are not text, and `encoding.bom` is the BOM name or `null`. See [`encoding`](#encoding).

## Top level

| Key | Type | Always | Description |
|---|---|---|---|
| `schemaVersion` | string | yes | `"1.0"` |
| `tool` | object | yes | `{ "name": "squalify", "version": "x.y.z" }` |
| `options` | object | yes | Options that affect output: `{ "includeValues": false, "includeValuesFor": [] }`. `includeValuesFor` lists allow-listed names, sorted ordinally; names that match nothing are reported as the issue `includeValuesForUnmatched`. |
| `limits` | object | yes | Caps and table versions in effect. See below. |
| `file` | object | yes | `name` (not a path), `size` (bytes), `sha256` |
| `encoding` | object | yes | `kind` (`empty`/`text`/`binary`), `charset`, `bom`, `lineEndings {crlf, lf, cr}`, `invalidBytes`, `controlBytes` |
| `format` | object | yes | `family` (`delimited`, `parquet`, `touchstone`, `keyValue`, `stdf`, `text`, `binary`, `empty`), `dialect` (family-specific), `score` (the winning profiler's score; 1 for `empty`) |
| `kind` | object | yes | `label`, `ruleId`, `confidence` (sq-15) |
| `layout` | object | yes | `regions[]`, `headerConvention` (tabular), `maxLineLength` (text and delimited; in characters) |
| `table` | object | when tabular | `rows`, `fieldCount {min, max}`, `rowClasses`, `fields[]` |
| `sections` | array | keyValue | `[{ name, line, keys[] }]` |
| `binary` | object | when binary | `bytes {histogram[256], nul, printableAscii, high, entropy}`, `blocks {count, min, max, mean}` and `segments[16]` (both from sq-10; omitted until then) |
| `stdf` | object | stdf | See below. |
| `fingerprint` | object | yes | `version`, `structure`, `layout`, `typed`, `names` |
| `hints` | object | yes | Detect-rule hints |
| `issues` | array | yes | `[{ code, count, firstLines[] or offsets[] }]`, sorted by code |
| `evidence` | array | yes | The inference trail |

## `limits`

```json
{ "distinctPerField": 10000, "quantilePerField": 100000, "shapesPerField": 64, "regions": 1000,
  "headerSample": 1000, "runawayQuote": 65536, "fieldCountRuns": 100000, "rowClasses": 64,
  "charsetTolerance": { "invalidSequences": 8, "perBytes": 65536 },
  "stdfTests": 10000,
  "budget": { "quantileValues": 10000000, "distinctValues": 2000000 }, "nullTokens": 1, "unitTable": 1 }
```

`distinctPerField` and `quantilePerField` are the effective caps after the global budget: min(cap, budget ÷ fields), with integer division, where fields is the table's field count (the caps of this tool version are 10,000 and 100,000, so a table of 100 fields or fewer keeps both, and 2,000 fields give 1,000 and 5,000). `shapesPerField` (64) and `budget` are written as set, and a file that is not a table writes the caps of the tool version. `headerSample` is the number of records of a delimited data run that are scored as header candidates. `runawayQuote` is the longest open quoted field, in characters, before a delimiter candidate is disqualified, and `fieldCountRuns` is the most field-count runs a candidate may have before it is disqualified as too irregular (see [`format.dialect`](#formatdialect-by-family)). `nullTokens` and `unitTable` are versions of fixed built-in tables. `nullTokens` is the version of the null vocabulary (always `ValueClassifier.NullVocabularyVersion`; no option sets it): version 1 is `""`, `NA`, `NA.`, `N/A`, `null`, `NaN` and `-`, matched ASCII case-insensitively after trimming U+0009 to U+000D, U+0020 and U+00A0 from both ends (a fixed set; other space characters, such as U+2028 and U+3000, are not trimmed). The vocabulary is never learned from the data; any change to it bumps this version. See [Value classification and types](design.md#value-classification-and-types).

`charsetTolerance` sets how many invalid UTF-8 sequences a file may hold and still keep its `ascii` or `utf-8` charset (see [`encoding`](#encoding)). The number allowed for a file is `min(invalidSequences, ceil(size / perBytes))`: at most 8 per file and at most 1 per 64 KiB, and a file shorter than 64 KiB tolerates one. For example, a 100-byte file tolerates 1, a 65,537-byte file 2, and any file of 8 × 64 KiB or more 8.

## `file`

```json
{ "name": "plain.txt", "size": 177, "sha256": "035c84ff…" }
```

`name` is the file name only: the profiler rejects a source name that is empty or contains `/` or `\` with an error. A path never reaches the descriptor, so the same bytes under the same name in two directories give the same descriptor. `size` is the number of bytes read, and `sha256` is the lower-case hex SHA-256 of the content.

## `encoding`

```json
{ "kind": "text", "charset": "ascii", "bom": null, "lineEndings": { "crlf": 0, "lf": 5, "cr": 0 },
  "invalidBytes": 0, "controlBytes": 0 }
```

The byte pass (pass 0) decides `kind` from whole-file counts, with these rules in order:

1. A zero-length file is `empty`.
2. A named binary magic is `binary`. The magic table, in matching order (the first match wins), is: `parquet` (`PAR1` at the start and at the end), `stdf-le` (`02 00 00 0A`), `stdf-be` (`00 02 00 0A`), `gzip` (`1F 8B`), `zip` (`PK\x03\x04` or `PK\x05\x06`), `xz`, `bzip2` (`BZh`), `zstd`, `7z`, `tar` (`ustar` at offset 257), `ole2`, `pdf` (`%PDF-`), `png`, `sqlite`, `hdf5`, `duckdb` (`DUCK` at offset 8, the check DataBall uses to recognize a `.ball` file), `arrow` (`ARROW1`) and `netcdf` (`CDF\x01` or `CDF\x02`). Compressed and archive files are described as binary with their magic name; nothing is decompressed.
3. A UTF-16 or UTF-32 BOM is `text` with that charset.
4. A NUL-parity pattern is UTF-16 text without a BOM: NUL bytes are at least 30% of the file, and at least 90% of them are at odd offsets (`utf-16le`) or at least 90% at even offsets (`utf-16be`).
5. Any other NUL byte, or control bytes above 5% of the file, is `binary`.
6. Otherwise the file is `text`. The charset is `ascii` when every byte is below 0x80, `utf-8` when the content is valid UTF-8 (a UTF-8 BOM counts as valid content and is reported in `bom`), and `latin-1` otherwise. When `latin-1` is chosen and bytes 0x80 to 0x9F occur, the evidence reason adds "possibly windows-1252".
7. Stray invalid UTF-8 sequences within `limits.charsetTolerance` do not change the charset. It stays `ascii` (when every byte at or above 0x80 belongs to an invalid sequence) or `utf-8`, and the decoder maps each invalid sequence to U+FFFD. Beyond the tolerance, rule 6's `latin-1` applies.

Fields:

- `charset` is `ascii`, `utf-8`, `latin-1`, `utf-16le`, `utf-16be`, `utf-32le` or `utf-32be` for text, and `null` otherwise.
- `bom` is `utf-8`, `utf-16le`, `utf-16be`, `utf-32le`, `utf-32be` or `null`.
- `lineEndings` counts CRLF pairs, lone LF and lone CR in the decoded text of a text file (all zero for other files). A line break is one of those three.
- `invalidBytes` is the number of bytes in invalid UTF-8 sequences when the charset is `ascii`, `utf-8` or `latin-1`, and 0 otherwise. It, like the `invalidUtf8` issue, covers UTF-8 and ASCII input only. In this version, invalid UTF-16 or UTF-32 data (a lone surrogate, an odd trailing byte, an out-of-range UTF-32 value) is decoded to U+FFFD with no count and no issue: "hello world\n" in UTF-16LE plus one stray byte gives `invalidBytes` 0 and `issues` `[]`. A later slice will add the reporting.
- `controlBytes` is the count of bytes 0x00-0x08, 0x0E-0x1F and 0x7F in the raw file, whatever the kind. A control byte in text is counted here and does not on its own make the file binary.
  **Warning: this is a raw byte count, not a count of control characters.** For UTF-16 and UTF-32 text it includes the zero high-order bytes of every ASCII character (a BOM plus `hello\n` in UTF-16LE gives 6), so rules must not read it as a count of control characters for those charsets.

## `layout` of a text file

A text file that no other profiler claims has one `unknown` region over the whole file, and `layout.maxLineLength` is the longest line in characters (UTF-16 code units of the decoded text, line break excluded):

```json
{ "regions": [ { "kind": "unknown", "lines": [1, 5], "bytes": [0, 177], "lineKinds": { "blank": 1, "text": 4 } } ],
  "maxLineLength": 58 }
```

- `lines` runs from 1 to the line count. A last line without a terminator counts, and a trailing line break does not add a line. `bytes` is `[0, size]`.
- `lineKinds` counts `blank` lines (empty, or only spaces and tabs) and `text` lines; a kind with no lines is omitted.
- A file with no characters (for example only a BOM) has `regions: []` and `maxLineLength: 0`.
- Empty and binary files have `regions: []`, and a binary file has no `maxLineLength`.

## `layout` of a delimited file

A file that the delimited profiler claims has its regions in file order, and `layout.maxLineLength` is the longest line in characters (UTF-16 code units of the decoded text, line break excluded: the same measure as for a text file). Regions tile the file: each starts at the first record of its kind and ends on the line before the next one starts, a blank line belongs to the region before it, the first region starts at byte 0 (a BOM included) and the last ends at the file size. Byte ranges are offsets in the file's own charset. For `lab/sweep-preamble.csv` (`tests/squalify.Tests/fixtures/lab/`):

```json
{ "regions": [
    { "kind": "preamble", "lines": [1, 7], "bytes": [0, 98], "lineKinds": { "comment": 1, "keyValue": 4, "text": 2 },
      "keys": ["Lot", "Wafer", "Temp", "Operator"] },
    { "kind": "header", "lines": [8, 8], "bytes": [98, 127] },
    { "kind": "units", "lines": [9, 9], "bytes": [127, 138] },
    { "kind": "data", "lines": [10, 29], "bytes": [138, 435] },
    { "kind": "footer", "lines": [30, 31], "bytes": [435, 448], "lineKinds": { "keyValue": 1, "text": 1 } } ],
  "maxLineLength": 28 }
```

- `preamble`: the lines before the data run (written only when the run does not start on line 1). `lineKinds` counts the records and blank lines by kind (`blank`, `comment`, `keyValue`, `keyDelimValue`, `text`; a kind with none is omitted); `keys` lists the normalized keys of the `keyValue` and `keyDelimValue` records, in file order (an empty list when there are none); `values`, aligned with `keys`, is **values tier**.
- `annotation`: the records between the start of the run and the header, except comment lines (a comment line inside the run is counted as `commentInData`, never listed with a label or cells) (see [`layout.regions[]`](#layoutregions)).
- `header`: the header record. `units`: the units row. `data`: from the first record after those. `footer`: the non-blank records after the run, with `lineKinds`.
- `hasHeader: false` leaves out `annotation`, `header` and `units`. A header with no data rows leaves out `data`.

## `binary.bytes`

```json
{ "histogram": [8, 0, 1, …], "nul": 8, "printableAscii": 18, "high": 16, "entropy": 4.793773756368349 }
```

`histogram` has 256 counts, indexed by byte value. `nul` is the count of 0x00 bytes, `printableAscii` of 0x20-0x7E, and `high` of bytes at or above 0x80. `entropy` is the Shannon entropy in bits per byte, `-sum(p * log2 p)` over the bins 0 to 255 in order, with the deterministic `DetMath.Log2`. `blocks` and `segments` are added by sq-10 and are omitted until then.

## Interim members

Until the slices that own them land, these members hold fixed interim values (or `null`), so that every file yields a valid descriptor:

- `kind` follows the byte-pass class: `empty` (`K12`), `binary` (`K11`) or `text` (`K09`), with confidence 1. sq-15 replaces it with the rule table.
- `fingerprint` has `version` 0. `structure`, `layout` and `typed` are the SHA-256 of the compact JSON `{"family":"<family>"}`, and `names` is the SHA-256 of `[]`. sq-06 replaces these with the real projections and `version` 1.
- `table.fields[]` of a delimited file holds real values for every member except those of later slices: `nameParts` is `null` until sq-14, `sourceType` (Parquet) is `null` for text, and `top` and `text` (values tier) are `null` until sq-07. A consumer must read `null` in those members as "not computed". See [`table.fields[]`](#tablefields) for the rest; `unit` is `null` without `--include-values` or when the units cell is null.
- `hints` carries `extension` (lower case, with the dot, or `null`), `magic` (the magic name or `null`) and `fileNameShape` (the shape mask of the file name); every other member is `null`, and `stableLines` is `[]`. sq-06 adds the rest, and the structured `magic`.

## `layout.regions[]`

```json
{ "kind": "preamble", "lines": [1, 6], "bytes": [0, 212], "lineKinds": { "keyValue": 5, "comment": 1 },
  "keys": ["Lot", "Wafer", "Temp", "Operator", "Date"] }
```

- Region kinds: `preamble`, `annotation`, `header`, `units`, `data`, `footer`, `block`, `unknown`.
- Lines are 1-based and inclusive. Bytes are 0-based and half-open.
- `block` regions carry `index` and an optional `label` (for example `"noise"` in Touchstone).
- Preamble `keys` are always included, normalized (trimmed, trailing `:` or `=` removed). `values`, aligned with `keys`, are **values tier**.
- An `annotation` region holds the column-aligned rows between the start of the table run and the header:

  ```json
  { "kind": "annotation", "lines": [7, 8], "bytes": [210, 655], "rows": [
    { "line": 7, "label": "Upper", "blankFields": 1, "nullTokens": { "NA": 2 }, "typeVotes": { "float": 9 }, "type": "float" } ] }
  ```

  `label` is the normalized first field when it is non-numeric, else `null`; it is always included. When there is a label, that cell is left out of `blankFields`, `nullTokens`, `typeVotes` and `type`. `blankFields` counts blank cells; `nullTokens` counts the other null-vocabulary cells by the vocabulary's spelling (`NA.`, `NaN`, ...); `typeVotes` counts the non-null cells by type; `type` is their strict join (`empty` when there are none). The row cells (`values`, every cell trimmed, the label included) are **values tier**.

## `layout.headerConvention`

```json
{ "dominant": "nameParenUnit", "share": 0.769, "counts": { "nameParenUnit": 10, "none": 3 } }
```

## `table.rowClasses`

```json
{ "count": 3, "transitions": 120, "allNullRows": 40, "truncated": 0, "classes": [
  { "id": "rc:3f9a…", "count": 900, "fields": [[0, 11]], "firstLine": 3, "lastLine": 1002 } ] }
```

Records are grouped by their set of non-null fields (sq-17). `fields` are inclusive index ranges. The `id` hashes the non-null field indexes, so it compares across files only when the column set matches. Classes are sorted by count descending, then id. Past `limits.rowClasses`, records are counted in `truncated`.

## `table.fields[]`

From sq-05 this section is normative for delimited files. The members are computed by `FieldAccumulator` (see [design.md](design.md#field-profiling)).

```json
{
  "index": 7,
  "name": "Frequency(MHz)",
  "nameParts": { "base": "Frequency", "unit": "MHz", "convention": "nameParenUnit" },
  "type": "float",
  "dominant": { "type": "float", "share": 1 },
  "typeVotes": { "float": 9998, "int": 2 },
  "patterns": null,
  "count": 10000,
  "nulls": 0,
  "nullsExcludingAllNullRows": 0,
  "nullTokens": {},
  "flags": { "leadingZero": 0, "commaDecimalLike": 0, "padded": 0 },
  "distinct": { "value": 81, "approximate": false },
  "numeric": { "min": 700, "max": 2700, "mean": 1700, "stddev": 584.5, "quantiles": { "p01": 700, "p05": 800, "p25": 1200, "p50": 1700, "p75": 2200, "p95": 2600, "p99": 2700 }, "approximate": false },
  "temporal": null,
  "statsWithheld": null,
  "length": { "min": 3, "max": 6, "mean": 5.2 },
  "shapes": [{ "mask": "9{4}", "count": 9120 }, { "mask": "9{3}", "count": 880 }],
  "monotonic": "nonDecreasing",
  "constant": false,
  "unique": false,
  "sourceType": null,
  "unit": null,
  "top": null,
  "text": null
}
```

A temporal field, for example:

```json
{
  "type": "date",
  "patterns": { "n/n/yyyy": 40 },
  "temporal": { "orders": ["DMY", "MDY"], "ambiguous": true, "clock": null, "min": null, "max": null, "quantiles": null },
  "monotonic": null
}
```

- **Counts.** `count` is the non-null plus the null values of the data records that have the header's field count; ragged records are not typed. `nulls` counts the null values, and `nullsExcludingAllNullRows` those outside records in which every field is null. The sum of `typeVotes` is `count - nulls`.
- **`type`, `dominant`, `typeVotes`.** `type` is the strict join of the votes over the lattice `empty` < `bool`; `empty` < `int` < `float`; `empty` < `date` < `datetime`; `empty` < `time`; any other join is `string` (`empty` when there is no non-null value). It is also `string` when the date values share no order (see `temporal`). `dominant` is the plurality vote with its share of the non-null votes (`empty` with share 0 when there are none). A tie goes to the earlier type in the order `bool`, `int`, `float`, `date`, `datetime`, `time`, `string`: the narrower of `int` and `float` or of `date` and `datetime`, and, between types that are not comparable, `bool` before `int`, `int` before `date`, `date` before `time`. `typeVotes` counts the non-null votes by type, so it can disagree with `type`.
- **`nullTokens`** counts the null values by **null-vocabulary entry**, not by the spelling in the file: the keys are `""` (blank and white space), `NA`, `NA.`, `N/A`, `null`, `NaN` and `-`, and `NULL`, `Null` and `null` are all counted under `null`. It is always written, `{}` when there are no nulls, and `NaN` is a key of its own, so a consumer that reads `NaN` as a number can tell it apart. Keys are ordered ordinally and the counts add up to `nulls`.
- **`patterns`** gives the counts of matched date, datetime and time patterns, keyed by pattern; it is null for fields with no such value. A pattern is the order-neutral layout of a value: `yyyy` for a 4-digit year, `nn` or `n` for a 1–2 digit day or month, with the separator kept (`n/n/yyyy` for `6/5/2026`, whichever order it is; the orders are in `temporal.orders`); `HH`/`H` (24-hour) or `hh`/`h` (12-hour) for the hour with `:mm`, `:ss`, `.f+` (a fraction of any length) and `tt` (an `AM`/`PM` marker, preceded by a space when the value has one); `Z`, `zz` (`+hh`), `zzzz` (`+hhmm`) or `zzz` (`+hh:mm`) for a zone offset; and a literal `T` or space between date and time. For example `yyyy-nn-nn`, `nn/n/yyyy hh:mm:ss tt` and `yyyy-nn-nnTHH:mm:ssZ`.
- **`flags`.** `leadingZero` counts numbers whose integer part starts with `0` and another digit (`007`), `commaDecimalLike` the values that match `^[+-]?[0-9]+,[0-9]+$` whatever the decimal separator, and `padded` the non-empty values whose raw text starts or ends with a trim-set character (null tokens included).
- **`distinct`** is `{ "value": n, "approximate": false }`, the exact number of distinct non-null values (compared as trimmed text, so `1` and `1.0` are two; `0` when there are none), up to `limits.distinctPerField`. Past the cap, before sq-11: `{ "value": null, "exceeds": 10000 }` with the effective cap (`approximate` is absent). From sq-11: `{ "value": n, "approximate": true, "errorBound": 0.0081 }`.
- **`numeric`** is for `int` and `float` fields; it is `null` for other types. `min` and `max` are integers for `int` and doubles for `float`. `mean` and `stddev` (the sample standard deviation, `null` for fewer than two values) are computed by Welford in file order. A running mean or sum of squares that overflows (values near the limits of a double, such as `1.7E308` and `-1.7E308`) would be infinite or NaN; the member is then `null`, never a string, and the two are decided separately (`mean` can be written while `stddev` is `null`). The `quantiles` are the lower nearest rank of the sorted values: `p01`, `p05`, `p25`, `p50`, `p75`, `p95` and `p99` are the value at zero-based index `ceil(p * n) - 1`, with no interpolation, and are integers for `int`. Past `limits.quantilePerField` values, before sq-11, `quantiles` and `approximate` are `null` and `exceeds` holds the cap; `min`, `max`, `mean` and `stddev` are still written. Otherwise `approximate` is `false`.
- **`temporal`** is for `date`, `datetime` and `time` fields; it is `null` for other types, for a field whose date values share no order, and for a constant field whose statistics are withheld.
  - `orders` is the list of date orders that every value of the field allows, from `DMY`, `MDY` and `YMD`, **sorted ordinally** (so `["DMY", "MDY"]`, never `["MDY", "DMY"]`); it is `[]` for a `time` field. `ambiguous` is true when more than one order is left.
  - `clock` is `24h` or `12h` over the values that carry a time of day, and `null` for a `date` field or when 24-hour and 12-hour values are mixed.
  - `min`, `max` and `quantiles` (the same keys as `numeric`) are ISO 8601: `yyyy-MM-dd` for `date`, `yyyy-MM-ddTHH:mm:ss` for `datetime` and `HH:mm:ss` for `time`, with a fraction of a second (`.25`) only when it is not zero. A 12-hour value is read on a 24-hour day, and a zone offset is ignored: the statistics are over the wall-clock fields as written. When `ambiguous` is true, `min`, `max` and `quantiles` are written as `null` (not omitted), and so is the field's `monotonic`, because each depends on an assumed order. `quantiles` is also `null` past the cap, with `exceeds`.
  - When the values share no order (`3/17/2026` and `17/3/2026` in one field), `type` is `string`, `temporal` and `monotonic` are `null`, `typeVotes` and `dominant` are unchanged, and the field is listed in the evidence entry `type` / `dateOrderConflict`.
- **`statsWithheld`** is `"constant"` when the field is `constant`, has `numeric` or `temporal` statistics to give, and values are not opted in for it (`--include-values`, or the field's name in `--include-values-for`); `numeric` and `temporal` are then `null`. `length`, `shapes`, `flags` and `distinct` are kept. It is `null` otherwise.
- **`length`** is `{ min, max, mean }` over the trimmed non-null values, in UTF-16 code units; it is `null` for a field with no non-null value.
- **`shapes`** is the list of shape masks of the trimmed non-null values with their counts, sorted by count descending, then by mask (ordinal); it is `[]` when there is no non-null value. A mask maps each UTF-16 code unit to a class: `9` for `0`-`9`, `A` for `A`-`Z`, `a` for `a`-`z`; ASCII punctuation and space are kept literally, except `{` and `}`; every other character, including every non-ASCII letter and each half of a surrogate pair, is `?`. A run of two or more of the same symbol is written `c{n}`, and a mask of more than 64 tokens is cut after 64 and ends with `+`. The braces themselves are never literal: `{` and `}` are `?`, so `1{2}` is `9?9?` and cannot be read as the mask `9{9}` of nine digits. The first 64 distinct masks in file order get a slot (`limits.shapesPerField`); every value with another mask goes into one final entry `{ "mask": "other", "count": n }`, written after the sorted masks. The classes are ASCII ranges, so the masks do not depend on the runtime. `hints.fileNameShape` uses the same rules.
- **`monotonic`** is one of `increasing` (strictly), `decreasing`, `nonDecreasing`, `nonIncreasing` or `none`, over the non-null values in file order. It is for numeric and temporal fields with at least two non-null values and is `null` otherwise, and for an ambiguous or conflicting date field. A run of equal values is `nonDecreasing`.
- **`constant`** is true when every non-null value of the field is equal. For an `int`, `float`, `date`, `datetime` or `time` field the values are compared as parsed: `1`, `1.0` and `01` are equal, `0`, `-0` and `0.0` are equal, a float equals an integer only when it is exactly that integer, and two dates are equal when their parsed parts are (so `6/5/2026` and `06/05/2026` are equal in an ambiguous field too, and a date equals a datetime at midnight). For any other field, including one whose type ended as `string`, they are compared as trimmed text (`TRUE` and `true` differ). `distinct` and `unique` always compare the trimmed text, so a constant field can have a `distinct` value above 1. A sparse field with one value and many nulls is constant, and so is a field whose values are all null (it has one value, null). It is false for a field with no values at all, and false once two values differ.
- **`unique`** is true when every non-null value is distinct, false once a value repeats and for a field with no non-null value, and `null` when more distinct values than `limits.distinctPerField` came before any repeat (the count is then not exact).
- `name` is the trimmed header text; it is `null` when the table has no header (later slices refer to the field as `#<index>`), and `""` for a blank header name (with the issue `emptyHeaderName`).
- `nameHex` (present only when the name contains U+FFFD) is the hex (lower case) of the original bytes of the field as written, trimmed, quotes included.
- `sourceType` is the native type for Parquet (for example `DECIMAL(10,2)`).
- `unit` is the unit from a units row. **Values tier.** Units parsed from header names are in `nameParts` and are always included.
- `top` (`{ "items": [{ value, count }], "approximate": false }`, plus `maxError` when approximate) and `text` (`{ min, max }`, lexical) are **values tier**. The `{ items, approximate, maxError? }` shape of `top` is the pinned draft shape; sq-12 records it in the schema changelog.

Distinct counting, `unique` and the text comparison of `constant` compare a 64-bit xxHash of each trimmed value (see [design.md](design.md#field-profiling)), so two different values are taken for one with probability 2^-64.

## `format.dialect` by family

| Family | Dialect keys |
|---|---|
| delimited | `delimiter`, `quote`, `hasHeader`, `unitsRow`, `decimalSeparator`, `padding {trim, paddedShare, constantLineLength, lineLength}`, `blocks` |
| touchstone | `version`, `ports`, `frequencyUnit`, `parameter`, `dataFormat`, `referenceOhms`, `twoPortOrder`, `matrixFormat`, `keywords[]`, `valuesPerLine {n: count}`, `noise` |
| keyValue | `separator` (`=` or `:`), `hasSections` |
| parquet | `formatVersion`, `createdBy`, `numRows`, `rowGroups {count, rows {min, max}, compression[], encodings[]}`, `kvKeys[]` (KV values are values tier) |
| stdf | `cpuType`, `endianness`, `version` |

### delimited

```json
{ "delimiter": ",", "quote": "\"", "hasHeader": true, "unitsRow": false, "decimalSeparator": ".",
  "padding": { "trim": true, "paddedShare": 0.9726027397260274, "constantLineLength": true, "lineLength": 74 },
  "blocks": 1 }
```

- `delimiter` is `,`, a tab, `;`, `|`, or `whitespace` for runs of white space (the characters of the null-vocabulary trim set, leading and trailing white space trimmed).
- `quote` is always `"` for a delimited file: it is the quote character that the tokenizer honors, whether or not the file has a quoted field (the delimiter evidence counts the quoted fields). Quoting is RFC 4180: a quote opens a field only at the start of a field, `""` inside a quoted field is an escape, and line breaks are allowed inside quotes. A closing quote followed by spaces or tabs and then the delimiter, or the end of the line, closes the field. A lone `"` inside a quoted field that is followed by neither `"`, nor (after optional spaces or tabs) the delimiter or the end of the line is literal text. A quoted field longer than `limits.runawayQuote` characters disqualifies that delimiter.
- `hasHeader` is true when a header was chosen (see the `header` evidence below); `unitsRow` is true when the record after the header is a units row: every non-null field fails its column's type, is at most 16 characters and sits in a column that is not `string`, and at least one field is non-null. Blank cells and null-vocabulary tokens are neutral.
- `decimalSeparator` is `,` only when the delimiter is not `,` and comma-decimal cells (`^[+-]?[0-9]+,[0-9]+$`) outnumber dot-decimal cells (`^[+-]?[0-9]+\.[0-9]+$`) in the header sample; otherwise `.`.
- `padding` describes the data run (from its first to its last record). `paddedShare` is the share of cells that are non-empty after trimming and start or end with a trim-set character; `trim` is true when any cell is padded; `constantLineLength` is true when every non-blank physical line of the run has the same byte length, and `lineLength` is then that length **in bytes** (`null` otherwise). Unlike `layout.maxLineLength`, which counts characters, `padding.lineLength` counts the bytes of the file's own charset, line break excluded.
- `blocks` is 1 until sq-13 (repeated blocks).

## `stdf`

```json
{ "records": [{ "name": "FAR", "typ": 0, "sub": 10, "count": 1 }],
  "firstOccurrenceOrder": ["FAR", "MIR", "PIR", "PTR", "PRR", "TSR", "MRR"],
  "farFirst": true, "mrrLast": true,
  "tests": [{ "testNum": 1000, "kind": "P", "name": "VDD_LEAK", "occurrences": 4210 }],
  "testsTruncated": 0 }
```

## `kind`

```json
{ "label": "labSweepTable", "ruleId": "K05", "confidence": 0.8 }
```

## `fingerprint`

```json
{ "version": 1, "structure": "sha256:…", "layout": "sha256:…", "typed": "sha256:…", "names": "sha256:…" }
```

The projections are defined in [design.md](design.md#structural-fingerprint). Fingerprint equality is not a family test.

## `hints`

```json
{
  "extension": ".csv",
  "magic": null,
  "stableLines": [{ "line": 0, "regex": "^Lot\\s*[=:]" }],
  "headerFields": ["Site", "Vth", "Idsat"],
  "headerPrefix": ["Site", "Vth", "Idsat"],
  "nonNullFields": [0, 1],
  "fieldCount": { "min": 3, "max": 3 },
  "dataStartLine": 8,
  "delimiter": ",",
  "headerConvention": "nameParenUnit",
  "parquetColumns": null,
  "stdf": null,
  "fileNameShape": "a{5}_9{3}.a{3}"
}
```

- `nonNullFields` lists the indexes of fields with no nulls in this file. It is an observation, not a requirement of the type.

- `stableLines[].line` is 0-based, matching DataBall's `lineMatches`.
- Regexes escape only `\.^$|?*+()[]{}`, so they compile under both .NET and ECMAScript.
- Regexes for comment and text lines are **values tier**. The full-header regex and the header-prefix regex are always emitted.

## `issues[]`

Issues are sorted by `code`, ordinally. Each has a `count` and, for some, locations. The issues written by the byte pass and the generic text profile are:

| Code | When | `count` | Locations |
|---|---|---|---|
| `invalidUtf8` | The charset is `ascii` or `utf-8` and the file holds invalid UTF-8 sequences (within the tolerance). Not written for `latin-1`, where nothing is lost; `encoding.invalidBytes` still counts them. | Invalid sequences | `offsets`: byte offsets where the first 10 start, ascending |
| `mixedLineEndings` | More than one of CRLF, lone LF and lone CR occurs. | Line endings that are not the dominant kind (most frequent; a tie goes to CRLF, then LF, then CR) | `firstLines`: the first 10 lines, ascending, that end with a non-dominant kind |

The delimited profiler writes these:

| Code | When | `count` | Locations |
|---|---|---|---|
| `raggedRows` | A record in the data run (after the header and units row) has a field count other than k. It is counted in `table.rows` and `table.fieldCount` but not typed. | Ragged records | `firstLines`: the first 10 lines, ascending |
| `commentInData` | A comment line (`#`, `!` or `//` after trimming) lies inside the data run, before or after the header. It is neither a header candidate, an annotation row nor a data row, and not ragged; its text never reaches the descriptor. | Comment lines | `firstLines`: the first 10 lines, ascending |
| `duplicateHeader` | Two header names are equal under ASCII case folding (only possible through the relaxed header pass). | Names that repeat an earlier name | `firstLines`: the header line |
| `emptyHeaderName` | A header name is blank (relaxed header pass). | Blank names | `firstLines`: the header line |
| `tooIrregular` | The delimited profiler declined the file: no candidate delimiter is viable and at least one was disqualified (a runaway quote, or more than `limits.fieldCountRuns` field-count runs). The generic text profile describes the file. | Disqualified candidates | `firstLines`: the lines where they were disqualified, ascending, at most 10 |

## `evidence[]`

```json
{ "stage": "dialect", "topic": "delimiter", "chosen": ",", "candidates": [
  { "value": ",", "score": 1, "reason": "k=3; 10000/10000 records; run lines 8-10007" },
  { "value": ";", "score": 0, "reason": "k=1; no run" } ] }
```

**Order of entries.** Entries are sorted by stage in pipeline order (`encoding`, `format`, `dialect`, `header`, `type`, then the stages that later slices add), and within a stage by `topic`, ordinally. A stage that is not in the list ranks after the known ones, so every new stage must be added to `Profiler.StageOrder` and listed here.

**Order of `candidates`.** The order is never insertion order. Each topic documents its own key, and a topic with a fixed set of candidates uses a fixed table. A later slice documents its topics here when it adds them.

| Stage / topic | Candidates | Order |
|---|---|---|
| `encoding` / `class` | `binary`, `empty`, `text`; the chosen one scores 1, the others 0 | `value`, ordinally |
| `encoding` / `charset` (text files only) | Rule 3 or 4: only the charset that the rule implies. Rule 6: `ascii`, `latin-1`, `utf-8`; the chosen one scores 1, the others 0. The reason of the chosen candidate carries the invalid-sequence count and the tolerance when they matter | `value`, ordinally |
| `format` / `family` | Every registered profiler: its family as `value`, its score and its reason. `chosen` is the family with the highest score, a tie going to the earlier family; for a zero-length file it is `empty`, which no profiler claims, and every candidate scores 0 | Registry order: `parquet`, `stdf`, `touchstone`, `keyValue`, `delimited`, `text`, `binary` (only registered profilers appear; at present `delimited`, `text` and `binary`) |

| `dialect` / `delimiter` (delimited text files, when the profiler wins, or when it declined a file as too irregular) | The five delimiter candidates as `value`: `,`, a tab, `;`, `|`, `whitespace`. `score` is the candidate's dialect score (0 for a disqualified one); `chosen` is the winner, `null` when none is viable. The reason is `k={k}; {inRun}/{total} records; clean={c}; run lines {a}-{b}`, with `; {n} quoted fields` always appended and `; {n} literal quotes` appended when the candidate saw any; `k={k}; no run` (with the same quoted-fields and literal-quotes suffixes); `disqualified: runaway quote opened at line {n}`; or `disqualified: too irregular, more than {limit} field-count runs by line {n}` | Candidate order: `,`, tab, `;`, `|`, `whitespace` |
| `dialect` / `decimalSeparator` (delimited) | `,` and `.`; the chosen one scores 1. The reasons carry the counts of comma-decimal and dot-decimal cells in the header sample | `value`, ordinally |
| `header` / `candidates` (delimited) | Every eligible record of the header sample, with `value` = its 1-based line and `score` = its header score; the chosen record too when it is not eligible (relaxed or assumed); then one entry with `value` `summary` and score 0. The reason of a record is `eligible; nonEmpty=..; nonNumeric=..; unique=..; contrast=..`, `relaxed: blank or repeated names; ...` or `assumed: all-string table; ...`. The reason of the summary counts only: `sampled={n}; eligible={e}; ineligible={i} (comment line={a}, field count={b}, blank or repeated names={c}, fits every typed column={d})`. `chosen` is the header line, `null` when there is no header | Records by line, ascending (numerically); `summary` last |
| `type` / `dateOrderConflict` (delimited) | One candidate per field that joined to `string` because its date values share no order: `value` is the field index as a decimal string, `score` is 0, and the reason is `string: no date order fits every value; {n} date values; DMY {a}, MDY {b}, YMD {c}`, where {a}, {b} and {c} count the date values that allow each order. `chosen` is `string`. The entry is written only when such a field exists | Fields by index, ascending (numerically) |

Reasons are fixed templates filled with value-free numbers (counts and offsets), never data values.

## Values tier

| Member | Tier |
|---|---|
| Header names, preamble keys, annotation labels, section names and keys, Parquet column names and KV keys, STDF test names | always |
| Numeric and temporal statistics, counts, types, shapes, lengths | always, except statistics of constant fields |
| `nameParts` and `headerConvention` | always |
| Region `values` (preamble and annotation), key=value values, Parquet KV values, comment text | values tier |
| `numeric` and `temporal` of constant fields | values tier |
| `fields[].top`, `fields[].text`, `fields[].unit` (units row) | values tier |
| `stableLines` for comment and text lines | values tier |

`--include-values-for <name>` opts in the values tier for the named preamble keys, annotation labels, key=value keys and fields only.

## Versioning policy

- `schemaVersion` is `major.minor`. Adding optional keys bumps the minor version. Removing, renaming or changing the meaning of a key bumps the major version.
- Consumers must ignore unknown keys within a major version.
- `fingerprint.version` changes whenever a projection changes. Hashes from different versions cannot be compared.
- sq-12 makes this policy normative. Until then the schema is a draft: the members that sq-04 adds (`limits.runawayQuote`, `limits.fieldCountRuns`, the delimited dialect and regions, the delimited issues and the `dialect` and `header` evidence stages) do not bump `schemaVersion`, which stays `1.0`.
