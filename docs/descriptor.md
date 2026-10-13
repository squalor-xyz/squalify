# Descriptor schema v1 (draft)

Status: draft. The schema is frozen by roadmap item sq-12, which also publishes a JSON Schema file. Until then, fields may change without a version bump.

A descriptor is one UTF-8 JSON object. Keys appear in the order shown here. The determinism rules are in [design.md](design.md#determinism-rules).

## Top level

| Key | Type | Always | Description |
|---|---|---|---|
| `schemaVersion` | string | yes | Descriptor schema version, `"1.0"` |
| `tool` | object | yes | `{ "name": "squalify", "version": "x.y.z" }` |
| `options` | object | yes | Options that affect output, for example `{ "includeValues": false }` |
| `limits` | object | yes | The memory caps in effect (distinct, quantile, mask, region caps) |
| `file` | object | yes | Name (not path), size in bytes, `sha256` |
| `encoding` | object | yes | `kind` (`text`/`binary`), `charset`, `bom`, `lineEnding`, invalid-byte count |
| `format` | object | yes | `family` (`delimited`, `parquet`, `touchstone`, `keyValueTable`, `stdf`, `binary`, `text`), `dialect` (family-specific), `confidence` (0–1) |
| `layout` | object | yes | `regions[]` (see below) |
| `table` | object | when tabular | Row count, field count range, and `fields[]` |
| `binary` | object | when binary | Byte histogram summary, entropy blocks, `records` (STDF) |
| `fingerprint` | object | yes | `version`, `structure`, `fields` |
| `hints` | object | yes | Detect-rule hints |
| `issues` | array | yes | Problems found and their counts (ragged rows, bad bytes, truncation) |
| `evidence` | array | yes | The inference trail |

## `layout.regions[]`

```json
{ "kind": "preamble", "lines": [1, 6], "bytes": [0, 212], "lineKinds": { "keyValue": 5, "comment": 1 }, "keys": ["Lot", "Wafer", "Temp", "Operator", "Date"] }
```

The region kinds are `preamble`, `header`, `data`, `footer`, `block` and `unknown`. Line ranges are 1-based and inclusive. Byte ranges are 0-based and half-open. Preamble `keys` are always included. Values appear only under `includeValues`, as `values` aligned with `keys`.

## `table.fields[]`

```json
{
  "index": 0,
  "name": "Vth",
  "type": "float",
  "typeVotes": { "float": 9998, "int": 2 },
  "count": 10000,
  "nulls": 0,
  "nullTokens": {},
  "distinct": { "value": 8431, "approximate": false },
  "numeric": { "min": -0.125, "max": 1.75, "mean": 0.512, "stddev": 0.083, "quantiles": { "p01": 0.31, "p50": 0.51, "p99": 0.74 }, "approximate": false },
  "length": { "min": 4, "max": 7 },
  "shapes": [{ "mask": "9.9{3}", "count": 9120 }, { "mask": "-9.9{3}", "count": 880 }],
  "constant": false,
  "monotonic": "none",
  "unique": false,
  "values": null
}
```

- `numeric.min`/`max` are included by default, because they are statistics, not raw records. **(Proposed. The owner may want these behind `--include-values` too.)** Lexical min/max for strings and `top[]` (value plus count) appear only under `includeValues`.
- `monotonic` is one of `increasing`, `decreasing`, `nonDecreasing`, `nonIncreasing` or `none`. It helps spot index and sweep columns.
- Shapes are sorted by count descending, then by mask ordinal. Past the cap, the remainder is reported as `{ "mask": "other", "count": n }`.

## `fingerprint`

```json
{ "version": 1, "structure": "sha256:…", "fields": "sha256:…" }
```

The projection is defined in [design.md](design.md#structural-fingerprint).

## `hints`

```json
{
  "extension": ".csv",
  "magic": null,
  "stableLines": [{ "line": 1, "regex": "^Lot=\\S+$" }],
  "headerFields": ["Site", "Vth", "Idsat"],
  "requiredFields": ["Site", "Vth"],
  "fieldCount": { "min": 3, "max": 3 },
  "parquetColumns": null
}
```

`stableLines` regexes are built only from shapes and literal key text, never from values, unless `includeValues` is set.

## `evidence[]`

```json
{ "stage": "dialect", "topic": "delimiter", "chosen": ",", "candidates": [
  { "value": ",", "score": 1.0, "reason": "field count 3 on 10000/10000 data lines" },
  { "value": ";", "score": 0.0, "reason": "field count 1 on 10000/10000 data lines" } ] }
```

## Versioning policy (proposed)

- `schemaVersion` is `major.minor`. Adding optional keys bumps the minor version. Removing, renaming or changing the meaning of a key bumps the major version.
- Consumers must ignore unknown keys within the same major version.
- `fingerprint.version` changes whenever the projection changes, because old and new hashes cannot be compared.
