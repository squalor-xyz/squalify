# Descriptor schema v1 (draft)

Status: draft. The schema is frozen by sq-12, which also publishes `schema/descriptor-v1.schema.json`. Until then, fields may change without a version bump (sq-05 made `table.fields[]` normative without one).

A descriptor is one UTF-8 JSON object. Keys appear in the order shown here, and from sq-02 on that order is normative: `DescriptorWriter` (`src/squalify/Json/DescriptorWriter.cs`) writes every object with explicit keys in this order, and golden files pin it. The determinism rules are in [design.md](design.md#determinism-rules). Members marked **values tier** appear only with `--include-values`, or for a named member with `--include-values-for` (see [Values tier](#values-tier)). A descriptor made with `--layout-only` has the reduced shape described under [Layout-only profile](#layout-only-profile): everything below is for a full profile unless that section says otherwise.

The JSON examples below are compacted onto fewer lines, and long ones are cut or shortened, for reading. Only the `table.fields[]` example shows the exact layout the writer produces. The layout rule (small objects and arrays on one line, the rest one member or element per line) is in [Canonical form](#canonical-form).

## Canonical form

From sq-02 the writer produces these bytes. For the same input, options and squalify version they are identical on every OS, CPU and culture, within one .NET runtime major version (see **Text** below). Fingerprint hashes are identical across runtimes.

- **Layout.** UTF-8 without a BOM, LF line endings, two-space indentation, and exactly one LF after the closing brace. An object or array is written on one line, spaced, when that one-line form is at most 120 characters: `{ "a": 1, "b": [1, 2] }`, with one space after `{` and before `}`, one after each `:` and each `,`, and none inside `[` and `]`. This holds at any depth of the descriptor, and a container on one line holds every container inside it on that line. The 120 counts the container alone (not its indentation, its key or a trailing comma), in characters: a literal non-ASCII character is one character, and an escape such as `\u0001` is its written length. A container whose one-line form is longer is written with one member or element per line, two spaces deeper than its parent, and the rule applies again to each of its members; `[]` and `{}` are written for empty collections. Numbers and strings are written exactly as under Numbers and Text, so the one-line form holds the same characters as the multi-line form.
- **Text.** Printable characters in the Basic Multilingual Plane are written literally (`µA`, `Ω`, `é`; a U+FFFD that is already in the text is written literally too). The writer uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, so these are escaped: `\"`, `\\`, the short forms `\b \f \n \r \t`, and `\uXXXX` with upper-case hex for other control characters (including U+007F), for BMP code points that the runtime's Unicode tables leave unassigned (for example U+0378, written `\u0378`), and for a few invisible or non-character code points (for example U+0085, U+00A0, U+2028, U+FEFF). Characters outside the BMP are written as surrogate pairs: U+1F600 becomes the 12 characters `\uD83D\uDE00`. A lone UTF-16 surrogate cannot occur in text decoded from bytes; if one reaches the writer it is written as the escape `\uFFFD`. The set of unassigned code points comes from the .NET runtime, so the bytes are guaranteed only within one runtime major version: the same squalify version on a newer runtime major can write a different escape for the same text. Consumers pin the package and the runtime. squalify has no escape table of its own, and its version does not change with the runtime (see [design.md](design.md#determinism-rules)). A test pins the behavior on the current runtime. An escape that differs between runtime majors can also move a line break, because the one-line rule counts an escape at its written length (see Layout); the same guarantee applies to it.
- **Numbers.** A count or offset is an integer. A double is written in the shortest form that parses back to the same double (.NET `"R"`, invariant culture), for example `0.1`, `1700`, `1E-07` or `1.5E+300`. `-0` is written as `0`. NaN, `Infinity` and `-Infinity` have no JSON number form and are written as the strings `"NaN"`, `"Infinity"` and `"-Infinity"`. Statistics of `int` fields (`min`, `max`, quantiles) are integers; the others are doubles.
- **Key order.** The order of the tables and examples in this document. Where a member is not in an example, its place is listed under [Key order of nested objects](#key-order-of-nested-objects).
- **Collections.** Producers must build every collection sorted by the documented key, and the writer never sorts. Maps (`typeVotes`, `nullTokens`, `lineKinds`, `headerConvention.counts`, `patterns`) are ordered by key, ordinally (UTF-16 code unit order); `valuesPerLine` is ordered by its integer key, ascending. Arrays keep the order documented where they are described (`issues` by code, `shapes` by count then mask, `includeValuesFor` ordinally, and so on). `stdf.records` is ordered by `typ`, then `sub`, ascending. Evidence `candidates` are never in insertion order; see [`evidence[]`](#evidence). `hints.stableLines` is ordered by `line` ascending, then by `regex`, ordinally. An array whose order is not yet documented (for example `stdf.tests`) gets one from the slice that first produces it, in that slice's change to this document.
- **Aligned values lists.** A values-tier list that lines up with another list (`regions[].values` with `keys`, `sections[].values` with `keys`, `rows[].values` with the row's cells) has one entry per aligned item. An entry that is not opted in (for example by `--include-values-for`) is `null`.
- **Optional versus null.** A member documented as "when tabular", "keyValue", "when binary", "stdf" and the like is omitted when it does not apply. Members outside `table.fields[]` that appear in the examples with the value `null` (for example `hints.magic`, `format.dialect`) are always written, as `null` when they do not apply. Within a values-tier member (`top`, `text`, `unit`, `regions[].values`) the rule of that member applies: a values-tier member that the schema always writes is `null` without `--include-values`, except inside `table.fields[]`, where `top`, `text` and `unit` are left out then (see below). **Inside `table.fields[]` (sq-32) a member whose value is `null` is left out, and absent means `null`**, with these exceptions, which are written as `null` because the [consumer subset](#consumer-subset) lists them as nullable: `name`, `rawName` and `nameParts` (all `null` for a table with no header), `nameParts.unit`, `nullTokens`, `constant` and `statsWithheld`. This holds at any depth of a field (`numeric.mean`, `numeric.stddev`, `temporal.clock`, `temporal.min`, `temporal.max`, `temporal.quantiles` and so on). Further members are left out for what they say: `flags` when all three of its counts are 0 (absent means `{ "leadingZero": 0, "commaDecimalLike": 0, "padded": 0 }`), `nullSpellings` when it has no entry and `nullSpellingsOther` when it is 0 (absent means `{}` and `0`; sq-36), `nullSpellingsOtherDistinct` when it is 0 and `nullSpellingsOtherDistinctCapped` when it is false (absent means `0` and `false`; sq-43), and `approximate` (in `distinct`, `numeric`, `temporal` and `top`) when it is false or, for `temporal`, null (absent means exact; for `temporal` with no `quantiles` it means there are none). Members written only in some cases are listed below (`layoutOnly`, `scope`, `file.sha256`, `nameHex`, `index`, `regions`, `lineKinds`, `keys`, `values`, `rows`, `headerConvention`, `maxLineLength`, `rowClasses`, `rowClasses.allNullRowSpellingsOtherDistinctCapped`, `fields[].nullSpellingsOtherDistinct`, `fields[].nullSpellingsOtherDistinctCapped`, `distinct.relativeStdError`, `numeric.rankError`, `temporal.rankError`, `top.maxError`, `firstLines`, `offsets`, `sections[].values`, `annotation.rows[].values`). `binary.blocks` and `binary.segments` are also written only in some cases: they are omitted until sq-10 adds them.

### Key order of nested objects

Every object of the descriptor has a row here, or is listed inside the row of its parent as a name followed by its members in parentheses; the test `Schema_PropertiesMatchTheDocumentedMembers` compares these rows and the table under [Top level](#top-level) with `schema/descriptor-v1.schema.json`. A member or object that says *planned* is documented and not produced by this version: it is not in the schema (it is added in a 1.x minor release).

| Object | Keys, in order |
|---|---|
| `tool` | `name`, `version` |
| `options` | `includeValues`, `includeValuesFor` |
| `limits` | `distinctPerField`, `quantilePerField`, `shapesPerField`, `shapeCharsPerField`, `regions`, `headerSample`, `runawayQuote`, `fieldCountRuns`, `rowClasses`, `charsetTolerance` (`invalidSequences`, `perBytes`), `stdfTests`, `budget` (`quantileValues`, `distinctValues`, `shapeChars`), `sketches` (always; sq-38), `nullSpellings` (always; sq-42), `nullTokens`, `unitTable` |
| `limits.sketches` | `hllPrecision`, `rankError`, `budgetBytes` |
| `limits.nullSpellings` | `max`, `maxChars`, `maxDistinctOther` |
| `file` | `name`, `size`, `sha256` (omitted when only a prefix was read) |
| `scope` | `maxBytes`, `bytesRead`, `complete` |
| `encoding` | `kind`, `charset`, `bom`, `lineEndings` (`crlf`, `lf`, `cr`), `invalidBytes`, `invalidBytesTolerated` (only when true; sq-46), `controlBytes` |
| `format` | `family`, `dialect`, `score` |
| `format.dialect` (delimited) | `delimiter`, `quote`, `hasHeader`, `unitsRow`, `decimalSeparator`, `padding` (`trim`, `paddedShare`, `constantLineLength`, `lineLength`), `blocks` |
| `kind` | `label`, `ruleId`, `confidence` |
| `layout` | `regions`, `headerConvention` (tabular), `maxLineLength` (text and delimited; in characters) |
| `layout.headerConvention` | `dominant`, `share`, `counts` |
| `layout.regions[]` | `kind`, `index` (block), `lines`, `bytes`, `lineKinds`, `keys`, `values` (values tier), `rows` (annotation), `regions` (block: the child regions) |
| `layout.regions[].regions[]` | `kind`, `lines`, `bytes`, `lineKinds`, `keys`, `values` (values tier), `rows` (annotation) |
| `layout.regions[].rows[]` | `line`, `label` (null when the first field is numeric), `blankFields`, `nullTokens`, `typeVotes`, `type`, `values` (values tier) |
| `layout.regions[].regions[].rows[]` | `line`, `label` (null when the first field is numeric), `blankFields`, `nullTokens`, `typeVotes`, `type`, `values` (values tier) |
| `table` | `rows`, `fieldCount` (`min`, `max`), `rowClasses`, `fields` |
| `table.rowClasses` | `count`, `transitions`, `allNullRows`, `allNullRowSpellings`, `allNullRowSpellingsMixed`, `allNullRowSpellingsOther`, `allNullRowSpellingsOtherDistinct` (always; sq-43), `allNullRowSpellingsOtherDistinctCapped` (only when true; sq-43), `truncated`, `classes` (`id`, `count`, `fields`, `firstLine`, `lastLine`) |
| `table.fields[]` of a layout-only profile (sq-47) | `index`, `name`, `rawName`, `nameHex` (only when the name contains U+FFFD), `nameParts`; nothing else (see [Layout-only profile](#layout-only-profile)) |
| `table.fields[]` | `index`, `name`, `rawName`, `nameHex` (only when the name contains U+FFFD), `nameParts`, `type`, `dominant`, `typeVotes`, `patterns`, `count`, `nulls`, `nullsExcludingAllNullRows`, `nullTokens`, `nullSpellings` (left out when empty), `nullSpellingsOther` (left out when 0), `nullSpellingsOtherDistinct` (left out when 0; sq-43), `nullSpellingsOtherDistinctCapped` (only when true; sq-43), `flags`, `distinct`, `numeric`, `temporal`, `statsWithheld`, `length`, `shapes`, `monotonic`, `constant`, `unique`, `unit`, `top`, `text`; a member that is null is left out, except `name`, `rawName`, `nameParts`, `nullTokens`, `constant` and `statsWithheld` (see Optional versus null), and `flags` is left out when all its counts are 0 |
| `fields[].dominant` | `type`, `share` |
| `fields[].flags` | `leadingZero`, `commaDecimalLike`, `padded` |
| `fields[].length` | `min`, `max`, `mean` |
| `fields[].shapes[]` | `mask`, `count` |
| `fields[].text` | `min`, `max` |
| `fields[].nameParts` | `base`, `unit` (null when none), `convention`, `unknownUnit` |
| `fields[].distinct` | `value`, `approximate` (only when true), then `relativeStdError` (only when approximate) |
| `fields[].numeric` | `min`, `max`, `mean`, `stddev` (absent for fewer than two values), `quantiles` (`p01`, `p05`, `p25`, `p50`, `p75`, `p95`, `p99`), `approximate` (only when true), then `rankError` (only when approximate) |
| `fields[].temporal` | `orders`, `ambiguous`, `clock`, `min`, `max`, `quantiles` (`p01`, `p05`, `p25`, `p50`, `p75`, `p95`, `p99`), `approximate` (only when true), then `rankError` (only when approximate); `clock`, `min`, `max` and `quantiles` are absent when they are null (all of `min`, `max` and `quantiles` when ambiguous) |
| `fields[].top` | `items` (`value`, `count`), `approximate` (only when true), `maxError` (only when approximate) |
| `sections[]` (planned) | `name`, `line`, `keys`, `values` (values tier) |
| `binary` | `bytes` (`histogram`, `nul`, `printableAscii`, `high`, `entropy`), `blocks` (planned; sq-10: `count`, `min`, `max`, `mean`), `segments` (planned; sq-10) |
| `stdf.records[]` (planned) | `name`, `typ`, `sub`, `count` |
| `stdf.tests[]` (planned) | `testNum`, `kind`, `name`, `occurrences` |
| `hints` | `extension`, `magic`, `stableLines`, `headerFields`, `headerPrefix`, `nonNullFields`, `fieldCount`, `dataStartLine`, `delimiter`, `headerConvention`, `stdf` (always `null` until sq-10), `fileNameShape`; every member is always written, `null` when it does not apply (`stableLines` is `[]`) |
| `hints.magic` | `name`, `offset`, `hex` |
| `hints.stableLines[]` | `line`, `regex` |
| `hints.fieldCount` | `min`, `max` |
| `hints.stdf` (planned) | `cpuType`, `version` |
| `fingerprint` | `version`, `structure`, `layout`, `typed` (null when `layoutOnly` is true), `names` |
| `issues[]` | `code`, `count`, `firstLines` (or `offsets`: one of them, when the issue has locations), `offsets` (the issues `invalidUtf8` and `invalidUnicode` write `firstLines`, `offsets` and `regions` together, in that order), `regions` (only `invalidUtf8` and `invalidUnicode`; sq-46), `names` (only `includeValuesForUnmatched`) |
| `evidence[]` | `stage`, `topic`, `chosen`, `candidates` (`value`, `score`, `reason`) |

`encoding.charset` is `null` for files that are not text, and `encoding.bom` is the BOM name or `null`. See [`encoding`](#encoding).

## Top level

| Key | Type | Always | Description |
|---|---|---|---|
| `schemaVersion` | string | yes | `"1.0"` |
| `tool` | object | yes | `{ "name": "squalify", "version": "x.y.z" }` |
| `options` | object | yes | Options that affect output: `{ "includeValues": false, "includeValuesFor": [] }`. Two options are carried outside it: `layoutOnly` is the top-level member of that name, and the byte limit is in `scope`. `includeValuesFor` lists allow-listed names as a sorted set (ordinal order, exact duplicates dropped); names that match nothing are reported as the issue `includeValuesForUnmatched`. |
| `layoutOnly` | boolean | only with `--layout-only` | `true` when the profile skipped the per-field work (sq-47): then `hints` and `fingerprint.typed` are `null` and `table.fields[]` holds only names. Absent otherwise, never `false`. See [Layout-only profile](#layout-only-profile). |
| `limits` | object | yes | Caps and table versions in effect (layout-only: those a full profile would use). See below. |
| `file` | object | yes | `name` (not a path), `size` (bytes, of the whole file), `sha256` (omitted when only a prefix was read) |
| `scope` | object | with a byte limit | `maxBytes`, `bytesRead`, `complete`; see [`scope`](#scope). Absent when no limit was given. |
| `encoding` | object | yes | `kind` (`empty`/`text`/`binary`), `charset`, `bom`, `lineEndings {crlf, lf, cr}`, `invalidBytes`, `invalidBytesTolerated` (only when true), `controlBytes` |
| `format` | object | yes | `family` (`delimited`, `touchstone`, `keyValue`, `stdf`, `text`, `binary`, `empty`), `dialect` (family-specific), `score` (the winning profiler's score; 1 for `empty`) |
| `kind` | object | yes | `label`, `ruleId`, `confidence` (sq-15) |
| `layout` | object | yes | `regions[]`, `headerConvention` (tabular), `maxLineLength` (text and delimited; in characters) |
| `table` | object | when tabular | `rows`, `fieldCount {min, max}`, `rowClasses`, `fields[]` |
| `sections` | array | planned (keyValue) | `[{ name, line, keys[] }]` |
| `binary` | object | when binary | `bytes {histogram[256], nul, printableAscii, high, entropy}`, `blocks {count, min, max, mean}` and `segments[16]` (both from sq-10; omitted until then) |
| `stdf` | object | planned (stdf) | See below. |
| `fingerprint` | object | yes | `version`, `structure`, `layout`, `typed`, `names`; `typed` is `null` when `layoutOnly` is true |
| `hints` | object | yes | Detect-rule hints; `null` when `layoutOnly` is true |
| `issues` | array | yes | `[{ code, count, firstLines[], offsets[], regions[], names[] }]`, sorted by code; each entry has at most one of `firstLines`, `offsets` and `names`, except `invalidUtf8` and `invalidUnicode`, which have `firstLines`, `offsets` and `regions` together |
| `evidence` | array | yes | The inference trail |

## `limits`

```json
{ "distinctPerField": 10000, "quantilePerField": 100000, "shapesPerField": 64, "shapeCharsPerField": 53312, "regions": 1000,
  "headerSample": 1000, "runawayQuote": 65536, "fieldCountRuns": 100000, "rowClasses": 64,
  "charsetTolerance": { "invalidSequences": 8, "perBytes": 65536 },
  "stdfTests": 10000,
  "budget": { "quantileValues": 10000000, "distinctValues": 2000000, "shapeChars": 16000000 },
  "sketches": { "hllPrecision": 14, "rankError": 0.001, "budgetBytes": 67108864 },
  "nullSpellings": { "max": 64, "maxChars": 256, "maxDistinctOther": 64 }, "nullTokens": 1, "unitTable": 1 }
```

`distinctPerField` and `quantilePerField` are the effective caps after the global budget: min(cap, budget ÷ fields), with integer division, where fields is the table's field count (the caps of this tool version are 10,000 and 100,000, so a table of 100 fields or fewer keeps both, and 2,000 fields give 1,000 and 5,000). `shapesPerField` (64) and `budget` are written as set, and a file that is not a table writes the caps of the tool version. `shapeCharsPerField` is the effective cap on the characters of shape mask text that a field keeps, written next to the other per-field caps: min(53,312, `budget.shapeChars` ÷ fields), with integer division. 53,312 is 64 masks of the longest possible length (833 characters), so a table of up to 300 fields is never limited; a table of 2,000 fields gets 8,000 characters per field, and 64 masks of the usual length take a few hundred. A mask that would take a field past its cap is counted as `other`, like a mask past `shapesPerField`. `budget.shapeChars` (16,000,000) is the global budget that the division starts from. `sketches` (sq-38) is **always** written, for every file that has a `limits` block (a text, binary or empty file as well as a table), and says how far the per-field sketches were scaled down so that all of them nominally fit the sketch budget (the nominal size is exceeded by few-valued columns, see [Bounded memory and sketches](design.md#bounded-memory-and-sketches)): `{ "hllPrecision": 14, "rankError": 0.001, "budgetBytes": 67108864 }` is the full precision (above, and for a table of 547 fields or fewer), and `{ "hllPrecision": 12, "rankError": 0.004, "budgetBytes": 67108864 }` is what 2,000 fields give. A consumer reads the precision from here and never assumes 14 or 0.001. `hllPrecision` is the number of index bits of the HyperLogLog sketch of each field (2^12 = 4,096 registers; 14 is the full precision), `rankError` is the epsilon of its Greenwald-Khanna summaries as a fraction of the value count (0.001 at the full precision), and `budgetBytes` is the sketch budget that the nominal size of every field's sketches was scaled to fit (64 MiB, `MemoryBudget.SketchBudgetBytes`, a constant of its own apart from the memory ceiling's headroom). The members are a function of the field count only: 547 fields or fewer keep the full precision (14 and 0.001), up to 1,077 fields give 13 and 0.002, up to 2,083 give 12 and 0.004, up to 3,910 give 11 and 0.008, up to 6,853 give 10 and 0.015625, up to 11,275 give 9 and 0.03125, and more give 8 and 0.0625 (the floor; past 16,644 fields the budget is exceeded). Every sketched field of the table reports the same precision in its own `distinct.relativeStdError` and `numeric.rankError` / `temporal.rankError`. See [Bounded memory and sketches](design.md#bounded-memory-and-sketches). `headerSample` is the number of records of a delimited data run that are scored as header candidates; it also bounds the preamble of a block that follows another (see [Several blocks](#several-blocks)). `regions` is the most blocks of a delimited file that are recognised (1,000; see [Several blocks](#several-blocks)). `runawayQuote` is the longest open quoted field, in characters, before a delimiter candidate is disqualified, and `fieldCountRuns` is the most field-count runs a candidate may have before it is disqualified as too irregular (see [`format.dialect`](#formatdialect-by-family)). `nullSpellings` (sq-42) is **always** written, for every file that has a `limits` block, and holds the bounds on the exact null spellings (two from sq-42, a third from sq-43): `max` (64) is the most distinct spellings that a field keeps in `fields[].nullSpellings`, and that the table keeps in `table.rowClasses.allNullRowSpellings`, and `maxChars` (256) is the longest spelling kept, in characters. A new spelling past either bound is counted in `nullSpellingsOther` (of the field) or `allNullRowSpellingsOther` (of the table), and a spelling already kept keeps counting. `maxDistinctOther` (64) (sq-43) is the most distinct dropped spellings that are counted exactly in `nullSpellingsOtherDistinct` (of the field) or `allNullRowSpellingsOtherDistinct` (of the table): the count is exact up to it, and a further new dropped spelling sets the matching `Capped` marker instead. A consumer that must know whether a spelling was dropped, or how many different ones were, reads the three bounds here and never hard-codes 64, 256 and 64: `{ "max": 64, "maxChars": 256, "maxDistinctOther": 64 }` is what this tool version writes. The values are the constants that the profiler enforces (one source, so the descriptor cannot say another number), and they do not depend on the file or its field count, unlike `sketches`. `nullTokens` and `unitTable` are versions of fixed built-in tables. `unitTable` is the version of the table of known units (see [`layout.headerConvention`](#layoutheaderconvention)): version 1 is the units V, A, W, Hz, s, Ohm, F and H (each with the SI prefixes f, p, n, u, m, k, M and G, where u is also spelled with U+00B5 or U+03BC), the units dB, dBm, dBc, %, C, degree-sign C, K, degree-sign alone and `deg`, and the spelled-out aliases listed there. Any change to the table bumps the version. `nullTokens` is the version of the null vocabulary (always `ValueClassifier.NullVocabularyVersion`; no option sets it): version 1 is `""`, `NA`, `NA.`, `N/A`, `null`, `NaN` and `-`, matched ASCII case-insensitively after trimming U+0009 to U+000D, U+0020 and U+00A0 from both ends (a fixed set; other space characters, such as U+2028 and U+3000, are not trimmed). The vocabulary is never learned from the data; any change to it bumps this version. See [Value classification and types](design.md#value-classification-and-types).

`charsetTolerance` sets how many invalid UTF-8 sequences a file may hold and still keep its `ascii` or `utf-8` charset (see [`encoding`](#encoding)). The number allowed for a file is `min(invalidSequences, ceil(size / perBytes))`: at most 8 per file and at most 1 per 64 KiB, and a file shorter than 64 KiB tolerates one. For example, a 100-byte file tolerates 1, a 65,537-byte file 2, and any file of 8 × 64 KiB or more 8.

## `file`

```json
{ "name": "plain.txt", "size": 177, "sha256": "035c84ff…" }
```

`name` is the file name only: the profiler rejects a source name that is empty or contains `/` or `\` with an error. A path never reaches the descriptor, so the same bytes under the same name in two directories give the same descriptor. `size` is the byte length of the file, and `sha256` is the lower-case hex SHA-256 of the whole content. When the profile covers the whole file (no byte limit, or `scope.complete` true), both come from the byte pass over the bytes read, so `size` is the number of bytes read. With a byte limit that stops short of the end of the file (`scope.complete` false), `size` is the length that the source reports (the file's length, for a path) and `sha256` is **omitted**: a hash of the prefix presented as the file's would mislead, and the hash of the file needs a read of all of it, which the limit exists to avoid. When the limit covers the whole file, `sha256` is written as usual.

## `scope`

```json
{ "maxBytes": 1048576, "bytesRead": 1048570, "complete": false }
```

Written only when a byte limit is given (`--max-bytes`, `ProfileOptions.MaxBytes`); a profile without one has no `scope` member, so its bytes are the same as before the limit existed. It sits right after `file`.

- `maxBytes` is the limit as given. It must be positive; zero or less is an error.
- `bytesRead` is the number of bytes that the profile covers: bytes `0` to `bytesRead` of the file. Every other section describes that prefix only: `encoding`, `layout` (offsets, line numbers and counts), `table` (rows, fields and statistics), `binary`, `fingerprint` and `hints` are computed from it, exactly as if the prefix were a file of its own (the fingerprints of a limited profile equal those of a file that holds just that prefix, and they do not depend on `maxBytes` apart from where it makes the cut).
- `complete` is true when the profile covers the whole file (`bytesRead` equals `file.size`), including when the limit is larger than the file. A complete scope gives the same descriptor as no limit, plus the `scope` member.

**Where the cut falls.** `bytesRead` is at most `maxBytes`, with two exceptions.

- A CRLF that straddles the limit adds one code unit (one byte; two in UTF-16, four in UTF-32): a CR that ends the limit takes the LF after it, so a CRLF pair is never split.
- **A first record longer than the limit is read to its end, however long, with no ceiling** (unless the opt-in `--max-first-record-bytes` / `ProfileOptions.MaxFirstRecordBytes` is set: then a first record longer than that many bytes, terminator included, is refused with an error and no descriptor is produced; the ceiling is not written to the descriptor, and a file that passes gives the same descriptor as without it). When no line terminator lies inside the limit, squalify reads on to the end of that line, or to the end of the file when the file has no line terminator at all (it is then read whole, and `complete` is true). A file with data never gives an empty profile, and `bytesRead` exceeds `maxBytes` by the overshoot, which can be the whole file. The record caps (`limits.runawayQuote` and the others) limit how that record is profiled, not how much of it is read.

Otherwise a text file is cut just after its last line terminator (LF, CR or CRLF; one two-byte unit in UTF-16, one four-byte unit in UTF-32) that lies inside the limit, so the profile ends on a whole line. A limit that falls inside a code unit counts only the whole units before it. The class of the content decides the cut: when the bytes inside the limit are binary, the file is cut exactly at the limit; when they are text, at a line end as above. That decision, and the charset that sets the code unit, come from sniffing only the bytes inside the limit, so a limit shorter than a byte order mark can sniff differently from the whole file (a binary file with a tiny limit can be cut at a line terminator, a UTF-16 file read as one byte per unit, a UTF-32BE file called binary). The cut rule, with this caveat, is a documented contract in the [user guide](user-guide.md#reading-only-a-prefix); a change to it, including a change to the sniff that moves a cut, is a documented breaking change. After the cut, every section is computed from the cut prefix alone, including the text-or-binary decision (`encoding.kind`) and the charset. The prefix is the bytes inside the limit, except in the overshoot case, where it is longer. The cut looks only at line terminators, not at quoting: a quoted field that spans lines and straddles the limit is cut at a line break inside it, and the last record of the prefix is then a short one.

## Layout-only profile

Written only when the profile is made with `--layout-only` (library: `ProfileOptions.LayoutOnly`, an optional init member of `ProfileOptions` and an optional parameter of `ProfileOptions.Create`, false by default; sq-47). A consumer that only classifies a file reads the encoding, the dialect, the layout regions, the header names and the row classes, and does not need the per-field work. The option computes exactly those and skips the rest: no field is typed, no statistic, sketch, exact-value buffer or shape mask is made, and no hint is built. The pass over the rows keeps what the layout and the row classes need (the header sample, the padding facts, and one null decision per cell, which is the null-vocabulary check that is the first step of classifying a value); the cost that is left is reading and tokenizing the file, which the choice of the dialect needs in any case. The option does not change the default profile (owner decision, sq-47).

The descriptor marks what it left out, so a reader can tell "not computed" from "empty" (owner decision, 2026-10-30: a top-level `layoutOnly: true` plus null sections):

- **`layoutOnly`** is `true`, written right after `options`. It is written only with the option, and is never `false`: without the option the member is absent and the bytes are those of a profile made before this feature. It is not a member of `options`, which stays as it is (`options` holds `includeValues` and `includeValuesFor`, as `scope` carries the byte limit). A reader checks `layoutOnly` before it reads any member of the next three bullets.
- **`hints`** is `null`. The member is always written (see [Optional versus null](#canonical-form)), as `null` here for every file, tabular or not.
- **`fingerprint.typed`** is `null`: the typed projection needs the type of every field. `fingerprint.version` is 2 as without the option, and `structure`, `layout` and `names` are written and are the hashes that a full profile of the same bytes gives (no projection reads a type or a count; no projection changes). A reader that compares `typed` hashes cannot use a layout-only descriptor, and one that compares the other three can.
- **`table.fields[]`** holds `index`, `name`, `rawName`, `nameHex` (only when the name contains U+FFFD) and `nameParts`, with the meaning and the null rules they have in a full profile (all three of `name`, `rawName` and `nameParts` are `null` for a table with no header), and nothing else. Every other member of a field is absent: `type`, `dominant`, `typeVotes`, `patterns`, `count`, `nulls`, `nullsExcludingAllNullRows`, `nullTokens`, `nullSpellings`, `nullSpellingsOther`, `nullSpellingsOtherDistinct`, `nullSpellingsOtherDistinctCapped`, `flags`, `distinct`, `numeric`, `temporal`, `statsWithheld`, `length`, `shapes`, `monotonic`, `constant`, `unique`, `unit`, `top` and `text`. For a member that a full profile always writes (`type`, `dominant`, `typeVotes`, `count`, `nulls`, `nullsExcludingAllNullRows`, `nullTokens`, `constant`, `statsWithheld`) absence here means "not computed"; for a member that a full profile leaves out when it is null, the rule "absent means null" of a full profile does not apply, because `layoutOnly` says that nothing was computed.
- **`evidence`** has no entry of the stage `type`: the entry `type` / `dateOrderConflict` needs the types, so no entry does not say that no field has conflicting dates. Every other entry is the one of a full profile (stages `encoding`, `format`, `dialect`, `header` and `unit`; the `header` evidence reads column types of the header sample, which is part of the layout, so it is the same).

Everything else is written as for a full profile and is equal to it, which a test checks on every fixture of the repository and on further shapes (a wide padded table, repeated blocks with units rows, mixed blocks, truncated blocks, ragged rows, comment lines, duplicate and empty names, all-null separator rows of every spelling, invalid bytes, mixed line endings, excluded names, 2,000 columns, UTF-16): `schemaVersion`, `tool`, `limits`, `file`, `scope`, `encoding`, `format` (and its dialect, `padding` and `blocks`), `kind`, `layout` (regions, `headerConvention`, `maxLineLength`), `table.rows`, `table.fieldCount`, `table.rowClasses` (the ids, counts, lines, transitions and all-null spellings, because the null decision per cell is the same), `binary`, `sections`, `stdf` and `issues`.

- **Issues.** Every issue code that squalify writes comes from the byte pass, the format choice, the layout, the header or the options, and none from the analysis of the values of a field. So no code is dropped: `invalidUtf8`, `invalidUnicode`, `mixedLineEndings`, `nameExcludedFromFingerprint`, `includeValuesForUnmatched`, `raggedRows`, `commentInData`, `duplicateHeader`, `emptyHeaderName`, `heterogeneousBlocks`, `noDataRows`, `blocksTruncated` and `tooIrregular` are written with the same counts and locations as in a full profile (the issues `invalidUtf8` and `invalidUnicode` keep their `firstLines`, `offsets` and `regions`, and `encoding.invalidBytesTolerated` is kept). A code that a later slice adds and that needs a field's values will be left out and listed here.
- **`limits`** is written as for a full profile of the same file: it is a function of the tool version and of the field count, so `limits.sketches` and the per-field caps say what a full profile would have used. With `layoutOnly` true no per-field cap or sketch is in use.
- **Values.** `--include-values` and `--include-values-for` still open the preamble and annotation values of the layout, which are layout members and equal to a full profile's with the same options; they have no effect on the fields: no field value is computed, so no `top`, `text`, `unit` or statistic of a constant field exists to be written. `options` echoes the two options as given, and an `includeValuesFor` name still matches a preamble key, an annotation label or a field name, so `includeValuesForUnmatched` is written as in a full profile.
- **Other options.** `--max-bytes` writes `scope` and cuts the file as usual, `--max-first-record-bytes` refuses the same files with the same error, and `Profiler.ProfilePrefix` returns the same descriptor as `Profiler.Profile` for the same options.
- **Files that are not tables.** A text, binary or empty file has nothing per field to leave out: it is marked, `hints` and `fingerprint.typed` are `null`, and every other member is written as for a full profile (`binary` included).
- **The model.** `Descriptor.LayoutOnly` is `Options.LayoutOnly`. In a layout-only `Descriptor`, `Hints` and `Fingerprint.Typed` are null, and every `FieldProfile` has only `Index`, `Name`, `RawName`, `NameHex` and `NameParts` set (`Type`, `Dominant`, `TypeVotes`, `Count`, `Nulls` and `NullsExcludingAllNullRows`, which a full profile always sets, are null: six members say "not computed" in their types. The others do not: `NullSpellingsOther` and `NullSpellingsOtherDistinct` are 0, `NullSpellingsOtherDistinctCapped` is false, and `NullSpellings`, `NullTokens`, `Constant` and `StatsWithheld` are null, which in a full profile means none. A reader checks `Descriptor.LayoutOnly` before it reads a field). Equality is by value as for any descriptor, so a layout-only descriptor is never equal to a full one (the option differs, and so do the sections). `DescriptorWriter` refuses a descriptor whose shape contradicts the flag (a layout-only one that holds hints, a typed fingerprint, an evidence entry of the stage `type` or a field statistic, or a full one that lacks hints, the typed fingerprint or a field's type or counts) with a `SqualifyException`, so the bytes and the model cannot disagree.

## `encoding`

```json
{ "kind": "text", "charset": "ascii", "bom": null, "lineEndings": { "crlf": 0, "lf": 5, "cr": 0 },
  "invalidBytes": 0, "controlBytes": 0 }
```

`invalidBytesTolerated` is written, as `true`, only when the file holds invalid UTF-8 sequences and the charset was kept (`ascii` or `utf-8`) under the tolerance of rule 7 (`limits.charsetTolerance`); see below. The example shows a file without invalid bytes, so it has no such member.

The byte pass (pass 0) decides `kind` from whole-file counts, with these rules in order:

1. A zero-length file is `empty`.
2. A named binary magic is `binary`. Parquet is not in v1, so a Parquet file is described like any other unsupported binary: the byte profile and the `parquet` magic below. The magic table, in matching order (the first match wins), is: `parquet` (`PAR1` at the start and at the end), `stdf-le` (`02 00 00 0A`), `stdf-be` (`00 02 00 0A`), `gzip` (`1F 8B`), `zip` (`PK\x03\x04` or `PK\x05\x06`), `xz`, `bzip2` (`BZh`), `zstd`, `7z`, `tar` (`ustar` at offset 257), `ole2`, `pdf` (`%PDF-`), `png`, `sqlite`, `hdf5`, `duckdb` (`DUCK` at offset 8, the check DataBall uses to recognize a `.ball` file), `arrow` (`ARROW1`) and `netcdf` (`CDF\x01` or `CDF\x02`). Compressed and archive files are described as binary with their magic name; nothing is decompressed.
3. A UTF-16 or UTF-32 BOM is `text` with that charset.
4. A NUL-parity pattern is UTF-16 text without a BOM: NUL bytes are at least 30% of the file, and at least 90% of them are at odd offsets (`utf-16le`) or at least 90% at even offsets (`utf-16be`).
5. Any other NUL byte, or control bytes above 5% of the file, is `binary`.
6. Otherwise the file is `text`. The charset is `ascii` when every byte is below 0x80, `utf-8` when the content is valid UTF-8 (a UTF-8 BOM counts as valid content and is reported in `bom`), and `latin-1` otherwise. When `latin-1` is chosen and bytes 0x80 to 0x9F occur, the evidence reason adds "possibly windows-1252".
7. Stray invalid UTF-8 sequences within `limits.charsetTolerance` do not change the charset. It stays `ascii` (when every byte at or above 0x80 belongs to an invalid sequence) or `utf-8`, and the decoder maps each invalid sequence to U+FFFD. Beyond the tolerance, rule 6's `latin-1` applies.

Fields:

- `charset` is `ascii`, `utf-8`, `latin-1`, `utf-16le`, `utf-16be`, `utf-32le` or `utf-32be` for text, and `null` otherwise.
- `bom` is `utf-8`, `utf-16le`, `utf-16be`, `utf-32le`, `utf-32be` or `null`.
- `lineEndings` counts CRLF pairs, lone LF and lone CR in the decoded text of a text file (all zero for other files). A line break is one of those three.
- `invalidBytes` is the number of bytes in invalid units: invalid UTF-8 sequences when the charset is `ascii`, `utf-8` or `latin-1`, and invalid UTF-16 or UTF-32 units when the charset is `utf-16le`, `utf-16be`, `utf-32le` or `utf-32be`; it is 0 for other files. Where the invalid bytes are is in the issue `invalidUtf8` or `invalidUnicode` (see [`issues[]`](#issues)). An invalid UTF-16 unit is a lone high or low surrogate (2 bytes) or an odd trailing byte (1 byte). An invalid UTF-32 unit is a value above U+10FFFF or in the surrogate range D800-DFFF (4 bytes), or a truncated trailing unit of 1 to 3 bytes. Each such unit decodes to U+FFFD, and a U+FFFD that is really in the file is valid and not counted. "hello world\n" in UTF-16LE plus one stray byte gives `invalidBytes` 1 and the issue `invalidUnicode` with `offsets` of the stray byte. The `invalidUtf8` issue covers UTF-8 and ASCII input and the `invalidUnicode` issue covers UTF-16 and UTF-32; there is no tolerance for the latter, because the BOM or the NUL pattern fixes the charset and there is no fallback to apply.
- `invalidBytesTolerated` (sq-46) says that the label was kept despite invalid bytes: it is `true` when the charset is `ascii` or `utf-8` although `invalidBytes` is above 0, which means the file held stray invalid UTF-8 sequences within the tolerance of rule 7. A consumer that reads `charset` `utf-8` as "every byte is valid UTF-8" checks this member first. It is left out (absent means `false`) when there is no invalid byte, when the tolerance is exceeded (the charset is then `latin-1`, so no label is kept) and for UTF-16 and UTF-32, which have no tolerance (the issue `invalidUnicode` reports every invalid unit). It is written exactly when the issue `invalidUtf8` is.
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
- `hasHeader: false` leaves out `annotation`, `header` and `units`. A header with no data rows leaves out `data` (the issue `noDataRows` says so; see [`issues[]`](#issues)).
- A file with more than one block has `block` regions instead (see [Several blocks](#several-blocks)); a file with one block has the flat regions above.

### Several blocks

Lab exports often repeat a header and a table per site, temperature or sweep. squalify finds the blocks from the field counts (`FieldCountRle`) and the first records of each run (`BlockDetector`, `HeaderDetector.OpensWithHeader`):

- A **candidate** is a run of at least 5 records with k fields (k is the table's field count) that follows records with another field count (its **gap**), after the first run of the data run. It is a **block** by one of two rules (owner decisions 2026-10-27), tried in this order:
  1. **An exact repeat.** One of the first 5 records of the run (`BlockDetector.RepeatWindow`) equals the first block's header (every cell trimmed, compared exactly). That record is the block's header; the records of the run before it are the block's `annotation`, and the gap is its preamble. This also finds the blocks of a table of only text, and a header that follows a line of the table's own width (a `key,value` line in a two-column table).
  2. **Another header.** The first record of the run is a header line: all its fields are non-blank and pairwise distinct (ASCII case folding), and its values fail the column types below it in at least half of the typed columns (`BlockDetector.MinContrast`; a real header fails almost all of them, an error row of text in a numeric table fails few) and in one at least. The column types are the strict join of the next up to 100 records (`BlockDetector.SampleRecords`), with a units row directly after the header left out. And the gap holds at least one line that is no data row: a comment, a `key=value` line, or a line of one field. A line of two or more fields that is no `key=value` line (`S99,1100`, `7,8`) looks like a short data row and does not make a gap.
- If neither rule holds, the odd records are ragged rows of one table and the run is data, so one bad row cannot split a table, and the table is described as it is without block detection. A run of fewer than 5 records is never a block. The first block must be a block too: it needs at least 5 records of the table's width and a header; a table whose first block has no header, or fewer than 5 records, has one block.
- The records between the last record with k fields of one block and the header of the next are the next block's **preamble**, not ragged rows. A preamble of more than 1,000 records (`limits.headerSample`) does not start a block.
- At most `limits.regions` (1,000) blocks are recognised. From the first candidate after that, nothing is profiled any more (as after a block with another header) and the issue [`blocksTruncated`](#issues) counts the candidates left, so the records of the later blocks are not read as rows of the last one.
- A **block region** has `index` (0 for the first block, in file order), `lines`, `bytes` and `regions`, its child regions in file order: `preamble` (block 0: when the data run does not start on line 1), `annotation`, `header`, `units` (when the block has a units row) and `data`. The nesting is exactly one level: a block's children are never `block` regions. There is no label and no other member: a consumer reads the file to name a block. The blocks tile the file: block 0 starts at byte 0 and each block ends where the next begins. The `footer` region, when there is one, follows the last block at the top level.
- `format.dialect.blocks` is the number of blocks (1 for a table without blocks, whose regions are flat). `unitsRow` is the first block's, `decimalSeparator` is decided once from the header sample (the first records of the data run), and `padding` describes the whole data run, from the first block's first record to the last record with k fields. `hints` (`dataStartLine`, `headerFields`, `stableLines`) and `fields[].unit` come from the first block.
- The fields are **aggregated** across the leading blocks whose header text equals the first block's (trimmed, compared exactly): one `table.fields[]`, one `table.rows`, and one set of `table.rowClasses`. Blocks are read in one pass, so aggregation stops at the first block whose header differs: that block and every later one, even one with the first header again, are **not profiled**: their records are in no field, row class or `table.rows`, they raise the issue [`heterogeneousBlocks`](#issues), and their regions are still listed. Row classes never count a class transition across a block boundary (`RowClassAccumulator.Break`); a class's `firstLine` and `lastLine` span the blocks.
- `fingerprint`: see [`fingerprint`](#fingerprint). Evidence: `dialect` / `blocks` (see [`evidence[]`](#evidence)).
- **Not recognised.** Blocks separated by blank lines only (the repeated header lies inside one run of k-field records, with no gap), blocks with another field count or delimiter, and blocks nested in blocks. A repeated header inside a run stays a data row (a string vote).

For `delimited/per-site-blocks.csv` (`tests/squalify.Tests/fixtures/`, three sites, each with a two-line preamble, the same header and six rows, and a `Rows=18` line at the end), the regions are:

```json
{ "regions": [
    { "kind": "block", "index": 0, "lines": [1, 10], "bytes": [0, 168], "regions": [
        { "kind": "preamble", "lines": [1, 3], "bytes": [0, 46], "lineKinds": { "comment": 1, "keyValue": 2 }, "keys": ["Site", "Temp(C)"] },
        { "kind": "header", "lines": [4, 4], "bytes": [46, 75] },
        { "kind": "data", "lines": [5, 10], "bytes": [75, 168] } ] },
    { "kind": "block", "index": 1, "lines": [11, 19], "bytes": [168, 312], "regions": [
        { "kind": "preamble", "lines": [11, 12], "bytes": [168, 190], "lineKinds": { "keyValue": 2 }, "keys": ["Site", "Temp(C)"] },
        { "kind": "header", "lines": [13, 13], "bytes": [190, 219] },
        { "kind": "data", "lines": [14, 19], "bytes": [219, 312] } ] },
    { "kind": "block", "index": 2, "lines": [20, 28], "bytes": [312, 455], "regions": [ "..." ] },
    { "kind": "footer", "lines": [29, 29], "bytes": [455, 463], "lineKinds": { "keyValue": 1 } } ] }
```

Block regions are written only when `dialect.blocks` is greater than 1.

## `binary.bytes`

```json
{ "histogram": [8, 0, 1, …], "nul": 8, "printableAscii": 18, "high": 16, "entropy": 4.793773756368349 }
```

`histogram` has 256 counts, indexed by byte value. `nul` is the count of 0x00 bytes, `printableAscii` of 0x20-0x7E, and `high` of bytes at or above 0x80. `entropy` is the Shannon entropy in bits per byte, `-sum(p * log2 p)` over the bins 0 to 255 in order, with the deterministic `DetMath.Log2`. `blocks` and `segments` are added by sq-10 and are omitted until then.

## Interim members

Until the slices that own them land, these members hold fixed interim values (or `null`), so that every file yields a valid descriptor:

- `kind` follows the byte-pass class: `empty` (`K12`), `binary` (`K11`) or `text` (`K09`), with confidence 1. sq-15 replaces it with the rule table.
- `table.fields[]` of a delimited file holds real values for every member except those of later slices: `top` and `text` (values tier) are absent unless values are opted in for the field (see [Values tier](#values-tier)); `nameParts` is computed from the header by sq-14 (it is written as `null` only for a table with no header). A consumer reads an absent `top`, `text` or `unit`, and a `null` `nameParts`, as "not computed". See [`table.fields[]`](#tablefields) for the rest; `unit` is absent unless values are opted in for the field (`--include-values`, or its name in `--include-values-for`), and when the units cell is null.
- `hints.stdf` is `null` until sq-10 adds the STDF profiler; every other `hints` member is computed (see [`hints`](#hints)).

## `layout.regions[]`

```json
{ "kind": "preamble", "lines": [1, 6], "bytes": [0, 212], "lineKinds": { "keyValue": 5, "comment": 1 },
  "keys": ["Lot", "Wafer", "Temp", "Operator", "Date"] }
```

- Region kinds: `preamble`, `annotation`, `header`, `units`, `data`, `footer`, `block`, `unknown`.
- Lines are 1-based and inclusive. Bytes are 0-based and half-open.
- `block` regions carry `index` (0-based) and `regions`, the child regions of the block (see [Several blocks](#several-blocks)). They have no label. Only a file with more than one block has them.
- Preamble `keys` are always included, normalized (trimmed, trailing `:` or `=` removed). `values`, aligned with `keys`, are **values tier**.
- An `annotation` region holds the column-aligned rows between the start of the table run and the header:

  ```json
  { "kind": "annotation", "lines": [7, 8], "bytes": [210, 655], "rows": [
    { "line": 7, "label": "Upper", "blankFields": 1, "nullTokens": { "NA": 2 }, "typeVotes": { "float": 9 }, "type": "float" } ] }
  ```

  `label` is the normalized first field when it is non-numeric, else `null`; it is always included. When there is a label, that cell is left out of `blankFields`, `nullTokens`, `typeVotes` and `type`. `blankFields` counts blank cells; `nullTokens` counts the other null-vocabulary cells by the vocabulary's spelling (`NA.`, `NaN`, ...); `typeVotes` counts the non-null cells by type; `type` is their strict join (`empty` when there are none). The row cells (`values`, every cell trimmed, the label included) are **values tier**.

## `layout.headerConvention`

```json
{ "dominant": "nameParenUnit", "share": 0.8461538461538461, "counts": { "nameParenUnit": 11, "none": 2 } }
```

Written for a delimited table that has a header (sq-14), and omitted otherwise. It reports how the header names carry units. Every header name is parsed against these conventions, in this order, and the first match wins (see [design.md](design.md#header-unit-conventions)):

| Convention | Rule | Example |
|---|---|---|
| `nameParenUnit` | The name ends with `)`, the last `(` lies after the first character, and the text inside is not empty. The base name and the unit are trimmed and neither is empty. The unit text is taken as written; it need not be in the unit table | `I_Total(A)` |
| `nameBracketUnit` | The same rule with `[` and `]`, except that unit text of only ASCII digits is not a unit (an array index, as in the trace columns `Trace[0]` to `Trace[n]`); such a name falls through to the next rules | `Pout[dBm]`; `Trace[0]` is `none` |
| `nameUnderscoreUnit` | The last `_` is neither the first nor the last character, and the text after it, trimmed, is in the unit table. The text before it, trimmed, is the base | `VCC_V`, `ICC_A`; `I_Total` is not one |
| `unitsRow` | The file has a units row, the name matched none of the three above, and the units cell of that column has a value (an empty or null cell leaves the name `none`) | `Freq` over a units row holding `MHz` |
| `none` | Anything else; the base is the whole name | `SN`, `I_Total` |

Parsing mirrors DataBall's `HeaderParser`, except that the bracket form and `unitsRow` are squalify's own. Names are the trimmed header text (U+0009 to U+000D, U+0020 and U+00A0 are trimmed from both ends). Matching never uses the Unicode tables or the current culture.

- `counts` has an entry for each convention that occurs, keyed ordinally. `share` is the count of the `dominant` convention over the number of header names. The `dominant` convention is the most common one; a tie goes to the earlier convention in the order of the table above, so `none` loses every tie. With no header names the dominant is `none` and the share is 0 (a table with no header omits the object, so this does not occur in a descriptor).
- The unit table, version 1 (`limits.unitTable`), is matched case-sensitively and exactly, except for the aliases. Base units: `V`, `A`, `W`, `Hz`, `s`, `Ohm`, `F`, `H`, `dB`, `dBm`, `dBc`, `%`, `C`, `K`, `Ω` (U+03A9, the Greek capital omega), the ohm sign (U+2126), `°C` (U+00B0 then `C`), and the angle units `°` (U+00B0 alone) and `deg`, which take no prefix. The prefixes `f`, `p`, `n`, `u`, `µ` (U+00B5), `μ` (U+03BC), `m`, `k`, `M` and `G` combine with `V`, `A`, `W`, `Hz`, `s`, `Ohm`, `Ω`, `F`, `H`. Aliases, matched with ASCII letters folded to lower case: `amps`, `amp`, `ampere`, `amperes`, `volt`, `volts`, `sec`, `second`, `seconds`, `degC`, `celsius`, `ohm` (the only alias that takes a prefix, and the prefix stays case-sensitive: `mohm` is milli, `Mohm` is mega), `ohms` (no prefix), `watt`, `watts`, `pct`, `percent`. There is no `id` unit.
- Everything in `layout.headerConvention` and `fields[].nameParts` comes from header names, so it is always included. See [`table.fields[]`](#tablefields) for `nameParts`.
- With the evidence entry `unit` / `headerConvention` (see [`evidence[]`](#evidence)) and the hint `hints.headerConvention`, which repeats `dominant`.

## `table.rowClasses`

```json
{ "count": 2, "transitions": 80, "allNullRows": 40, "allNullRowSpellings": { "NULL": 38, "NA": 1 }, "allNullRowSpellingsMixed": 1,
  "allNullRowSpellingsOther": 0, "allNullRowSpellingsOtherDistinct": 0, "truncated": 0, "classes": [
  { "id": "rc:3f9a2c07d41be855", "count": 900, "fields": [[0, 11]], "firstLine": 3, "lastLine": 1002 },
  { "id": "rc:ef46db3751d8e999", "count": 40, "fields": [], "firstLine": 4, "lastLine": 1001 } ] }
```

The first id is illustrative. The second is real: it is the all-null class, the `XxHash64` of empty input (seed 0), the same in every file.

Present for every delimited table (with no data row it is `{ "count": 0, "transitions": 0, "allNullRows": 0, "allNullRowSpellings": {}, "allNullRowSpellingsMixed": 0, "allNullRowSpellingsOther": 0, "allNullRowSpellingsOtherDistinct": 0, "truncated": 0, "classes": [] }`). Data records are grouped by their set of non-null fields (sq-17), so a file that interleaves record kinds (measurement rows, setup rows, error rows and all-null separator rows) shows each kind once, however often it switches. Null is the field's null decision: a value of the null vocabulary (see [`table.fields[]`](#tablefields)). The header, units, annotation and comment lines are not data records, and a ragged record (another field count than the header's) has no null mask: it is in no class, is not an all-null record and does not count as a transition (the records on each side of it are adjacent).

- **`id`** is `rc:` and 16 lowercase hex digits: the `XxHash64` (seed 0) of the class's non-null field indexes in ascending order, written as invariant decimal numbers separated by commas and encoded as UTF-8 (fields 1 and 10 hash the bytes of `1,10`; a class with no non-null field hashes no bytes). The id depends on the column order and on nothing else (not on the line, the order of the records or the file), so it compares across files only when the column set matches. Two different sets share an id with probability 2^-64, and classes are keyed by this hash.
- **`fields`** is the non-null field indexes as inclusive ranges `[[a, b], ...]`, ascending and non-adjacent; `[]` for a class in which every field is null.
- **`count`** is the number of records in the class. **`firstLine`** and **`lastLine`** are the 1-based file lines on which the first and the last record of the class start (as `layout.regions[].lines`; a record with a quoted line break counts at its first line).
- **The all-null class.** A record in which every field is null is a class like the others (`fields` is `[]`), and is also counted in `allNullRows`, the number of such records. A consumer that wants the data rows takes `table.rows` minus `allNullRows`. A null here is the null vocabulary's, which is wider than a single token (`NULL`, `NA`, `N/A`, `NaN`, `-`, blank and the others).
- **`allNullRowSpellings`** (sq-36) counts the all-null records whose **every cell has the same exact spelling**, by that spelling: the raw cell text as the dialect delivers it, with the quotes removed and nothing trimmed (`NULL`, `NA`, ` NULL `; `""` for a record of blank cells such as `,,`). It is a map ordered by key, ordinally, and it is always written, `{}` when there is none. **`allNullRowSpellingsMixed`** counts the all-null records whose cells do not share one spelling (`NULL,NA,NULL`, or `NULL,null,Null`), and **`allNullRowSpellingsOther`** the all-null records of one spelling that is not a key because the table already holds `limits.nullSpellings.max` (64) spellings, or the spelling is longer than `limits.nullSpellings.maxChars` (256) characters (a count member, not a map key, so no key can collide with a spelling). The three are always written, and `sum(allNullRowSpellings) + allNullRowSpellingsMixed + allNullRowSpellingsOther == allNullRows`. **`allNullRowSpellingsOtherDistinct`** (sq-43) is how many *different* spellings those `allNullRowSpellingsOther` records hold, so that one long spelling can be told from many: each distinct dropped spelling counts once however often it recurs, whether it was dropped by the 64-spelling bound or by the 256-character bound, compared as exact text (not trimmed: `NA ` and `NA` are two). It is always written (0 when `allNullRowSpellingsOther` is 0), and `0 <= allNullRowSpellingsOtherDistinct <= allNullRowSpellingsOther`, with at least 1 when `allNullRowSpellingsOther` is positive. The count is exact up to `limits.nullSpellings.maxDistinctOther` (64) distinct dropped spellings; when more were seen it stays at that cap and **`allNullRowSpellingsOtherDistinctCapped`** is written as `true`, so the count is then 64 and means that more than 64 distinct spellings were seen (a lower bound: the true number is at least 65). The marker is written **only when true**, unlike the count, which is always written: absent means `false`. The count compares the spellings through a 64-bit hash of each (see the field member below), so it is exact up to hash collisions, which are possible in principle (probability about 2^-64 per pair) and would make the count one too low. A mixed or uniform record is classified from its cells in the profiled blocks only, as `allNullRows` is. The spelling is vocabulary text plus padding, never data, so the member is written without the values tier. How a consumer uses it is in [F9](#derivation-formulas).
- **`transitions`** is the number of adjacent data records whose classes differ, over all records that have a mask, truncated ones included.
- **`classes`** is sorted by `count` descending, then by `id` ordinally.
- **`count`** (of the object) is the number of classes listed, at most `limits.rowClasses` (64). Past the cap the first 64 classes seen keep exact counts, including records that come after the cap was reached, and the records of every later class are counted in **`truncated`** (so `truncated > 0` means more classes exist than are listed, and the classes' `count`s plus `truncated` equal the records that have a mask).

## `table.fields[]`

From sq-05 this section is normative for delimited files. The members are computed by `FieldAccumulator` (see [design.md](design.md#field-profiling)).

```json
{
  "index": 7,
  "name": "Frequency(MHz)",
  "rawName": "Frequency(MHz)",
  "nameParts": { "base": "Frequency", "unit": "MHz", "convention": "nameParenUnit", "unknownUnit": false },
  "type": "float",
  "dominant": { "type": "float", "share": 1 },
  "typeVotes": { "float": 9998, "int": 2 },
  "count": 10000,
  "nulls": 0,
  "nullsExcludingAllNullRows": 0,
  "nullTokens": {},
  "distinct": { "value": 81 },
  "numeric": {
    "min": 700,
    "max": 2700,
    "mean": 1700,
    "stddev": 584.5,
    "quantiles": { "p01": 700, "p05": 800, "p25": 1200, "p50": 1700, "p75": 2200, "p95": 2600, "p99": 2700 }
  },
  "statsWithheld": null,
  "length": { "min": 3, "max": 6, "mean": 5.2 },
  "shapes": [{ "mask": "9{4}", "count": 9120 }, { "mask": "9{3}", "count": 880 }],
  "monotonic": "nonDecreasing",
  "constant": false,
  "unique": false
}
```

This is the field as written: the members that are `null` here (`patterns`, `flags` with all counts 0, `temporal`, `unit`, `top`, `text`, and `approximate` when false) are left out, `statsWithheld` is kept as `null`, and `numeric` is over 120 characters on one line, so it is written with one member per line (see [Canonical form](#canonical-form)).

A temporal field, for example:

```json
{
  "type": "date",
  "patterns": { "n/n/yyyy": 40 },
  "temporal": { "orders": ["DMY", "MDY"], "ambiguous": true }
}
```

- **`null` below.** Where the text below says a member is `null` (`patterns`, `numeric`, `temporal`, `length`, `monotonic`, `unique`, `unit`, `top`, `text`, `numeric.stddev`, `temporal.clock`, and so on), the member is left out of the written field, and absent means `null`; see [Optional versus null](#canonical-form). Only `name`, `rawName`, `nameParts`, `nameParts.unit`, `nullTokens`, `constant` and `statsWithheld` are written as `null`. The rule is for the written JSON: the model still holds `null`.
- **Counts.** `count` is the non-null plus the null values of the data records that have the header's field count; ragged records are not typed. `nulls` counts the null values, and `nullsExcludingAllNullRows` those outside records in which every field is null (all-null records are counted in [`table.rowClasses.allNullRows`](#tablerowclasses); so `nulls - nullsExcludingAllNullRows` equals `allNullRows` for every field). The row-level count is made once, by the `RowClassAccumulator` that decides which records are all-null, and the field profile carries it. The sum of `typeVotes` is `count - nulls`.
- **`type`, `dominant`, `typeVotes`.** `type` is the strict join of the votes over the lattice `empty` < `bool`; `empty` < `int` < `float`; `empty` < `date` < `datetime`; `empty` < `time`; any other join is `string` (`empty` when there is no non-null value). It is also `string` when the date values share no order (see `temporal`). `dominant` is the plurality vote with its share of the non-null votes (`empty` with share 0 when there are none). A tie goes to the earlier type in the order `bool`, `int`, `float`, `date`, `datetime`, `time`, `string`: the narrower of `int` and `float` or of `date` and `datetime`, and, between types that are not comparable, `bool` before `int`, `int` before `date`, `date` before `time`. `typeVotes` counts the non-null votes by type, so it can disagree with `type`.
- **`nullTokens`** counts the null values by **null-vocabulary entry**, not by the spelling in the file: the keys are `""` (blank and white space), `NA`, `NA.`, `N/A`, `null`, `NaN` and `-`, and `NULL`, `Null` and `null` are all counted under `null`. It is always written, `{}` when there are no nulls, and `NaN` is a key of its own, so a consumer that reads `NaN` as a number can tell it apart. Keys are ordered ordinally and the counts add up to `nulls`.
- **`nullSpellings`** (sq-36) counts the null values by the **exact spelling in the file**, next to `nullTokens`, which does not change and which still counts by vocabulary entry. The key is the field text as the dialect delivers it, with the quotes removed, and **not trimmed**: `NULL`, `Null`, `null` and ` NULL ` are four keys, `NA` and `NA ` (or a tab before it, or a no-break space) are different keys, a blank field is the key `""` and a field of spaces is a key of its own. It is the text of a value that the null vocabulary matched, so a key is a vocabulary entry in some ASCII case with trim-set characters around it, never other data, and the member is written without the values tier, like `nullTokens`. The counts are over **all data records**, the all-null records (separator rows) included, like `nullTokens`, so `sum(nullSpellings) + nullSpellingsOther == nulls`, which is also `sum(nullTokens)`: the two maps split the same total, one by vocabulary entry and one by spelling. A ragged record is in neither. How the all-null records spell themselves is in [`table.rowClasses`](#tablerowclasses) (`allNullRowSpellings`). Keys are ordered ordinally. The member is left out when it has no entry (a field with no null): absent means `{}`.
  - **`nullSpellingsOther`** is the number of those null values whose spelling is not a key. A field keeps the first `limits.nullSpellings.max` (64) distinct spellings in file order and only those of at most `limits.nullSpellings.maxChars` (256) characters (a null padded with more white space than that is not kept; fixed-width exports pad a blank to the column width); a new spelling past either bound is counted here, and a spelling already kept keeps counting. The two bounds are published in [`limits.nullSpellings`](#limits) (`max` 64, `maxChars` 256; sq-42), and the memory of the kept spellings is counted in the field's estimate. The member is left out when it is 0: absent means 0, so the invariant above is `sum(nullSpellings) + nullSpellingsOther == nulls` with a missing member read as empty or 0.
  - **`nullSpellingsOtherDistinct`** (sq-43) is the number of *different* spellings among those `nullSpellingsOther` values, so that a consumer can tell one long spelling from many: each distinct dropped spelling counts once however often it recurs, whether it was dropped by the 64-spelling bound or by the 256-character bound, compared as exact text (not trimmed: ` NULL` and `NULL` are two). It is left out when `nullSpellingsOther` is 0 (absent means 0), and `0 <= nullSpellingsOtherDistinct <= nullSpellingsOther`, with at least 1 when `nullSpellingsOther` is positive. The count is **exact up to `limits.nullSpellings.maxDistinctOther` (64) distinct dropped spellings**: when more were seen it stays at that cap (a further new spelling is neither counted nor remembered) and **`nullSpellingsOtherDistinctCapped`** is written as `true`, so the count is then 64 and means that more than 64 distinct spellings were seen (a lower bound: the true number is at least 65). The marker is written only when true (absent means `false`). A field remembers one 64-bit hash for each of the first 64 distinct dropped spellings (8 bytes each, at most 0.5 KiB a field that spills; a field that never drops a spelling remembers nothing and writes neither member). The hash is `XxHash64` of the spelling's UTF-16 code units, each written as two bytes with the low byte first on every platform (so a lone surrogate is itself, not U+FFFD), with the spelling's length in characters as the seed; it does not depend on the culture, the machine or the run. Two different spellings can share a 64-bit hash (probability about 2^-64 per pair, so about 2^-53 for a field that fills its 64 places); the count is therefore exact **up to hash collisions**, and a collision would make it one too low. **A single spelling:** a consumer reads "the field has one spelling" as `keys(nullSpellings) + nullSpellingsOtherDistinct == 1` with `nullSpellingsOtherDistinctCapped` not true (see [F9](#derivation-formulas)); the cap cannot hide a second spelling for that test, because a capped count is 64 and the true number is more than 64.
- **`patterns`** gives the counts of matched date, datetime and time patterns, keyed by pattern; it is null for fields with no such value. A pattern is the order-neutral layout of a value: `yyyy` for a 4-digit year, `nn` or `n` for a 1–2 digit day or month, with the separator kept (`n/n/yyyy` for `6/5/2026`, whichever order it is; the orders are in `temporal.orders`); `HH`/`H` (24-hour) or `hh`/`h` (12-hour) for the hour with `:mm`, `:ss`, `.f+` (a fraction of any length) and `tt` (an `AM`/`PM` marker, preceded by a space when the value has one); `Z`, `zz` (`+hh`), `zzzz` (`+hhmm`) or `zzz` (`+hh:mm`) for a zone offset; and a literal `T` or space between date and time. For example `yyyy-nn-nn`, `nn/n/yyyy hh:mm:ss tt` and `yyyy-nn-nnTHH:mm:ssZ`.
- **`flags`.** The member is left out when all three counts are 0. `leadingZero` counts numbers whose integer part starts with `0` and another digit (`007`), `commaDecimalLike` the values that match `^[+-]?[0-9]+,[0-9]+$` whatever the decimal separator, and `padded` the non-empty values whose raw text starts or ends with a trim-set character (null tokens included).
- **`distinct`** is `{ "value": n }` (`approximate` is left out when false), the exact number of distinct non-null values (compared as trimmed text, so `1` and `1.0` are two; `0` when there are none), up to `limits.distinctPerField`. Past that cap it is a HyperLogLog estimate, `{ "value": n, "approximate": true, "relativeStdError": 0.008125 }`: precision 14 (16,384 registers; fewer for a table of more than 547 fields, see [`limits`](#limits)) over the 64-bit xxHash64 (seed 0, UTF-8) of the trimmed value, linear counting while the raw estimate is at most 3.5 times the register count (about 57,000 values at precision 14), rounded half to even; the estimate is never below `limits.distinctPerField + 1`, which the exact set had already proved. `relativeStdError` is one standard error of the sketch, `1.04 / sqrt(m)` for its `m` registers (`1.04 / sqrt(16384)` = 0.008125 at the full precision), written as the same number for every sketched count of the table (present only when `approximate` is true). For a table of more than 547 fields the sketch has fewer registers and the number is larger: 0.01149 (m = 8,192, up to 1,077 fields), 0.01625 (4,096; up to 2,083 fields, so 2,000), 0.02298 (2,048), 0.0325 (1,024), 0.04596 (512) and 0.065 (256), as `limits.sketches.hllPrecision` says (sq-38). It is a one-sigma figure, not a hard bound: for large counts an estimate is within three times it (2.4 percent) of the true count in about 99.7 percent of cases. The raw estimate runs several percent high just above 2.5 times the register count (the reason HyperLogLog++ has bias tables, which are not implemented), so the switch to it is made later, where linear counting and the raw estimate meet; the worst error measured between 30,000 and 120,000 distinct values is 1.4 percent. The same set of values gives the same estimate in any order.
- **`numeric`** is for `int` and `float` fields; it is `null` for other types. `min` and `max` are integers for `int` and doubles for `float`. `mean` and `stddev` (the sample standard deviation, `null` for fewer than two values) are computed by Welford in file order. A running mean or sum of squares that overflows (values near the limits of a double, such as `1.7E308` and `-1.7E308`) would be infinite or NaN; the member is then `null`, never a string, and the two are decided separately (`mean` can be written while `stddev` is `null`). The `quantiles` are the lower nearest rank of the sorted values: `p01`, `p05`, `p25`, `p50`, `p75`, `p95` and `p99` are the value at zero-based index `ceil(p * n) - 1`, with no interpolation, and are integers for `int`. Up to `limits.quantilePerField` values they are exact and `approximate` is left out (it means false). Past that cap they come from a Greenwald-Khanna summary (`approximate` is `true`, and `rankError`, a sibling of `quantiles` and `approximate`, is `0.001` at the full precision, written as the same number for every sketched field of the table and absent when the quantiles are exact; a table of more than 547 fields has a larger one, `limits.sketches.rankError`, from 0.002 up to 0.0625, sq-38): `rankError` is the worst-case rank error as a fraction of the number of values, so the value returned for a percentage `p` is a value of the field whose rank is within `rankError` times the number of numeric values of the rank `ceil(p * n)` that the exact rule would pick; `p01` and `p99` can differ from the exact values by that many ranks, and the extremes of the field are always in `min` and `max`. A field that holds integers keeps integer quantiles; if a `float` arrives after the cap, the summary keeps its order and the quantiles are doubles, as they are in the exact case. `min`, `max`, `mean` and `stddev` do not depend on the cap.
- **`temporal`** is for `date`, `datetime` and `time` fields; it is `null` for other types, for a field whose date values share no order, and for a constant field whose statistics are withheld.
  - `orders` is the list of date orders that every value of the field allows, from `DMY`, `MDY` and `YMD`, **sorted ordinally** (so `["DMY", "MDY"]`, never `["MDY", "DMY"]`); it is `[]` for a `time` field. `ambiguous` is true when more than one order is left.
  - `clock` is `24h` or `12h` over the values that carry a time of day, and `null` for a `date` field or when 24-hour and 12-hour values are mixed.
  - `min`, `max` and `quantiles` (the same keys as `numeric`) are ISO 8601: `yyyy-MM-dd` for `date`, `yyyy-MM-ddTHH:mm:ss` for `datetime` and `HH:mm:ss` for `time`, with a fraction of a second (`.25`) only when it is not zero. A 12-hour value is read on a 24-hour day, and a zone offset is ignored: the statistics are over the wall-clock fields as written. When `ambiguous` is true, `min`, `max` and `quantiles` are absent, and so is the field's `monotonic`, because each depends on an assumed order. Past `limits.quantilePerField` values the quantiles come from a Greenwald-Khanna summary of the values read under the field's order (two summaries are kept while the day-month-year and month-day-year readings both fit, and the one of the order that survives is written): `approximate` is `true` and `rankError` is `0.001` (`limits.sketches.rankError` for a table of more than 547 fields), with the same meaning as for `numeric`, over the number of date or time values. `approximate` is left out while they are exact (and when `quantiles` is absent).
  - When the values share no order (`3/17/2026` and `17/3/2026` in one field), `type` is `string`, `temporal` and `monotonic` are `null`, `typeVotes` and `dominant` are unchanged, and the field is listed in the evidence entry `type` / `dateOrderConflict`.
- **`statsWithheld`** is `"constant"` when the field is `constant`, has `numeric` or `temporal` statistics to give, and values are not opted in for it (`--include-values`, or the field's name in `--include-values-for`); `numeric` and `temporal` are then `null`. `length`, `shapes`, `flags` and `distinct` are kept. It is `null` otherwise.
- **`length`** is `{ min, max, mean }` over the trimmed non-null values, in UTF-16 code units; it is `null` for a field with no non-null value.
- **`shapes`** is the list of shape masks of the trimmed non-null values with their counts, sorted by count descending, then by mask (ordinal); it is `[]` when there is no non-null value. A mask maps each UTF-16 code unit to a class: `9` for `0`-`9`, `A` for `A`-`Z`, `a` for `a`-`z`; ASCII punctuation and space are kept literally, except `{` and `}`; every other character, including every non-ASCII letter and each half of a surrogate pair, is `?`. A run of two or more of the same symbol is written `c{n}`, and a mask of more than 64 tokens is cut after 64 and ends with `+`. The braces themselves are never literal: `{` and `}` are `?`, so `1{2}` is `9?9?` and cannot be read as the mask `9{9}` of nine digits. The first 64 distinct masks in file order get a slot (`limits.shapesPerField`); every value with another mask goes into one final entry `{ "mask": "other", "count": n }`, written after the sorted masks. The classes are ASCII ranges, so the masks do not depend on the runtime. `hints.fileNameShape` uses the same rules.
- **`monotonic`** is one of `increasing` (strictly), `decreasing`, `nonDecreasing`, `nonIncreasing` or `none`, over the non-null values in file order. It is for numeric and temporal fields with at least two non-null values and is `null` otherwise, and for an ambiguous or conflicting date field. A run of equal values is `nonDecreasing`.
- **`constant`** is true when every non-null value of the field is equal. For an `int`, `float`, `date`, `datetime` or `time` field the values are compared as parsed: `1`, `1.0` and `01` are equal, `0`, `-0` and `0.0` are equal, a float equals an integer only when it is exactly that integer, and two dates are equal when their parsed parts are (so `6/5/2026` and `06/05/2026` are equal in an ambiguous field too, and a date equals a datetime at midnight). For any other field, including one whose type ended as `string`, they are compared as trimmed text (`TRUE` and `true` differ). `distinct` and `unique` always compare the trimmed text, so a constant field can have a `distinct` value above 1. A sparse field with one value and many nulls is constant, and so is a field whose values are all null (it has one value, null). It is false for a field with no values at all, and false once two values differ.
- **`unique`** is true when every non-null value is distinct, false once a value repeats and for a field with no non-null value, and `null` when more distinct values than `limits.distinctPerField` came before any repeat (the count is then not exact).
- `name` is the trimmed header text; it is `null` when the table has no header (later slices refer to the field as `#<index>`), and `""` for a blank header name (with the issue `emptyHeaderName`).
- `rawName` is the header text of the field exactly as in the file: the cell with its quotes removed per the dialect (a doubled quote inside a quoted cell is one quote) and nothing else changed, so leading and trailing spaces are kept (`name` is the same text trimmed). Two things are not kept: a line break inside a quoted cell is written as LF (a CRLF becomes LF), and white space outside the quotes of a quoted cell (after the closing quote) is dropped. It is the text of the header line only: a units row, preamble or annotation row never reaches it. In a file with several blocks it is the first block's header; later blocks are matched to it by trimmed text, so a later block whose raw text differs (padding, say) is not reported. It is written for every field of a table with a header, also when it equals `name`, so a consumer that matches exact header text reads one member and needs no fallback; it is `null` when the table has no header (`name` is `null` too), and `""` for a blank header cell. Two names that trim to the same name (`Val(V)` and `Val(V) `) keep their own `rawName`. A header with an undecodable byte holds U+FFFD in `rawName` as in `name`, where the byte was; the bytes themselves are in `nameHex` (see below). `rawName` is not used by the fingerprints, whose `names` projection still uses the trimmed, case-folded `name`. Like `name`, it is always included (see [Values tier](#values-tier)).
- `nameParts` splits the trimmed `name` into `base`, `unit` and `convention` (see [`layout.headerConvention`](#layoutheaderconvention)). `unit` is the unit text as written in the name (`degC`, `dBm`), with no case change, or `null` when the name carries none. `unknownUnit` is `true` when `unit` is not in the unit table, as for `Date(date)` or `RowID(id)`, and `false` otherwise. It is written for every field of a table with a header. It is `null` when the table has no header (`name` is `null` too), so a headerless table has no convention. For `unitsRow` the member `unit` is `null`: the text of the units row is a value, so it lives only in `fields[].unit` (values tier), while the convention is always shown. A trailing space in the header is trimmed before parsing, so `RowID(id) ` and `RowID(id)` give the same `nameParts`; a near miss such as `RowID(ID)` or `rowid(id)` keeps its own text in `base` and `unit`, and is flagged the same way.
- `nameHex` (present only when the name contains U+FFFD) is the hex (lower case) of the original bytes of the field as written, trimmed, quotes included. For an unquoted cell it covers the trimmed field, so the spaces around the name are read from `rawName` beside it. For a quoted cell it spans the quotes and any padding inside them, so it alone holds the exact bytes.
- `unit` is the unit from a units row. **Values tier.** Units parsed from header names are in `nameParts` and are always included. With a units row, a field whose name carries no unit has the convention `unitsRow` when its units cell has a value, and `none` otherwise; `unit` is absent either way unless values are opted in.
- `top` (`{ "items": [{ value, count }] }`, with `"approximate": true` when approximate, plus `maxError` when approximate) and `text` (`{ min, max }`, lexical) are **values tier**. The `{ items, approximate, maxError? }` shape of `top` is the pinned draft shape; sq-12 records it in the schema changelog.
  - `top.items` lists at most 10 values of any type, as the trimmed text of the file (not normalized: `1` and `1.0` are two values), ordered by `count` descending, then `value` ordinally. A field with no non-null value has `items: []`.
  - Counts are exact while the field has at most `limits.distinctPerField` distinct values (`approximate` is left out). Past that cap the counts come from Space-Saving with 1,000 counters (`approximate: true`): a count is never below the true count and exceeds it by at most `maxError`, the largest overcount among the listed items (never more than the number of values divided by 1,000; it is 0 when every listed counter is still exact). A value that is not listed has a count no larger than the count of the last listed item. Ties are always settled by ordinal value, so the output is deterministic.
  - `text.min` and `text.max` are the ordinal (UTF-16 code unit) minimum and maximum of the trimmed non-null values of a field whose `type` is `string`; `text` is `null` for every other type.

Distinct counting, `unique` and the text comparison of `constant` compare a 64-bit xxHash of each trimmed value (see [design.md](design.md#field-profiling)), so two different values are taken for one with probability 2^-64.

## `format.dialect` by family

| Family | Dialect keys |
|---|---|
| delimited | `delimiter`, `quote`, `hasHeader`, `unitsRow`, `decimalSeparator`, `padding {trim, paddedShare, constantLineLength, lineLength}`, `blocks` |
| touchstone | `version`, `ports`, `frequencyUnit`, `parameter`, `dataFormat`, `referenceOhms`, `twoPortOrder`, `matrixFormat`, `keywords[]`, `valuesPerLine {n: count}`, `noise` |
| keyValue | `separator` (`=` or `:`), `hasSections` |
| stdf | `cpuType`, `endianness`, `version` |

### delimited

```json
{ "delimiter": ",", "quote": "\"", "hasHeader": true, "unitsRow": false, "decimalSeparator": ".",
  "padding": { "trim": true, "paddedShare": 0.9726027397260274, "constantLineLength": true, "lineLength": 74 },
  "blocks": 1 }
```

- `delimiter` is `,`, a tab, `;`, `|`, or `whitespace` for runs of white space (the characters of the null-vocabulary trim set, leading and trailing white space trimmed). It is the word `none` for a one-column table, where no delimiter occurs (see the evidence entry `dialect` / `shape`); a consumer that re-reads the file treats `none` as "one field per record". The member is always a string.
- `quote` is always `"` for a delimited file: it is the quote character that the tokenizer honors, whether or not the file has a quoted field (the delimiter evidence counts the quoted fields). Quoting is RFC 4180: a quote opens a field only at the start of a field, `""` inside a quoted field is an escape, and line breaks are allowed inside quotes. A closing quote followed by spaces or tabs and then the delimiter, or the end of the line, closes the field. A lone `"` inside a quoted field that is followed by neither `"`, nor (after optional spaces or tabs) the delimiter or the end of the line is literal text. A quoted field longer than `limits.runawayQuote` characters disqualifies that delimiter.
- `hasHeader` is true when a header was chosen (see the `header` evidence below); `unitsRow` is true when the record after the header is a units row: every non-null field fails its column's type, is at most 16 characters and sits in a column that is not `string`, and at least one field is non-null. Blank cells and null-vocabulary tokens are neutral.
- `decimalSeparator` is `,` only when the delimiter is not `,` and comma-decimal cells (`^[+-]?[0-9]+,[0-9]+$`) outnumber dot-decimal cells (`^[+-]?[0-9]+\.[0-9]+$`) in the header sample; otherwise `.`.
- `padding` describes the data run (from its first to its last record). `paddedShare` is the share of cells that are non-empty after trimming and start or end with a trim-set character; `trim` is true when any cell is padded; `constantLineLength` is true when every non-blank physical line of the run has the same byte length (never when the table has no data record), and `lineLength` is then that length **in bytes** (`null` otherwise). Unlike `layout.maxLineLength`, which counts characters, `padding.lineLength` counts the bytes of the file's own charset, line break excluded.

- `blocks` is the number of blocks: 1 for a table without repeated blocks, and the number of `block` regions otherwise (see [Several blocks](#several-blocks)).

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
{ "version": 2, "structure": "sha256:…", "layout": "sha256:…", "typed": "sha256:…", "names": "sha256:…" }
```

`version` is 2 (it was 1 until v0.2.0; see the versioning policy). It changes whenever a projection changes, and hashes of different versions cannot be compared. Each hash is `sha256:` and 64 lower-case hex digits: the SHA-256 of the UTF-8 bytes of a compact JSON projection (no white space, members in the order below, no BOM). The projections hold no data values, so the hashes are the same with and without `--include-values`, and for two files that differ only in their values.

**Projection text.** The projection is written by its own minimal writer, not by the descriptor writer: `"` and `\` are escaped, a character below U+0020 is written as `\u00xx` with lower-case hex, a lone surrogate is written as `\ufffd`, and every other character is written as it is. No Unicode table is consulted, so a hash is identical across runtimes.

**Case folding** in every projection is ASCII only: `A` to `Z` become `a` to `z`, and every other character (`É`, `İ`) is kept as it is. The folding never uses the runtime's Unicode tables or the current culture.

| Hash | Projection |
|---|---|
| `structure` | `{"family", <family members>, "charsetFamily", "regions"}`. No names |
| `layout` | `{"family", <family members>, "charsetFamily", "regions", "headerConvention", "fields"}` |
| `typed` | The `layout` members, then the family's typed members, then `"types"` |
| `names` | A JSON array of names |

- `family` is `format.family`.
- `charsetFamily` is `"latin1"` for the charset `latin-1`, `"unicode"` for every other charset (`ascii`, `utf-8`, `utf-16le`, and so on), and `null` when `encoding.charset` is `null`.
- `regions` is the array of the `layout.regions[].kind` values, in file order (for a file with several blocks, the first block's regions and the footer: see below). Line numbers, byte ranges and indexes are not part of it.
- `headerConvention` is `layout.headerConvention.dominant`. When the object is absent (the file has no header, or is not a table) the projection writes JSON `null`. The convention `none` is the string `"none"`, so an absent convention and `none` hash differently. Header names that carry indexes, such as `Trace[0]` to `Trace[n]`, are not collapsed: files with a different number of indexed columns have different `layout`, `typed` and `names` hashes.
- `fields` has one entry per `table.fields[]`, in column order: the name, case-folded, or `#<index>` (the decimal column index) when the field is unnamed (no header, or an empty name) or its name is excluded (below). It is `[]` when there is no table. The placeholder `#<index>` can collide with a real name: an unnamed field at index `i` and a header named `#i` at index `i` have the same `layout` and `typed`, and differ only in `names`, which lists the real name and omits the unnamed field. This is accepted.
- `types` has the `fields[].type` of each field, in column order.
- `names` is the sorted (ordinal), de-duplicated, case-folded field names, plus the names that a family adds. Unnamed and excluded fields are left out, not replaced by a placeholder, so a reordered column set has the same `names`.

**Delimited family members** (from `DelimitedFingerprint`, through `IFingerprintContributor`):

| Projection | Members, after `family` |
|---|---|
| `structure` | `"dialect": {"delimiter", "quote", "hasHeader", "unitsRow", "blocks" (only when true), "decimalSeparator", "constantLineLength"}`, then `"preambleKind"` |
| `layout`, `typed` | `"dialect": {"delimiter", "quote", "hasHeader", "unitsRow", "blocks" (only when true)}`, then `"preambleKeys"` |

`delimiter` is `format.dialect.delimiter` (`","`, a tab, `";"`, `"|"`, `"whitespace"` or `"none"` for a one-column table), `quote` is the quote character or `null`, `blocks` is the JSON `true` when `format.dialect.blocks` is greater than 1 and is left out otherwise (the number of blocks is never part of a projection: 3 blocks and 5 give the same four hashes), and `constantLineLength` is `format.dialect.padding.constantLineLength`. `preambleKind` is the most common line kind of the preamble region (`lineKinds`, not counting `blank`); a tie goes to the kind whose name sorts first, and it is `null` when there is no preamble or it has no line of another kind. `preambleKeys` is the preamble region's `keys`, in file order, case-folded. For a file with more than one block, the regions are read as the first block sees them: `regions` lists the kinds of the first block's child regions in place of all the `block` regions, followed by the regions after the last block (the footer), and `preambleKind` and `preambleKeys` are the first block's.

**Names that are left out.** A header name or a preamble key that holds a control character (U+0000 to U+001F, U+007F to U+009F) or U+FFFD is excluded: a field name is projected as `#<index>` and left out of `names`, and a preamble key is left out of `preambleKeys`. Such bytes may differ from file to file and would make every sibling file unique. The issue `nameExcludedFromFingerprint` counts them (see [`issues[]`](#issues)). The names themselves stay in `table.fields[].name`, `layout.regions[].keys` and `hints`.

Example: the file `a,b` / `1,2` / `3,4` has the `layout` projection

```text
{"family":"delimited","dialect":{"delimiter":",","quote":"\"","hasHeader":true,"unitsRow":false},"preambleKeys":[],"charsetFamily":"unicode","regions":["header","data"],"headerConvention":"none","fields":["a","b"]}
```

The projections are defined in [design.md](design.md#structural-fingerprint). Fingerprint equality is not a family test.

**Layout-only profile.** The `structure`, `layout` and `names` projections read no field type, so a layout-only profile writes the same three hashes as a full profile of the same bytes. The `typed` projection holds the type of every field, so it has no value there: `fingerprint.typed` is `null` (`layoutOnly` is `true`), and the projection is not built. `fingerprint.version` is not affected: it is 2 for both, because no projection changes (see [Layout-only profile](#layout-only-profile)).

## `hints`

```json
{
  "extension": ".csv",
  "magic": null,
  "stableLines": [{ "line": 0, "regex": "^\\s*Lot\\s*[=:]" }],
  "headerFields": ["Site", "Vth", "Idsat"],
  "headerPrefix": ["Site", "Vth", "Idsat"],
  "nonNullFields": [0, 1],
  "fieldCount": { "min": 3, "max": 3 },
  "dataStartLine": 8,
  "delimiter": ",",
  "headerConvention": "nameParenUnit",
  "stdf": null,
  "fileNameShape": "a{5}_9{3}.a{3}"
}
```

Every member is always written, as `null` when it does not apply (`stableLines` is `[]`). In a layout-only profile (`layoutOnly` true) the whole `hints` member is `null` (`nonNullFields` needs the per-field counts, and the stable lines are not read; see [Layout-only profile](#layout-only-profile)). A hint is a neutral fact that a consumer can turn into a match rule; the vocabulary mirrors DataBall's conditions (`extension`, `headerExact`, `headerContainsAll`, `lineMatches`, `columnCount`).

| Member | Meaning |
|---|---|
| `extension` | The file extension with its dot, ASCII-lower-cased (only `A` to `Z` change), or `null` when the name has none |
| `magic` | `{ "name": "gzip", "offset": 0, "hex": "1f8b" }`, or `null` when no binary magic matched. `offset` is the 0-based byte offset where the bytes lie and `hex` is those bytes in lower-case hex: the first start-anchored byte string of the matched magic. A magic that also needs bytes at the end of the file (Parquet) lists only the start-anchored part |
| `stableLines` | See below |
| `headerFields` | The header names, trimmed, in column order (`table.fields[].name`); `null` when the file has no header or is not a table. Always included |
| `headerPrefix` | The first 5 header names, or all of them when there are fewer; `null` when `headerFields` is |
| `nonNullFields` | The indexes of the fields that have no null value outside the all-null rows (`fields[].nullsExcludingAllNullRows` is 0) and that have at least one value (`fields[].count` is greater than `fields[].nulls`), ascending. A field that is null only in separator rows (see [`table.rowClasses`](#tablerowclasses)) is listed; a table whose every row is a separator lists nothing; `[]` when none; `null` when the file is not a table. An observation about this file, not a requirement of the type |
| `fieldCount` | `{ min, max }`, the same as `table.fieldCount`; `null` for a file that is not a table |
| `dataStartLine` | The 1-based line where the data region starts; `null` when there is none. Note that `stableLines[].line` is 0-based |
| `delimiter` | `format.dialect.delimiter` of a delimited table (`none` for a one-column table), else `null` |
| `headerConvention` | `layout.headerConvention.dominant`, or `null` (sq-14) |
| `stdf` | `{ cpuType, version }`; `null` until sq-10 |
| `fileNameShape` | The shape mask of `file.name`, by the rules of [`fields[].shapes`](#tablefields) (ASCII classes only; `{` and `}` are `?`) |

**`stableLines`.** Each entry is `{ "line", "regex" }`: `line` is 0-based, matching DataBall's `lineMatches`, and `regex` is anchored. Entries are ordered by `line` ascending, then by `regex`, ordinally. A delimited table gives these, for lines that are one physical line each:

| Line | Regex | Tier |
|---|---|---|
| The header line | `^<escaped line>$`, whatever the line length: the physical line as written, quotes and padding included | always |
| The header line, as a prefix | `^` then the first 5 names (all when there are fewer), each as ` *"?<escaped name>"? *` (the quote only when the dialect has a quote character) and joined by the escaped delimiter; for the `whitespace` delimiter, `^\s*`, the names as `"?<name>"?` joined by `\s+`, and `\s*` | always |
| A preamble line of kind `keyValue` | `^\s*<escaped key>\s*[=:]`; the key is the normalized key of `layout.regions[].keys` (the value is never part of it) | always |
| A preamble line of kind `comment` or `text` | `^<escaped line>$` | values tier: only with `--include-values` |

- A regex escapes only the characters `\ . ^ $ | ? * + ( ) [ ] { }`, each with a backslash. Besides escaped literals it uses only `^`, `$`, `\s`, `*`, `+`, `?` and the class `[=:]`, so it compiles under both .NET (the default options) and ECMAScript (`RegexOptions.ECMAScript`). `\s` is the one construct whose meaning differs: the default matches all Unicode white space, and the ECMAScript option only ASCII white space. Real JavaScript's `\s` is Unicode and broader than .NET's ECMAScript option, so a regex kept here still matches there; the two-engine filter is conservative and may drop a regex that JavaScript would match.
- A regex is emitted only when it matches its own source line under both the .NET default and `RegexOptions.ECMAScript`. A key that was trimmed of a non-ASCII space (`\s` does not match it under the ECMAScript option), or a quoted key, may not match; its line then has no entry.
- A record that spans more than one physical line (a quoted field with a line break) has no entry, and neither has a `keyDelimValue` line.
- A regex whose text contains U+FFFD or a control character other than tab (U+0000 to U+0008, U+000A to U+001F, U+007F to U+009F) is left out: a stray byte may differ from file to file, and the line gets no entry. `headerFields` and `headerPrefix` still list the names as they are.
- Header names and preamble keys are always included (see [Values tier](#values-tier)), so the header regexes and key regexes always are; the values of the key lines never are.

## `issues[]`

Issues are sorted by `code`, ordinally. Each has a `count` and, for some, locations. The issues written by the byte pass and the generic text profile are:

| Code | When | `count` | Locations |
|---|---|---|---|
| `invalidUtf8` | The charset is `ascii` or `utf-8` and the file holds invalid UTF-8 sequences (within the tolerance). Not written for `latin-1`, where nothing is lost; `encoding.invalidBytes` still counts them. | Invalid sequences | `firstLines`, `offsets` and `regions`, one entry per invalid sequence for the first 10, ascending: see [Locating invalid bytes](#locating-invalid-bytes). The charset label was kept: `encoding.invalidBytesTolerated` is `true` |
| `invalidUnicode` | The charset is `utf-16le`, `utf-16be`, `utf-32le` or `utf-32be` and the file holds invalid units (see [`encoding`](#encoding)). There is no tolerance: every invalid unit is counted, however few. `encoding.invalidBytes` holds their bytes. | Invalid units | `firstLines`, `offsets` (BOM included) and `regions`, one entry per invalid unit for the first 10, ascending: see [Locating invalid bytes](#locating-invalid-bytes) |
| `mixedLineEndings` | More than one of CRLF, lone LF and lone CR occurs. | Line endings that are not the dominant kind (most frequent; a tie goes to CRLF, then LF, then CR) | `firstLines`: the first 10 lines, ascending, that end with a non-dominant kind |
| `nameExcludedFromFingerprint` | A header name or a preamble key holds a control character (U+0000 to U+001F, U+007F to U+009F) or U+FFFD, so the fingerprint projections leave it out (see [`fingerprint`](#fingerprint)). Written by every family that has such names. | Names and keys left out | none |
| `includeValuesForUnmatched` | A name in `options.includeValuesFor` matches no preamble key, annotation label, key=value key (a section's) or field name, under ASCII case folding. Written by every family, whether or not `includeValues` is set. | Unmatched names, without duplicates | `names`: the unmatched names as given on the command line, sorted ordinally. They come from the command line, not from the data, so they are not values tier |

The delimited profiler writes these:

| Code | When | `count` | Locations |
|---|---|---|---|
| `raggedRows` | A record in the data run (after the header and units row) has a field count other than k. It is counted in `table.rows` and `table.fieldCount` but not typed. | Ragged records | `firstLines`: the first 10 lines, ascending |
| `commentInData` | A comment line (`#`, `!` or `//` after trimming) lies inside the data run, before or after the header. It is neither a header candidate, an annotation row nor a data row, and not ragged; its text never reaches the descriptor. | Comment lines | `firstLines`: the first 10 lines, ascending |
| `duplicateHeader` | Two header names are equal under ASCII case folding (only possible through the relaxed header pass). | Names that repeat an earlier name | `firstLines`: the header line |
| `emptyHeaderName` | A header name is blank (relaxed header pass). | Blank names | `firstLines`: the header line |
| `heterogeneousBlocks` | A block's header differs from the first block's: from that block on, blocks are not profiled (see [Several blocks](#several-blocks)). The header text is not written: read the file at the listed lines. | Blocks not profiled | `firstLines`: the header lines of the first 10 blocks not profiled, ascending |
| `noDataRows` | The table has no data record: a header (or a header and a units row) and nothing after it. `table.rows` is 0, every field is typed `empty`, and the layout has no `data` region. This is the only way a consumer can tell a header-only file from an empty sweep that has rows. | 1 | `firstLines`: the header line |
| `blocksTruncated` | The file has more candidates for blocks than `limits.regions` allows blocks (see [Several blocks](#several-blocks)): from the first candidate after the last block recognised, nothing is profiled. | Candidates left | `firstLines`: the header lines of the first 10 candidates left, ascending |
| `tooIrregular` | The delimited profiler declined the file: no candidate delimiter is viable and at least one was disqualified (a runaway quote, or more than `limits.fieldCountRuns` field-count runs). The generic text profile describes the file. | Disqualified candidates | `firstLines`: the lines where they were disqualified, ascending, at most 10 |

### Locating invalid bytes

The issues `invalidUtf8` and `invalidUnicode` (sq-46) say where the invalid bytes are, so that a consumer can accept a stray byte in a preamble and refuse one in the data, and name the line in its message, without reading the file again:

```json
{ "code": "invalidUtf8", "count": 2, "firstLines": [2, 9], "offsets": [19, 163], "regions": ["preamble", "data"] }
```

- `offsets`, `firstLines` and `regions` have the same length (at most 10, the first invalid sequences or units in file order) and line up one to one: entry *i* of each describes the *i*-th invalid sequence. `count` is still the number of all of them, so `count` above the length of the lists means that the rest are not listed. The writer fails if the three lengths differ.
- `offsets` are 0-based byte offsets from the start of the file (BOM included) where the invalid sequence or unit starts.
- `firstLines` are the 1-based physical lines that those offsets lie on, numbered as `layout.regions[].lines` are (a CRLF, a lone LF and a lone CR each end one line). Unlike the `firstLines` of the other issues they are not de-duplicated: two invalid bytes on one line give the line twice, so that the three lists stay aligned. A quoted field that spans lines counts physical lines, as everywhere in the layout.
- `regions` names the layout region that holds each offset: the `kind` of the `layout.regions[]` entry whose `bytes` range holds it, so one of `preamble`, `annotation`, `header`, `units`, `data`, `footer` or `unknown`. `unknown` is what a file that is not a table (the generic text profile) has: its one region covers every byte. In a file with more than one block, the name is the kind of the block's child region that holds the byte (`preamble`, `header` and so on of the block), never `block`; the block's index is not part of it (a consumer that needs it finds the block by `offsets` in `layout.regions[]`). The value `other` is a byte that no region holds: a layout with no region, or a gap between regions. The layouts squalify writes tile the file, so `other` is a safety value that a descriptor does not normally contain; it is a member of the closed set of names, and a consumer treats a name it does not know like `other`.
- For a profile of a prefix (`scope`), the offsets, the lines and the regions are those of the prefix, which is the start of the file.
- The tolerance is not changed by this: `invalidUtf8` is written only while the charset is kept (`encoding.invalidBytesTolerated` is then `true`), and a file with more invalid sequences than the tolerance is `latin-1` and has no issue, so the lists have at most 8 entries for UTF-8. `invalidUnicode` has no tolerance and lists the first 10.
- Locating the lines costs one more read of the source, up to the last listed offset, and only for a file that has invalid bytes. Nothing is repaired or escaped: the bytes are not copied into the descriptor, and no value is written.

## `evidence[]`

```json
{ "stage": "dialect", "topic": "delimiter", "chosen": ",", "candidates": [
  { "value": ",", "score": 1, "reason": "k=3; 10000/10000 records; run lines 8-10007" },
  { "value": ";", "score": 0, "reason": "k=1; no run" } ] }
```

**Order of entries.** Entries are sorted by stage in pipeline order (`encoding`, `format`, `dialect`, `header`, `unit`, `type`, then the stages that later slices add), and within a stage by `topic`, ordinally. A stage that is not in the list ranks after the known ones, so every new stage must be added to `Profiler.StageOrder` and listed here.

**Order of `candidates`.** The order is never insertion order. Each topic documents its own key, and a topic with a fixed set of candidates uses a fixed table. A later slice documents its topics here when it adds them.

| Stage / topic | Candidates | Order |
|---|---|---|
| `encoding` / `class` | `binary`, `empty`, `text`; the chosen one scores 1, the others 0 | `value`, ordinally |
| `encoding` / `charset` (text files only) | Rule 3 or 4: only the charset that the rule implies. Rule 6: `ascii`, `latin-1`, `utf-8`; the chosen one scores 1, the others 0. The reason of the chosen candidate carries the invalid-sequence count and the tolerance when they matter | `value`, ordinally |
| `format` / `family` | Every registered profiler: its family as `value`, its score and its reason. `chosen` is the family with the highest score, a tie going to the earlier family; for a zero-length file it is `empty`, which no profiler claims, and every candidate scores 0 | Registry order: `stdf`, `touchstone`, `keyValue`, `delimited`, `text`, `binary` (only registered profilers appear; at present `delimited`, `text` and `binary`) |

| `dialect` / `delimiter` (delimited text files, when the profiler wins, or when it declined a file as too irregular) | The five delimiter candidates as `value`: `,`, a tab, `;`, `|`, `whitespace`. `score` is the candidate's dialect score (0 for a disqualified one); `chosen` is the winner, `none` for a one-column table, and `null` when none is viable. The reason is `k={k}; {inRun}/{total} records; clean={c}; run lines {a}-{b}`, with `; {n} non-k records skipped in the start window` appended when the start window of the data run skipped any records with another field count (ragged records and comment lines), `; {n} quoted fields` always appended and `; {n} literal quotes` appended when the candidate saw any; `k={k}; no run` (with the same quoted-fields and literal-quotes suffixes); `disqualified: runaway quote opened at line {n}`; or `disqualified: too irregular, more than {limit} field-count runs by line {n}` | Candidate order: `,`, tab, `;`, `|`, `whitespace` |
| `dialect` / `shape` (delimited; only for a header-only or one-column table) | Both shapes: `headerOnly` and `singleColumn` as `value`. The one taken scores 1, with the reason `header only; k={k}; header on line {n}; no data record` or `single column; {n} records; the header on line {m} fails the column's type`; the other scores 0 with `declined: a line has several fields` or `declined: no line has several fields`. `chosen` is the shape taken. These two tables are claimed only when no delimiter candidate is viable (see the dialect rules in the design) | `value`, ordinally |
| `dialect` / `blocks` (delimited, only when the file has more than one block) | One candidate per block, `value` = the block index as a decimal string, `score` 1 when the block is profiled and 0 when it is not (see [Several blocks](#several-blocks)), and the reason `header line {n}; the first block`, `header line {n}; the same header as block 0`, `header line {n}; another header than block 0: not profiled` or `header line {n}; the same header as block 0, but a block before it has another header: not profiled`; then one entry with `value` `summary` and score 0: `candidates={c}; blocks={b}; profiled={p}`, where c counts the runs that were looked at as candidates, those that are no block included, and p the blocks that are profiled. At most the first 10 blocks are listed. `chosen` is the number of blocks | Blocks by index, ascending (numerically), the first 10 only; `summary` last |
| `dialect` / `decimalSeparator` (delimited) | `,` and `.`; the chosen one scores 1. The reasons carry the counts of comma-decimal and dot-decimal cells in the header sample | `value`, ordinally |
| `header` / `candidates` (delimited) | Every eligible record of the header sample, with `value` = its 1-based line and `score` = its header score; the chosen record too when it is not eligible (relaxed or assumed); then one entry with `value` `summary` and score 0. The reason of a record is `eligible; nonEmpty=..; nonNumeric=..; unique=..; contrast=..`, `relaxed: blank or repeated names; ...` or `assumed: all-string table; ...`. The reason of the summary counts only: `sampled={n}; eligible={e}; ineligible={i} (comment line={a}, field count={b}, blank or repeated names={c}, fits every typed column={d})`. `chosen` is the header line, `null` when there is no header | Records by line, ascending (numerically); `summary` last |
| `unit` / `headerConvention` (delimited tables with a header) | The five conventions as `value`, always all five: `nameParenUnit`, `nameBracketUnit`, `nameUnderscoreUnit`, `unitsRow`, `none`. `score` is the share of header names with that convention (count over number of names); `chosen` is the `dominant` convention. The reason is `{n} of {m} headers` | Fixed order as listed, which is also the tie-break order of the dominant convention |
| `type` / `dateOrderConflict` (delimited) | One candidate per field that joined to `string` because its date values share no order: `value` is the field index as a decimal string, `score` is 0, and the reason is `string: no date order fits every value; {n} date values; DMY {a}, MDY {b}, YMD {c}`, where {a}, {b} and {c} count the date values that allow each order. `chosen` is `string`. The entry is written only when such a field exists | Fields by index, ascending (numerically) |

Reasons are fixed templates filled with value-free numbers (counts and offsets), never data values.

In a layout-only profile (`layoutOnly` true) no entry of the stage `type` is written, so `type` / `dateOrderConflict` is never there; every other entry is the one of a full profile.

## Values tier

| Member | Tier |
|---|---|
| Header names (`fields[].name` and `fields[].rawName`), preamble keys, annotation labels, section names and keys, STDF test names | always |
| Numeric and temporal statistics, counts, types, shapes, lengths | always, except statistics of constant fields |
| `nameParts` and `headerConvention` | always |
| Region `values` (preamble and annotation), key=value values, comment text | values tier |
| `numeric` and `temporal` of constant fields | values tier |
| `fields[].top`, `fields[].text`, `fields[].unit` (units row) | values tier |
| `stableLines` for comment and text lines | values tier |
| `fingerprint` (the projections hold no values), `hints.stableLines` for the header and for `keyValue` keys | always |

Normative rules:

- **One list.** `ValuesPolicy` (`src/squalify/ValuesPolicy.cs`) is the single list of value-bearing members: `layout.regions[].values` (preamble, owner: the key), `layout.regions[].rows[].values` (annotation cells, owner: the label), `sections[].values` (owner: the key), `table.fields[].unit`, `top`, `text`, the `numeric` and `temporal` members of a field whose `constant` is `true` (owner: the field name), and `hints.stableLines` entries built from a comment or text line (no owner). Comment text joins the list when its member joins the model. Every such model member carries a `[ValueBearing]` mark and a test compares the marks with the list.
- **`--include-values`** opts in every member. **`--include-values-for <name>`** (repeatable; `options.includeValuesFor`, a sorted set: ordinal order, exact duplicates dropped, names that differ in case kept) opts in the members whose owner has that name: a preamble key, an annotation label, a key=value key or a field name. The name is compared whole and with ASCII case folding (`A` to `Z` only); there is no prefix or substring match and no Unicode folding. A name that matches several members, for example a preamble key and a field, opens all of them. A stable-line regex of a comment or text line has no owner, so only `--include-values` opens it. A name that matches nothing gives the issue [`includeValuesForUnmatched`](#issues).
- **Aligned lists.** The entry for an owner that is not opted in is `null`; an annotation row's `values` is absent unless its label is opted in.
- **Never held, never written.** A profiler asks the policy before it collects a value, so a value that may not be written is not kept in memory, and `DescriptorWriter` checks every descriptor against the policy before it writes. A value-bearing member whose owner is not opted in makes the writer throw `InvalidOperationException`, with the member's path and no value in the message. That is a bug in the producer, not a user error, and it is not wrapped in `SqualifyException`.

## Consumer subset

A downstream consumer (prism) matches delimited lab exports against its own declared file types, and reads a small subset of this descriptor to do it. squalify only describes: it does not classify, and it adds no member for one consumer. The owner decided (2026-10-27) that this document is the contract and the consumer adapts: where the meaning of a member differs from the consumer's own, the difference is listed here with the formula that gets the consumer's view, and where no formula exists the consumer keeps its own check of the file.

`ConsumerSubsetTests` (`tests/squalify.Tests/`) pins every member and every formula below on synthetic fixtures (`fixtures/consumer/`), and fails if this section stops listing a member.

### Preconditions

- Everything below is for `format.family` `delimited`. For any other family `table` is absent. A file with a header and no data row is `delimited` with `table.rows` 0 and the issue `noDataRows`, when its one multi-field line is the last line, lies within the first `limits.headerSample` lines, is preceded only by blank, comment or `key=value` lines, and reads like names (every cell filled, distinct, not a number, at most 64 characters and a single word, unless quoted: `Time,Vcc(V),Icc` is a table, `Test Name,Result` and `Hello, world` are text). Such a file has `format.dialect.padding.constantLineLength` false, `decimalSeparator` `.` as a default (no value was seen), and the same `layout` fingerprint as a sibling that has rows, but not the same `structure` fingerprint. A line of prose with a comma stays `text`, and so does a one-column file without a clear header. A one-column table has `format.dialect.delimiter` `none` and a header that its values fail (a text header over numbers, say).
- Every count is over the whole file, unless the descriptor has a `scope` member (a byte limit was given). squalify then counts the prefix `scope.bytesRead` and says so: a consumer that compares against its own prefix-based description checks that `scope` is absent or `scope.complete` is true before it treats the counts as whole-file counts (end of file reached, every byte of `file.size` examined).
- Pin `schemaVersion` (major), `limits.nullTokens` and `limits.unitTable`: the last two version the tables that decide every null and every header unit (see [`limits`](#limits)). A change to the null vocabulary or the unit table bumps only `limits.nullTokens` or `limits.unitTable`, not `schemaVersion`, so a consumer pins the table versions alongside `schemaVersion`.
- **Ragged records near the start.** The run-start window skips up to two records that have another field count, whatever their position (an earlier rule that needed three k-field records first is gone), so a ragged record among the first two data records is reported in `raggedRows` and the header is kept. It skips ragged records with two or more fields, comment lines, and a one-field line once the header and three rows have come before it (a stray row, reported as ragged). A one-field line earlier than that (a `key=value` line, a title) ends the window, so it stays preamble. Records after the last full-width row are the footer and never count against the window. A table whose header row is followed by three or more ragged lines can still lose its header.
- **Known limitation, k-field preamble absorbed.** A preamble of three or more lines with the header's field count, followed by at most two lines with two or more fields of another count (or comment lines), is absorbed into the data run as `annotation` rows instead of `preamble`; the header is still detected. The same happens to a k-field title line followed by one such line. A one-field line after fewer than four k-field lines keeps them preamble.
- **Known limitation, header-less comma files.** A header-less comma CSV with a multi-word first column and an integer last column can be read as whitespace-delimited (reported by the sq-04 architect probe; no test pins it). A file with a header is unaffected.
- With no header, `table.fields[].name`, `rawName` and `nameParts` are `null`, and the other field members are written as usual.
- **Repeated blocks.** When `format.dialect.blocks` is greater than 1, every member below describes the blocks that are profiled (those with the first block's header, up to the first with another one; see [Several blocks](#several-blocks)). With the issue `heterogeneousBlocks`, the records of the other blocks are in no member below. A consumer that expects one table per file rejects a file with more than one block.

### Members and their stability

Stability is read under the [versioning policy](#versioning-policy): adding an optional key is a minor change, and removing, renaming or changing the meaning of a key is a major one.

- **S** (stable): the meaning is fixed by this section. Changing it is a major change.
- **E** (closed set): the member is stable, but its set of values can grow. Adding a value is a minor change, and a consumer treats a value it does not know as unknown rather than failing. This rule for enumeration values is pending sq-12, which adds it to the versioning policy; until then it is a commitment of this section. The values today are listed in the description.
- **T** (versioned table): the value depends on a fixed built-in table (`limits.nullTokens` or `limits.unitTable`). A change to the null vocabulary or the unit table bumps only `limits.nullTokens` or `limits.unitTable`, not `schemaVersion`. A consumer pins those table versions alongside `schemaVersion`, and a change in either is a change to re-check against this section.

Until sq-12 freezes v1 this holds as a commitment, not as a published schema: members may still change without a version bump (see the status line), and a change to any member below must change this section and `ConsumerSubsetTests` in the same commit.

| Member | Stab. | Meaning for a consumer |
|---|---|---|
| `schemaVersion` | S | `"1.0"`. |
| `limits.nullTokens` | S | Version of the null vocabulary (1 today). |
| `limits.unitTable` | S | Version of the unit table (1 today). |
| `limits.nullSpellings.max` | S | The most distinct spellings kept in `table.fields[].nullSpellings` (per field) and in `table.rowClasses.allNullRowSpellings` (per table): 64 today. Always written. A consumer reads the bound here and does not hard-code it; a spelling past it is counted in the `Other` member. |
| `limits.nullSpellings.maxChars` | S | The longest spelling kept in those maps, in characters: 256 today. Always written. A longer spelling is counted in the `Other` member. |
| `limits.nullSpellings.maxDistinctOther` | S | The most distinct dropped spellings that the `OtherDistinct` members count exactly: 64 today. Always written. A consumer reads the cap here and does not hard-code it; one more distinct spelling sets the `Capped` marker. |
| `file.name` | S | The file name, never a path. |
| `format.family` | E | `delimited` for the files below. |
| `format.dialect.delimiter` | E | `,`, a tab, `;`, `\|`, or the word `whitespace` or `none` (a one-column table). |
| `format.dialect.quote` | S | Always `"` for a delimited file, whether or not a field is quoted. |
| `format.dialect.hasHeader` | S | True when a header row was chosen. |
| `format.dialect.blocks` | S | The number of blocks: 1 for a table with no repeated header-and-table blocks. Above 1, `table` and every member below describe the profiled blocks only (see [Several blocks](#several-blocks)). |
| `encoding.bom` | E | `utf-8`, `utf-16le`, `utf-16be`, `utf-32le`, `utf-32be` or `null`. |
| `encoding.lineEndings.crlf` | S | CRLF pairs, over the whole decoded text. |
| `encoding.lineEndings.lf` | S | Lone LF. |
| `encoding.lineEndings.cr` | S | Lone CR. A consumer whose parser fails on a lone CR rejects the file when it is not 0. |
| `encoding.invalidBytes` | S | Bytes in invalid UTF-8 sequences (UTF-8, ASCII and latin-1 files) or in invalid UTF-16 or UTF-32 units. Always written; 0 when there are none. |
| `encoding.invalidBytesTolerated` | S | `true` when the charset (`ascii` or `utf-8`) was kept although the file holds invalid UTF-8 sequences (within the tolerance of `limits.charsetTolerance`): the label does not mean that every byte is valid. Absent, not `false`, when there is none: absent means `false`. A consumer that refuses stray bytes in some regions reads it with `issues[]` `invalidUtf8`. |
| `table.rows` | S | Data records after the header and any units row, **ragged ones included**. Comment lines, annotation rows and the header are not data records. |
| `table.rowClasses.allNullRows` | S, T | Records in which every field is null **by the null vocabulary**. |
| `table.rowClasses.truncated` | S | Records of classes past the cap of 64. |
| `table.rowClasses.classes[].count` | S | Records in one class. |
| `table.fields[].index` | S | 0-based column index. |
| `table.fields[].name` | S | The header text, trimmed; `""` for a blank name; `null` with no header. |
| `table.fields[].rawName` | S | The header text exactly as in the file: quotes removed per the dialect, nothing else changed (spaces kept); `null` with no header. Written for every field with a header, also when equal to `name`. `Val(V)` and `Val(V) ` are two `rawName`s with one `name`. |
| `table.fields[].nameParts.base` | S | The name without its unit (the whole name when there is none). |
| `table.fields[].nameParts.unit` | S | The unit text as written, or `null`. |
| `table.fields[].nameParts.convention` | E | `nameParenUnit`, `nameBracketUnit`, `nameUnderscoreUnit`, `unitsRow` or `none`. |
| `table.fields[].nameParts.unknownUnit` | S, T | True when `unit` is not in the unit table. |
| `table.fields[].type` | E | The strict join of the value types: `empty`, `bool`, `int`, `float`, `date`, `datetime`, `time` or `string`. |
| `table.fields[].typeVotes` | S | Non-null values by type. |
| `table.fields[].count` | S | Non-null plus null values of the records that have the header's field count (ragged records are not counted). |
| `table.fields[].nulls` | S, T | Null values, **all-null records included**. |
| `table.fields[].nullsExcludingAllNullRows` | S, T | Null values outside all-null records. |
| `table.fields[].nullTokens` | S, T | Null values by null-vocabulary entry: `""`, `NA`, `NA.`, `N/A`, `null`, `NaN`, `-`. All-null records are counted; the counts add up to `nulls`. |
| `table.rowClasses.allNullRowSpellings` | S, T | The all-null records whose every cell has the same exact spelling, by that spelling (`""` for blank cells; not trimmed, quotes removed). Always written. |
| `table.rowClasses.allNullRowSpellingsMixed` | S, T | All-null records whose cells do not share one spelling. Always written. |
| `table.rowClasses.allNullRowSpellingsOther` | S, T | All-null records of one spelling that is not a key of `allNullRowSpellings` (more than `limits.nullSpellings.max` spellings, or one longer than `limits.nullSpellings.maxChars` characters). Always written. `sum(allNullRowSpellings) + allNullRowSpellingsMixed + allNullRowSpellingsOther == allNullRows`. |
| `table.rowClasses.allNullRowSpellingsOtherDistinct` | S, T | The number of distinct spellings among the `allNullRowSpellingsOther` records (each once however often it recurs; exact text; exact up to `limits.nullSpellings.maxDistinctOther` and up to hash collisions). Always written; 0 when `allNullRowSpellingsOther` is 0; at most `allNullRowSpellingsOther`, and at least 1 when that is positive. |
| `table.rowClasses.allNullRowSpellingsOtherDistinctCapped` | S, T | `true` when more than `limits.nullSpellings.maxDistinctOther` distinct spellings were seen, so the count above is a lower bound. Written only when true; absent means `false`. |
| `table.fields[].nullSpellings` | S, T | Null values by exact spelling (not trimmed, quotes removed) over all data records, all-null records included; absent means `{}`. With `nullSpellingsOther` it adds up to `nulls` (see [F9](#derivation-formulas)). |
| `table.fields[].nullSpellingsOther` | S, T | Null values whose spelling is not a key of `nullSpellings` (more than `limits.nullSpellings.max` spellings, or one longer than `limits.nullSpellings.maxChars` characters); absent means 0. |
| `table.fields[].nullSpellingsOtherDistinct` | S, T | The number of distinct spellings among the `nullSpellingsOther` values (each once however often it recurs; exact text; exact up to `limits.nullSpellings.maxDistinctOther` and up to hash collisions); absent means 0 (left out when `nullSpellingsOther` is 0); at most `nullSpellingsOther`, and at least 1 when that is positive. `keys(nullSpellings) + nullSpellingsOtherDistinct == 1` (with the marker below not true) says the field has a single spelling. |
| `table.fields[].nullSpellingsOtherDistinctCapped` | S, T | `true` when more than `limits.nullSpellings.maxDistinctOther` distinct spellings were seen, so the count above is a lower bound. Written only when true; absent means `false`. |
| `table.fields[].constant` | S, T | Every non-null value is equal (see [How `constant` treats nulls](#how-constant-treats-nulls)). |
| `table.fields[].statsWithheld` | E | `"constant"` or `null`. |
| `issues[].code`, `issues[].count` | E, S | `raggedRows` counts the records that have another field count than the header's, in the profiled blocks: the preamble records between two blocks are not ragged rows, and the records of a block that is not profiled (`heterogeneousBlocks`, `blocksTruncated`) are not counted at all. `noDataRows` (count 1) says the table has a header and no data record: `table.rows` is 0 and every field is `empty`. |
| `issues[].firstLines`, `issues[].offsets` | S | For `invalidUtf8` and `invalidUnicode` only (other issues have `firstLines` alone, or no location): the 1-based line and the 0-based byte offset of each of the first 10 invalid sequences or units, in file order, one line per offset, so a line repeats when two offsets lie on it (see [Locating invalid bytes](#locating-invalid-bytes)). A consumer that refuses invalid bytes names the first line in its message. |
| `issues[].regions` | E | For `invalidUtf8` and `invalidUnicode`: the layout region each offset falls in, one name per offset in the same order: `preamble`, `annotation`, `header`, `units`, `data`, `footer`, `unknown` (a file that is not a table) or `other` (no region holds it). The closed set can grow: a consumer treats a name it does not know like `other`. A consumer that accepts stray bytes in the preamble and refuses them in `header` and `data` reads this member; in a file with several blocks the name is the block's own region, and a consumer that expects one table per file rejects the file anyway. |

A field that holds only nulls is typed `empty`, and every member above is written for it, except `nullSpellings`, `nullSpellingsOther`, `nullSpellingsOtherDistinct` and `nullSpellingsOtherDistinctCapped`, which are absent when empty, 0 or false.

### Differences from the consumer's own view

Each row is a difference, and the formulas that follow are the consumer's way to its own view. Where a row says "no formula", the consumer keeps its own check of the file.

| Topic | squalify | Consumer's own view | How the consumer gets there |
|---|---|---|---|
| `nullTokens` keys | Entries of the null vocabulary, matched ASCII case-insensitively after trimming: `NULL`, `Null` and `null` all count under `null`. Counts include all-null records. | Per column, five case-sensitive spellings (`NULL`, the empty field, `null`, `NA`, `N/A`), counted over data rows only. Its null count, its all-null rows and its constant-ignoring-nulls flag follow the single file-level token `NULL`. | [F5](#derivation-formulas) covers the per-column counts and the single token. `nullTokens` cannot tell `NULL` from `null` from `Null`, but [`nullSpellings`](#tablefields) (sq-36) can, and `allNullRowSpellings` gives the separator rows by spelling: [F9](#derivation-formulas) is the formula for a consumer that treats only some exact spellings as null. |
| `NaN` | A vocabulary entry, so a `NaN` value, in any ASCII case (`nan`, `NAN`), is a null, counted under the key `NaN` (all-null records included), and it does not vote for a type. | A number in a `(unit)` column, for the exact spelling `NaN` only. | `nullTokens["NaN"]` is the count of `NaN` values in the field in any case, so it is an upper bound on the consumer's `NaN` numbers. The other non-finite spellings (`-NaN`, `+NaN`, `Infinity`, `-Infinity`, `+Infinity`, `inf`) are not in the vocabulary: they vote `string`, so a `(unit)` column holding them has `type` `string`. See [F8](#derivation-formulas). |
| Big integers | An integer that fits in 64 bits is `int`; a longer one, up to 309 digits, is `float`; one of more than 309 digits is `string`. | `Int64`. | A `(id)` column is an `Int64` column only when `type` is `int`. |
| `constant` | Compared as parsed values for `int`, `float`, `date`, `datetime` and `time` fields (`1`, `1.0` and `01` are equal), and as trimmed text otherwise. Nulls are ignored, and a field of only nulls is constant. | The same text in every data row, `NULL` counting as a value. | [F3](#derivation-formulas) for the NULL-as-a-value view (a necessary condition), [F7](#derivation-formulas) for the text. |
| `allNullRows` | Every field null by the vocabulary (blank, `NULL`, `NA`, `N/A`, `NaN`, `-` and the rest). | Every field the token `NULL`. | [F5](#derivation-formulas). |
| Names | Trimmed, and split by the DataBall grammar below. | The exact header text, and its own split. | [F6](#derivation-formulas) for the convention; `rawName` gives the exact header text of each field, so exact-name checks need no re-parse of the header. |
| Ragged records | Counted in `table.rows`, in no row class, and in no field's `count` or `nulls`. | A record of another width fails the parse. | [F2](#derivation-formulas): `issues[]` `raggedRows` is the count, and a consumer that does not accept ragged records rejects the file when it is not 0. |
| Types | A vote from the values. | A type from the name convention. | [Types](#types-and-the-name-convention). |
| Scope | The whole file, or the prefix named by `scope` when a byte limit was given. | A bounded prefix. | None needed without `scope`; with it, a prefix against a prefix is the same view. |

**Header grammar.** Names follow the [conventions of `layout.headerConvention`](#layoutheaderconvention), which mirror DataBall's `HeaderParser`, and they are trimmed before parsing. For a consumer that reads the header as written, these are the differences:

| Header cell | squalify `base`, `unit`, `convention` |
|---|---|
| `RowID( id )` | `RowID`, `id`, `nameParenUnit` (the unit is trimmed) |
| `RowID(id) ` | `RowID`, `id`, `nameParenUnit`, and `name` is `RowID(id)` (the trailing space is trimmed in `name` and kept in `rawName`) |
| `X( )` | `X( )`, `null`, `none` (blank unit text is no unit) |
| `A(b(c))` | `A(b`, `c)`, `nameParenUnit` (the last `(` opens the unit) |
| `X(1)` | `X`, `1`, `nameParenUnit`, `unknownUnit` true (digit-only text is still a paren unit) |
| `Pout(dBm)[1]`, `Trace[0]` | the whole name, `null`, `none` (an indexed trace column carries no unit) |
| `RowID(ID)` | `RowID`, `ID`, `nameParenUnit`: the unit text keeps its case, so `id` is matched ordinally |

### Types and the name convention

`type` is a vote from the values, not a type derived from the name. A consumer that types by name (a `(id)` column as an integer, a `(unit)` column as a number, any other column as boolean or string) uses `type` as a check:

| Consumer's column | `type` that fits | Notes |
|---|---|---|
| `(id)`, integer | `int` (or `empty` when every value is null) | `float` and `string` mean a value does not fit 64 bits or is not a number. squalify's `int` is wider than digits only: it also accepts a sign, surrounding spaces (`flags.padded`) and leading zeros (`flags.leadingZero`; `flags` absent reads as all counts 0), so a consumer that wants digits only keeps its own check. |
| `(unit)`, number | `int`, `float` or `empty` | `NaN` is a null here. `Infinity` and `-Infinity` vote `string`; `typeVotes` says how many ([F8](#derivation-formulas)). `empty` means only that every value is null by the vocabulary: a column of `NA` is `empty` to squalify, and every value of it is a misfit to a consumer that reads only `NULL` as null. Other null spellings, padded values, signs and comma decimals are numbers or nulls to squalify, so `type` alone does not say the consumer accepts the column (see F8). |
| Other, boolean or string | `bool` is a boolean; every other type is a string to the consumer | A column of `1` and `2` is `int`, which the consumer reads as a string column. squalify's `bool` is wider than `True` and `False`: it also takes `yes` and `no`, in any ASCII case, so a consumer that accepts exactly `True` and `False` keeps its own check. |

### How `constant` treats nulls

`constant` is true when every **non-null** value of the field is equal. Nulls are not values: a field with one value and many nulls is constant, a field whose values are all null is constant, and the all-null records (separator rows) neither make a field vary nor count as a value. The member never says whether the field has nulls. A consumer whose rule is "the same text in every data row, `NULL` included" gets a necessary condition for it from `constant`, the null counts and `count`, over its own data rows:

```
ConstantWithNullAsValue = constant && (nullsExcludingAllNullRows == 0
                                       || nullsExcludingAllNullRows == count - allNullRows)
```

The field is then one value with no null in a data row, or null in every data row. A field that is null in some data rows and has one value in the others is constant here and **not** constant with null as a value.

The formula is a necessary condition, not the consumer's rule. If it is false, the consumer's rule is false. If it is true, it is exact only in a NULL-only file ([F5](#derivation-formulas)) whose equal values are also equal texts (`1`, `1.0` and `01` are one parsed value). Otherwise the consumer keeps its own check of the file. For example, in a file that also uses `NA`, a record of `NA` in every field is an all-null record to squalify and a data row with the text `NA` to the consumer: a field that is `x` in every other record has the formula true and the consumer's rule false.

When `DataRows == 0` (every record is all-null), the consumer sets both of its constants false, while squalify's `constant` can be true (a field of only nulls is constant). The consumer keeps its own rule there.

The shorter form `constant && (nulls == 0 || nulls == count)` is wrong when the table has separator rows, because `nulls` includes them.

### Derivation formulas

`allNullRows`, `rows`, `count` and the rest are the members above.

- **F1. Data rows.** `DataRows = table.rows - table.rowClasses.allNullRows`. Ragged records are data rows here, and for every field `nulls - nullsExcludingAllNullRows == allNullRows`.
- **F2. Row accounting.** `table.rows == sum(table.rowClasses.classes[].count) + table.rowClasses.truncated + ragged`, where `ragged` is the `count` of the `issues[]` entry with `code` `raggedRows`, or 0 when there is none. Field `count` is `table.rows - ragged`.
- **F3. Constant, NULL counted as a value (necessary condition).** The formula in [How `constant` treats nulls](#how-constant-treats-nulls): false implies the consumer's rule is false, and true is exact only in a NULL-only file whose equal values are equal texts.
- **F4. Constant, nulls ignored.** `constant` itself.
- **F5. A NULL-only file.** Call a table NULL-only when every key of every field's `nullTokens` is `null`. Then every null in the file is a spelling of `NULL`, and for a consumer that treats only `NULL` as null (its file-level token): its all-null rows are `allNullRows`; its null count of a field over the data rows is `nullsExcludingAllNullRows`; and its count of the spelling `NULL` in a field is `nullTokens["null"] - allNullRows` (the key read as 0 when absent), modulo case, since `NULL`, `Null` and `null` are one key. Its counts of the other four spellings (the empty field, `null`, `NA`, `N/A`) are 0, except that `nullTokens` cannot split `NULL` from `null` (`nullSpellings` can; see F9). `nulls == 0` is a sufficient never-null test for every spelling in any file. `nullsExcludingAllNullRows == 0` is not: a record of `NA` in every field is all-null to squalify and a data row with `NA` tokens to the consumer. When the file is not NULL-only, squalify's nulls are wider than the consumer's, and the consumer keeps its own count.
- **F6. Id, Unit or None.** `Id` is `nameParts.convention == "nameParenUnit"` with `nameParts.unit == "id"` (ordinal); `Unit` is any other `nameParenUnit`; `None` is every other convention, including `nameBracketUnit`, `nameUnderscoreUnit` and `unitsRow`.
- **F7. Text constant.** `constant == false` implies the values are not the same text, because different parsed values are different texts. `constant == true` does not imply the same text: `1`, `1.0` and `01` are constant. The consumer keeps its own exact-text check.
- **F8. Non-numeric votes.** In a NULL-only file ([F5](#derivation-formulas)) with `flags.padded == 0` for the field (`flags` absent reads as all counts 0), `sum(typeVotes) - typeVotes.int - typeVotes.float` is the number of non-null values that are not numbers, and 0 means the `(unit)` column has no value the consumer fails for being non-numeric. A positive number is an upper bound on the consumer's misfits, because a consumer that accepts `Infinity` and `-Infinity` counts those as numbers where squalify votes them `string`. Differences of syntax remain even then: squalify's number votes include signed values (`+3`) and, in a file whose decimal separator is a comma, comma decimals (`1,5`), which a stricter consumer rejects. Outside a NULL-only file, or with padded values, the votes say nothing about the consumer's misfits (another null spelling is a null, not a vote, and a padded value is a number) and the consumer keeps its own misfit count.
- **F9. Exact spellings.** A consumer has two sets of exact strings: its null tokens `T`, and its separator tokens `S`, a subset of `T`. A separator row of the consumer is a record in which **every cell is the same token** `s` in `S` (a consumer that counts `NULL` and the empty field as null but treats only an all-`NULL` record as a separator has `T = { NULL, "" }` and `S = { NULL }`; a consumer whose separators are its null tokens has `S = T`). With `A[x]` for `table.rowClasses.allNullRowSpellings[x]` and `N[x]` for `fields[].nullSpellings[x]` (a missing key is 0):
  - its separator rows are the sum of `A[s]` for `s` in `S`;
  - its null count of a field for a token `t` in `T`, outside its separator rows, is `N[t] - A[t]` when `t` is in `S` and `N[t]` when it is not, because each record of uniform spelling `s` has exactly one cell `s` in every field, so the subtraction removes exactly the cells of its separators;
  - its null count of the field outside its separator rows is the sum of that expression for `t` in `T`.

  These are **exact** when all of the following hold:
  - no bound was hit: `allNullRowSpellingsOther` is 0 (for the first), and the field's `nullSpellingsOther` is 0 (for the second and third);
  - every token of `T` is a spelling that the null vocabulary matches (the trimmed, ASCII case-insensitive vocabulary: `NULL`, `null`, `NA`, `N/A`, `NA.`, `NaN`, `-` and the empty field, in any case or padding). A token the vocabulary does not know (for example `None`) is text to squalify, so it is in no spelling map and the formulas give 0 for it;
  - there is no `raggedRows` issue, or the consumer also drops ragged records: a ragged record is in no spelling map and in no `allNullRows`, so the formulas do not see it;
  - the file is read as profiled: with the issue `heterogeneousBlocks` or `blocksTruncated`, the records of the blocks that are not profiled are in no member, so the formulas are over the profiled blocks only.

  **Distinct dropped spellings (sq-43).** A consumer that must know whether a field (or the all-null rows) has **a single spelling** reads it as `keys(nullSpellings) + nullSpellingsOtherDistinct == 1` for a field, and `keys(allNullRowSpellings) + allNullRowSpellingsOtherDistinct == 1` for the uniform all-null rows (a missing member is 0), provided the matching `Capped` marker is not true. The test cannot be fooled by the cap: with the marker true the count is `limits.nullSpellings.maxDistinctOther` (64) and more than 64 distinct spellings were seen, so the sum is at least 64 and never 1. Without a dropped spelling the test is the number of keys; with only one long spelling dropped (a 300-character padded blank, 1,000 times) it is 0 keys plus 1. The distinct count is also an invariant bound: `0 <= nullSpellingsOtherDistinct <= nullSpellingsOther`, and it is at least 1 whenever `nullSpellingsOther` is positive (the same for the all-null rows); it does not change the sums above. It is exact up to 64-bit hash collisions of two different dropped spellings (about 2^-64 per pair), which would make it one too low and so could turn "two spellings" into "one"; a consumer that cannot accept that risk keeps its own check of the file.

  When an `Other` count is not 0 the sums are lower bounds, because the uncounted records or cells may be in `T` or `S`. A mixed all-null record (`allNullRowSpellingsMixed`, for example `NULL,NA,NULL`) is not a separator of the consumer, so its cells stay in the field counts, which is what the subtraction gives. The keys are the text as read. A consumer that trims before it compares can trim the keys of `nullSpellings` itself (a field count is a sum over keys), but it cannot do that for separators: the record `NULL, NULL ,NULL` is mixed to squalify and `allNullRowSpellingsMixed` counts it, though it is a uniform separator to a trimming consumer, so for such a consumer the separator formulas are lower bounds and it keeps its own check. Likewise `NULL` does not match the key ` NULL `. The test `F9_ExactSpellings_MatchABruteForceCountOfAConsumersOwnSeparatorsAndNulls` checks the formulas against a brute-force count for several `T` and `S`.

**Layout-only descriptors.** With `layoutOnly` `true` (see [Layout-only profile](#layout-only-profile)) the subset members that come from the header, the rows and the file are written as for a full profile: `schemaVersion`, the `limits` members, `file.name`, `format.*`, `encoding.*`, `table.rows`, `table.rowClasses.*` (including the all-null spellings), `table.fields[].index`, `name`, `rawName` and `nameParts.*`, and `issues[]`. The members that need a field's values are absent: `table.fields[].type`, `typeVotes`, `count`, `nulls`, `nullsExcludingAllNullRows`, `nullTokens`, `nullSpellings`, `nullSpellingsOther`, `nullSpellingsOtherDistinct`, `nullSpellingsOtherDistinctCapped`, `constant` and `statsWithheld`. So the formulas that read them (F3 to F5 and F7 to F9, the type table and the field `count` of F2) need a full profile; F1, the row accounting of F2 and F6 hold, and the separator rows of F9 are still `allNullRowSpellings`.

Not in the subset: statistics (`numeric`, `distinct`, `top` and the like), `encoding.charset`, `layout`, `kind`, `fingerprint`, `hints`, `evidence`.

## Versioning policy

- `schemaVersion` is `major.minor`. Adding optional keys bumps the minor version. Removing, renaming or changing the meaning of a key bumps the major version.
- Consumers must ignore unknown keys within a major version.
- `fingerprint.version` changes whenever a projection changes. Hashes from different versions cannot be compared.
- sq-12 makes this policy normative. Until then the schema is a draft: the members that sq-04 adds (`limits.runawayQuote`, `limits.fieldCountRuns`, the delimited dialect and regions, the delimited issues and the `dialect` and `header` evidence stages), the members that sq-17 adds (`table.rowClasses` and its classes) and the members that sq-14 adds (`fields[].nameParts` with `unknownUnit`, `layout.headerConvention`, `hints.headerConvention` and the `unit` evidence stage) do not bump `schemaVersion`, which stays `1.0`. The same holds for the members that sq-34 adds or changes: the issue `noDataRows`, the evidence entry `dialect` / `shape`, and the value `none` of `format.dialect.delimiter` (a one-column table); this is a new value of a closed set, not a new key. The same holds for the members that sq-06 adds or changes: the `fingerprint` projections (`fingerprint.version` is now 1), the structured `hints.magic` (it was a string), the other `hints` members and the issue `nameExcludedFromFingerprint`. The members that sq-07 adds or fills do not bump it either: `fields[].top` and `fields[].text` (now computed, behind the values tier), the issue `includeValuesForUnmatched` and `issues[].names`. sq-31 removes the Parquet-only members before the freeze, again without a bump: `fields[].sourceType`, `hints.parquetColumns`, the `parquet` dialect (with `kvKeys`) and the `parquet` family. Parquet is not in v1; adding it later would be an additive change. sq-11 replaces the `exceeds` placeholders with sketches, again without a bump: `distinct` is always a number with `approximate`, `numeric.quantiles` and `numeric.approximate` are never `null`, `temporal` gains `approximate`, `numeric` and `temporal` gain `rankError` (only when approximate), `distinct` gains `relativeStdError` (only when approximate), `limits.budget` gains `shapeChars`, `limits` gains `shapeCharsPerField`, and `distinct.exceeds`, `numeric.exceeds` and `temporal.exceeds` are removed. sq-34 adds, again without a bump: the issue `noDataRows`, the delimiter word `none`, the header-only and one-column table shapes and the `dialect`/`shape` evidence topic. sq-35 adds, again without a bump: the top-level `scope` member (written only when a byte limit is given) and the omission of `file.sha256` when only a prefix was read; a descriptor without a limit is unchanged. The members that sq-13 adds do not bump it either: the `block` regions with `index` and `regions` (the unused `label` is removed), `format.dialect.blocks` above 1, the issues `heterogeneousBlocks` and `blocksTruncated`, and the evidence entry `dialect` / `blocks`. sq-13 does bump `fingerprint.version`, from 1 to 2 (owner decision 2026-10-27: one bump covers every v0.2.0 slice that moves hashes, done in whichever lands last before the release): a file with more than one block gets `blocks` in the dialect of the `structure` and `layout` projections and is read as its first block sees it. The hashes of a file with one block are the same as under version 1, but only hashes of one version are comparable. sq-24 adds, again without a bump: the issue `invalidUnicode` (invalid UTF-16 and UTF-32 units, with `offsets`), and `encoding.invalidBytes` now counts invalid UTF-16 and UTF-32 bytes; no key is added. sq-32 changes the layout and the field profiles before the freeze, again without a bump: an object or array of at most 120 characters on one line is written on one line (descriptor-wide), and in `table.fields[]` a null member is left out (except `name`, `nameParts`, `nameParts.unit`, `nullTokens`, `constant` and `statsWithheld`), `flags` is left out when all three counts are 0, and `approximate` is left out when false; no member that the consumer subset lists is removed, and none of the fingerprint projections changes. sq-37 adds, again without a bump: `table.fields[].rawName` (the header text exactly as in the file, written for every field and `null` with no header); none of the fingerprint projections changes. sq-36 adds, again without a bump: `fields[].nullSpellings` and `fields[].nullSpellingsOther` (both left out when empty or 0) and `table.rowClasses.allNullRowSpellings`, `allNullRowSpellingsMixed` and `allNullRowSpellingsOther` (always written); `nullTokens` and every other member are unchanged, and none of the fingerprint projections changes. sq-38 adds, again without a bump: `limits.sketches` (`hllPrecision`, `rankError`, `budgetBytes`), written for every descriptor with the full-precision values (14, 0.001, 67108864) unless a table of more than 547 fields scaled them, and the effective values of `distinct.relativeStdError`, `numeric.rankError` and `temporal.rankError`, which are no longer always 0.008125 and 0.001 (they are for a table of 547 fields or fewer); the only change to a descriptor of such a table, and to one of a file that is not a table, is the added `limits.sketches` object (owner decision 2026-10-29: always write it, so that a consumer never hard-codes 14 and 0.001), and none of the fingerprint projections changes. sq-42 adds, again without a bump: `limits.nullSpellings` (`max`, `maxChars`), written for every descriptor with a `limits` block with the bounds the profiler enforces (64 and 256; owner decision 2026-10-29: one object, always written, so that a consumer never hard-codes them, and it replaces the sq-36 statement that the two bounds are unpublished); the only change to any descriptor is that added object, and none of the fingerprint projections changes (`limits` enters no projection). sq-43 adds, again without a bump: `limits.nullSpellings.maxDistinctOther` (64), always written inside the existing object (owner decision 2026-10-29: publish the cap, so that a consumer never hard-codes it), `fields[].nullSpellingsOtherDistinct` (left out when 0) with `fields[].nullSpellingsOtherDistinctCapped` (written only when true), and `table.rowClasses.allNullRowSpellingsOtherDistinct` (always written; 0 when nothing was dropped) with `table.rowClasses.allNullRowSpellingsOtherDistinctCapped` (written only when true; the marker rule for the table is an implementer choice that mirrors the field's, not an owner decision); the sums and every other member are unchanged, a descriptor of a table in which no spelling is dropped differs from before only by `limits.nullSpellings.maxDistinctOther` and `allNullRowSpellingsOtherDistinct: 0`, and none of the fingerprint projections changes (`limits` and the spellings enter no projection). sq-39 adds no descriptor member and no issue code, so the schema is untouched: the first-record ceiling (`--max-first-record-bytes`) either fails the run with an error or leaves the descriptor as it would be without it, and it is not written to `options` or `scope`. sq-40 adds no descriptor member either: `Profiler.ProfilePrefix` returns the same descriptor as `Profiler.Profile` for the same source and options (for a source whose `Length` is its true size), together with the prefix bytes (an API addition, not a schema change), and the cut rule it uses is the one in the [user guide](user-guide.md#reading-only-a-prefix), which is now a documented contract. sq-46 adds, again without a bump: `encoding.invalidBytesTolerated` (written only when `true`; absent means `false`) and, in the issues `invalidUtf8` and `invalidUnicode`, `firstLines` and `regions` next to the existing `offsets` (owner decision 2026-10-30: a parallel `regions` array and the optional `invalidBytesTolerated`); the three lists have the same length and line up, and a descriptor of a file without invalid bytes is unchanged. These are optional keys, so under the policy above they are a minor change; they ship in the first schema freeze (sq-12), which carries them. The `invalidUtf8` and `invalidUnicode` issues are the first with both `firstLines` and `offsets`, so a consumer must not assume that an issue has at most one of them. No fingerprint projection changes. sq-47 adds, again without a bump: the top-level `layoutOnly` (written only as `true`, only with the option `--layout-only`), and, in a descriptor that has it, `null` for `hints` and `fingerprint.typed` (members that are never `null` otherwise) and a `table.fields[]` that holds only `index`, `name`, `rawName`, `nameHex` and `nameParts`; a descriptor made without the option is unchanged byte for byte. `layoutOnly` is an optional key, so it is a minor change; the `null` hints and typed fingerprint, and the absent field members, appear only in a descriptor that says `layoutOnly: true`, so a descriptor made without the option never has them, and a reader that may meet a descriptor made by someone else checks `layoutOnly`. The schema that sq-12 publishes carries `layoutOnly` (optional, the constant `true`), the nullable `hints` and `fingerprint.typed`, and a layout-only alternative for `table.fields[]` (the five names members only, with the type and count members that a full field requires left out). `fingerprint.version` stays 2: no projection changes.
