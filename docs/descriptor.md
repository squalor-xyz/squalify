# Descriptor schema v1 (draft)

Status: draft. The schema is frozen by sq-12, which also publishes `schema/descriptor-v1.schema.json`. Until then, fields may change without a version bump.

A descriptor is one UTF-8 JSON object. Keys appear in the order shown here, and from sq-02 on that order is normative: `DescriptorWriter` (`src/squalify/Json/DescriptorWriter.cs`) writes every object with explicit keys in this order, and golden files pin it. The determinism rules are in [design.md](design.md#determinism-rules). Members marked **values tier** appear only with `--include-values` (see [Values tier](#values-tier)).

The JSON examples below are compacted onto fewer lines to be readable. The real output has one array element or object member per line (see [Canonical form](#canonical-form)).

## Canonical form

From sq-02 the writer produces these bytes, and they are identical on every OS, CPU and culture.

- **Layout.** UTF-8 without a BOM, LF line endings, two-space indentation, and exactly one LF after the closing brace. Every object member and every array element is on its own line; `[]` and `{}` are written for empty collections.
- **Text.** Printable characters in the Basic Multilingual Plane are written literally (`µA`, `Ω`, `é`; a U+FFFD that is already in the text is written literally too). The writer uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, so these are escaped: `\"`, `\\`, the short forms `\b \f \n \r \t`, and `\uXXXX` with upper-case hex for other control characters (including U+007F), for BMP code points that the runtime's Unicode tables leave unassigned (for example U+0378, written `\u0378`), and for a few invisible or non-character code points (for example U+0085, U+00A0, U+2028, U+FEFF). Characters outside the BMP are written as surrogate pairs: U+1F600 becomes the 12 characters `\uD83D\uDE00`. A lone UTF-16 surrogate cannot occur in text decoded from bytes; if one reaches the writer it is written as the escape `\uFFFD`. The set of unassigned code points comes from the .NET runtime, so a runtime major upgrade can change this output; that is an output-affecting change (see [design.md](design.md#determinism-rules)). A test pins the behavior.
- **Numbers.** A count or offset is an integer. A double is written in the shortest form that parses back to the same double (.NET `"R"`, invariant culture), for example `0.1`, `1700`, `1E-07` or `1.5E+300`. `-0` is written as `0`. NaN, `Infinity` and `-Infinity` have no JSON number form and are written as the strings `"NaN"`, `"Infinity"` and `"-Infinity"`. Statistics of `int` fields (`min`, `max`, quantiles) are integers; the others are doubles.
- **Key order.** The order of the tables and examples in this document. Where a member is not in an example, its place is listed under [Key order of nested objects](#key-order-of-nested-objects).
- **Collections.** Producers must build every collection sorted by the documented key, and the writer never sorts. Maps (`typeVotes`, `nullTokens`, `lineKinds`, `headerConvention.counts`, `patterns`) are ordered by key, ordinally (UTF-16 code unit order); `valuesPerLine` is ordered by its integer key, ascending. Arrays keep the order documented where they are described (`issues` by code, `shapes` by count then mask, `includeValuesFor` ordinally, and so on). `stdf.records` is ordered by `typ`, then `sub`, ascending. The Parquet dialect's `kvKeys`, `rowGroups.compression` and `rowGroups.encodings` are ordered ordinally. An array whose order is not yet documented (for example `stdf.tests`, `hints.stableLines`, `temporal.orders` and evidence `candidates`) gets one from the slice that first produces it, in that slice's change to this document.
- **Aligned values lists.** A values-tier list that lines up with another list (`regions[].values` with `keys`, `sections[].values` with `keys`, `rows[].values` with the row's cells) has one entry per aligned item. An entry that is not opted in (for example by `--include-values-for`) is `null`.
- **Optional versus null.** A member documented as "when tabular", "keyValue", "when binary", "stdf" and the like is omitted when it does not apply. Members that appear in the examples with the value `null` (for example `fields[].numeric`, `fields[].top`, `hints.magic`, `format.dialect`) are always written, as `null` when they do not apply. Within a values-tier member (`top`, `text`, `unit`, `regions[].values`) the rule of that member applies: a values-tier member that the schema always writes is `null` without `--include-values`. Members written only in some cases are listed below (`nameHex`, `index`, `label`, `lineKinds`, `keys`, `values`, `rows`, `headerConvention`, `maxLineLength`, `rowClasses`, `distinct.approximate`, `distinct.errorBound`, `distinct.exceeds`, `top.maxError`, `firstLines`, `offsets`, `sections[].values`, `annotation.rows[].values`).

### Key order of nested objects

| Object | Keys, in order |
|---|---|
| `layout` | `regions`, `headerConvention` (tabular), `maxLineLength` (text) |
| `layout.regions[]` | `kind`, `index` (block), `label` (block, optional), `lines`, `bytes`, `lineKinds`, `keys`, `values` (values tier), `rows` (annotation) |
| `layout.regions[].rows[]` | `line`, `label` (null when the first field is numeric), `blankFields`, `nullTokens`, `typeVotes`, `type`, `values` (values tier) |
| `table` | `rows`, `fieldCount`, `rowClasses` (sq-17), `fields` |
| `table.rowClasses` | `count`, `transitions`, `allNullRows`, `truncated`, `classes` |
| `table.fields[]` | `index`, `name`, `nameHex` (only when the name contains U+FFFD), `nameParts`, `type`, `dominant`, `typeVotes`, `patterns`, `count`, `nulls`, `nullsExcludingAllNullRows`, `nullTokens`, `flags`, `distinct`, `numeric`, `temporal`, `statsWithheld`, `length`, `shapes`, `monotonic`, `constant`, `unique`, `sourceType`, `unit`, `top`, `text` |
| `fields[].nameParts` | `base`, `unit` (null when none), `convention` |
| `fields[].distinct` | `value`, then `approximate`, `errorBound`, `exceeds` (each only when applicable) |
| `fields[].numeric` | `min`, `max`, `mean`, `stddev`, `quantiles` (`p01`, `p05`, `p25`, `p50`, `p75`, `p95`, `p99`), `approximate` |
| `fields[].temporal` | `orders`, `ambiguous`, `clock`, `min`, `max`, `quantiles` (same keys as numeric); the last three are `null` when ambiguous |
| `fields[].top` | `items` (`[{ value, count }]`), `approximate`, `maxError` (only when approximate) |
| `sections[]` | `name`, `line`, `keys`, `values` (values tier) |
| `binary` | `bytes` (`histogram`, `nul`, `printableAscii`, `high`, `entropy`), `blocks` (`count`, `min`, `max`, `mean`), `segments` |
| `stdf.records[]` | `name`, `typ`, `sub`, `count` |
| `stdf.tests[]` | `testNum`, `kind`, `name`, `occurrences` |
| `hints` | as in the example; every member is always written, `null` when it does not apply (`stableLines` is `[]`) |
| `hints.stdf` | `cpuType`, `version` |
| `issues[]` | `code`, `count`, then `firstLines` or `offsets` (one of them, when the issue has locations) |
| `evidence[]` | `stage`, `topic`, `chosen`, `candidates`; each candidate is `value`, `score`, `reason` |

`encoding.charset` is `null` for files that are not text, and `encoding.bom` is the BOM name or `null`.

## Top level

| Key | Type | Always | Description |
|---|---|---|---|
| `schemaVersion` | string | yes | `"1.0"` |
| `tool` | object | yes | `{ "name": "squalify", "version": "x.y.z" }` |
| `options` | object | yes | Options that affect output: `{ "includeValues": false, "includeValuesFor": [] }`. `includeValuesFor` lists allow-listed names, sorted ordinally; names that match nothing are reported as the issue `includeValuesForUnmatched`. |
| `limits` | object | yes | Caps and table versions in effect. See below. |
| `file` | object | yes | `name` (not a path), `size` (bytes), `sha256` |
| `encoding` | object | yes | `kind` (`empty`/`text`/`binary`), `charset`, `bom`, `lineEndings {crlf, lf, cr}`, `invalidBytes`, `controlBytes` |
| `format` | object | yes | `family` (`delimited`, `parquet`, `touchstone`, `keyValue`, `stdf`, `text`, `binary`, `empty`), `dialect` (family-specific), `score` |
| `kind` | object | yes | `label`, `ruleId`, `confidence` (sq-15) |
| `layout` | object | yes | `regions[]`, `headerConvention` (tabular), `maxLineLength` (text) |
| `table` | object | when tabular | `rows`, `fieldCount {min, max}`, `rowClasses`, `fields[]` |
| `sections` | array | keyValue | `[{ name, line, keys[] }]` |
| `binary` | object | when binary | `bytes {histogram[256], nul, printableAscii, high, entropy}`, `blocks {count, min, max, mean}`, `segments[16]` |
| `stdf` | object | stdf | See below. |
| `fingerprint` | object | yes | `version`, `structure`, `layout`, `typed`, `names` |
| `hints` | object | yes | Detect-rule hints |
| `issues` | array | yes | `[{ code, count, firstLines[] or offsets[] }]`, sorted by code |
| `evidence` | array | yes | The inference trail |

## `limits`

```json
{ "distinctPerField": 10000, "quantilePerField": 100000, "shapesPerField": 64, "regions": 1000,
  "headerSample": 1000, "rowClasses": 64, "charsetTolerance": { "invalidSequences": 8, "perBytes": 65536 },
  "stdfTests": 10000,
  "budget": { "quantileValues": 10000000, "distinctValues": 2000000 }, "nullTokens": 1, "unitTable": 1 }
```

`distinctPerField` and `quantilePerField` are the effective caps after the global budget: min(cap, budget ÷ fields). `nullTokens` and `unitTable` are versions of fixed built-in tables.

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

  `label` is always included. The row cells (`values`) are **values tier**.

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

- `numeric` is for `int` and `float`. `temporal` (`orders`, `ambiguous`, `clock`, and `min`, `max`, `quantiles` as ISO 8601) is for `date`, `datetime` and `time`. When `ambiguous` is true, `min`, `max`, `quantiles` and the field's `monotonic` are null. Both are always included, except for constant fields.
- `length` is null for a field that has no non-null values. `temporal.clock` is `12h` or `24h` for fields that carry a time of day and null for `date` fields.
- `statsWithheld` is `"constant"` when every non-null value of the field is equal and values are not opted in for it; `numeric` and `temporal` are then null.
- `nameHex` (present only when the name contains U+FFFD) is the hex of the name's original bytes.
- `patterns` gives the counts of matched date and time patterns.
- `distinct` past the cap, before sq-11: `{ "value": null, "exceeds": 10000 }`. From sq-11: `{ "value": n, "approximate": true, "errorBound": 0.0081 }`.
- `sourceType` is the native type for Parquet (for example `DECIMAL(10,2)`).
- `unit` is the unit from a units row. **Values tier.** Units parsed from header names are in `nameParts` and are always included.
- `top` (`{ "items": [{ value, count }], "approximate": false }`, plus `maxError` when approximate) and `text` (`{ min, max }`, lexical) are **values tier**. The `{ items, approximate, maxError? }` shape of `top` is the pinned draft shape; sq-12 records it in the schema changelog.
- `monotonic` is one of `increasing`, `decreasing`, `nonDecreasing`, `nonIncreasing` or `none`; it is null for an ambiguous temporal field.
- Shapes are sorted by count descending, then by mask (ordinal). Past the cap, the rest go into `{ "mask": "other", "count": n }`.

## `format.dialect` by family

| Family | Dialect keys |
|---|---|
| delimited | `delimiter`, `quote`, `hasHeader`, `unitsRow`, `decimalSeparator`, `padding {trim, paddedShare, constantLineLength, lineLength}`, `blocks` |
| touchstone | `version`, `ports`, `frequencyUnit`, `parameter`, `dataFormat`, `referenceOhms`, `twoPortOrder`, `matrixFormat`, `keywords[]`, `valuesPerLine {n: count}`, `noise` |
| keyValue | `separator` (`=` or `:`), `hasSections` |
| parquet | `formatVersion`, `createdBy`, `numRows`, `rowGroups {count, rows {min, max}, compression[], encodings[]}`, `kvKeys[]` (KV values are values tier) |
| stdf | `cpuType`, `endianness`, `version` |

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

## `evidence[]`

```json
{ "stage": "dialect", "topic": "delimiter", "chosen": ",", "candidates": [
  { "value": ",", "score": 1, "reason": "k=3; 10000/10000 records; run lines 8-10007" },
  { "value": ";", "score": 0, "reason": "k=1; no run" } ] }
```

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
- sq-12 makes this policy normative.
