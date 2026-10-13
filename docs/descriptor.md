# Descriptor schema v1 (draft)

Status: draft. The schema is frozen by sq-12, which also publishes `schema/descriptor-v1.schema.json`. Until then, fields may change without a version bump.

A descriptor is one UTF-8 JSON object. Keys appear in the order shown here, and from sq-02 on that order is normative. The determinism rules are in [design.md](design.md#determinism-rules). Members marked **values tier** appear only with `--include-values` (see [Values tier](#values-tier)).

## Top level

| Key | Type | Always | Description |
|---|---|---|---|
| `schemaVersion` | string | yes | `"1.0"` |
| `tool` | object | yes | `{ "name": "squalify", "version": "x.y.z" }` |
| `options` | object | yes | Options that affect output: `{ "includeValues": false }` |
| `limits` | object | yes | Caps and table versions in effect. See below. |
| `file` | object | yes | `name` (not a path), `size` (bytes), `sha256` |
| `encoding` | object | yes | `kind` (`empty`/`text`/`binary`), `charset`, `bom`, `lineEndings {crlf, lf, cr}`, `invalidBytes` |
| `format` | object | yes | `family` (`delimited`, `parquet`, `touchstone`, `keyValue`, `stdf`, `text`, `binary`, `empty`), `dialect` (family-specific), `score` |
| `kind` | object | yes | `label`, `ruleId`, `confidence` (sq-15) |
| `layout` | object | yes | `regions[]`, `headerConvention` (tabular) |
| `table` | object | when tabular | `rows`, `fieldCount {min, max}`, `fields[]` |
| `sections` | array | keyValue | `[{ name, line, keys[] }]` |
| `binary` | object | when binary | `bytes {histogram[256], nul, printableAscii, high, entropy}`, `blocks {count, min, max, mean}`, `segments[16]` |
| `stdf` | object | stdf | See below. |
| `fingerprint` | object | yes | `version`, `layout`, `typed`, `names` |
| `hints` | object | yes | Detect-rule hints |
| `issues` | array | yes | `[{ code, count, firstLines[] or offsets[] }]`, sorted by code |
| `evidence` | array | yes | The inference trail |

## `limits`

```json
{ "distinctPerField": 10000, "quantilePerField": 100000, "shapesPerField": 64, "regions": 1000,
  "headerSample": 1000, "stdfTests": 10000, "budget": { "quantileValues": 10000000, "distinctValues": 2000000 },
  "unitTable": 1 }
```

`distinctPerField` and `quantilePerField` are the effective caps after the global budget: min(cap, budget ÷ fields).

## `layout.regions[]`

```json
{ "kind": "preamble", "lines": [1, 6], "bytes": [0, 212], "lineKinds": { "keyValue": 5, "comment": 1 },
  "keys": ["Lot", "Wafer", "Temp", "Operator", "Date"] }
```

- Region kinds: `preamble`, `header`, `units`, `data`, `footer`, `block`, `unknown`.
- Lines are 1-based and inclusive. Bytes are 0-based and half-open.
- `block` regions carry `index` and an optional `label` (for example `"noise"` in Touchstone).
- Preamble `keys` are always included. `values`, aligned with `keys`, are **values tier**.

## `layout.headerConvention`

```json
{ "dominant": "nameParenUnit", "share": 0.769, "counts": { "nameParenUnit": 10, "none": 3 } }
```

## `table.fields[]`

```json
{
  "index": 7,
  "name": "Frequency(MHz)",
  "nameParts": { "base": "Frequency", "unit": "MHz", "convention": "nameParenUnit" },
  "type": "float",
  "dominant": { "type": "float", "share": 1.0 },
  "typeVotes": { "float": 9998, "int": 2 },
  "patterns": null,
  "count": 10000,
  "nulls": 0,
  "nullTokens": {},
  "flags": { "leadingZero": 0, "commaDecimalLike": 0, "padded": 0 },
  "distinct": { "value": 81, "approximate": false },
  "numeric": { "min": 700, "max": 2700, "mean": 1700, "stddev": 584.5, "quantiles": { "p01": 700, "p05": 800, "p25": 1200, "p50": 1700, "p75": 2200, "p95": 2600, "p99": 2700 }, "approximate": false },
  "temporal": null,
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

- `numeric` is for `int` and `float`. `temporal` (`min`, `max`, `quantiles` as ISO 8601) is for `date`, `datetime` and `time`. Both are always included.
- `patterns` gives the counts of matched date and time patterns.
- `distinct` past the cap, before sq-11: `{ "value": null, "exceeds": 10000 }`. From sq-11: `{ "value": n, "approximate": true, "errorBound": 0.0081 }`.
- `sourceType` is the native type for Parquet (for example `DECIMAL(10,2)`).
- `unit` is the unit from a units row. **Values tier.** Units parsed from header names are in `nameParts` and are always included.
- `top` (`[{ value, count }]`, plus `approximate` and `maxError`) and `text` (`{ min, max }`, lexical) are **values tier**.
- `monotonic` is one of `increasing`, `decreasing`, `nonDecreasing`, `nonIncreasing` or `none`.
- Shapes are sorted by count descending, then by mask (ordinal). Past the cap, the rest go into `{ "mask": "other", "count": n }`.

## `format.dialect` by family

| Family | Dialect keys |
|---|---|
| delimited | `delimiter`, `quote`, `hasHeader`, `unitsRow`, `blocks` |
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
{ "version": 1, "layout": "sha256:…", "typed": "sha256:…", "names": "sha256:…" }
```

The projections are defined in [design.md](design.md#structural-fingerprint).

## `hints`

```json
{
  "extension": ".csv",
  "magic": null,
  "stableLines": [{ "line": 0, "regex": "^Lot\\s*[=:]" }],
  "headerFields": ["Site", "Vth", "Idsat"],
  "requiredFields": ["Site", "Vth"],
  "fieldCount": { "min": 3, "max": 3 },
  "dataStartLine": 8,
  "delimiter": ",",
  "headerConvention": "nameParenUnit",
  "parquetColumns": null,
  "stdf": null
}
```

- `stableLines[].line` is 0-based, matching DataBall's `lineMatches`.
- Regexes escape only `\.^$|?*+()[]{}`, so they compile under both .NET and ECMAScript.
- Regexes for comment and text lines are **values tier**.

## `evidence[]`

```json
{ "stage": "dialect", "topic": "delimiter", "chosen": ",", "candidates": [
  { "value": ",", "score": 1.0, "reason": "k=3; 10000/10000 records; run lines 8-10007" },
  { "value": ";", "score": 0.0, "reason": "k=1; no run" } ] }
```

## Values tier

| Member | Tier |
|---|---|
| Header names, preamble keys, section names and keys, Parquet column names and KV keys, STDF test names | always |
| Numeric and temporal statistics, counts, types, shapes, lengths | always |
| `nameParts` and `headerConvention` | always |
| Region `values`, key=value values, Parquet KV values, comment text | values tier |
| `fields[].top`, `fields[].text`, `fields[].unit` (units row) | values tier |
| `stableLines` for comment and text lines | values tier |

## Versioning policy

- `schemaVersion` is `major.minor`. Adding optional keys bumps the minor version. Removing, renaming or changing the meaning of a key bumps the major version.
- Consumers must ignore unknown keys within a major version.
- `fingerprint.version` changes whenever a projection changes. Hashes from different versions cannot be compared.
- sq-12 makes this policy normative.
