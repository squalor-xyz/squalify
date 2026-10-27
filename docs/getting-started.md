# Getting started

Install squalify, profile a file and read the result. About five minutes.

| Page | For |
|---|---|
| [User guide](user-guide.md) | CLI and library reference, formats, values policy, guarantees, troubleshooting |
| [Examples](examples.md) | Task recipes with real output |
| [Descriptor schema](descriptor.md) | Every member of the descriptor |
| [Design](design.md), [How it works](how-it-works.md) | Architecture and diagrams |
| [Contributing](../CONTRIBUTING.md), [Releasing](release.md) | Working on squalify |

The commands below use [`jq`](https://jqlang.org) to pick parts out of the JSON. squalify itself does not need it.

## 1. Install

You need the .NET 10 SDK. Install the CLI as a .NET tool; the command is `squalify`:

```bash
dotnet tool install -g squalor.squalify.Cli
squalify --version
```

```text
0.1.0
```

Your version may be newer. If the shell says `squalify: command not found`, add `~/.dotnet/tools` to your `PATH` and open a new shell. Update later with `dotnet tool update -g squalor.squalify.Cli`.

To pin the version per repository (for CI), use a local tool instead of `-g`. In the repository root:

```bash
dotnet new tool-manifest                      # once; writes .config/dotnet-tools.json
dotnet tool install squalor.squalify.Cli
dotnet tool run squalify --version            # or: dotnet squalify --version
```

CI then runs `dotnet tool restore` before `dotnet tool run squalify profile ...`.

To call squalify from C# instead, add the library to a project: `dotnet add package squalor.squalify` (see [section 6](#6-use-it-from-c)).

## 2. Profile a file

The examples use a committed fixture, `tests/squalify.Tests/fixtures/lab/sweep-preamble.csv`: a lab export with a 7-line preamble, a header, a units row, 20 data rows and a footer. Download it, or clone the repository and use the path:

```bash
curl -fsSLO https://raw.githubusercontent.com/squalor-xyz/squalify/main/tests/squalify.Tests/fixtures/lab/sweep-preamble.csv
squalify profile sweep-preamble.csv -o sweep.json
```

`-o` writes the descriptor to a file; without it the descriptor goes to stdout. The file is about 620 lines of JSON. You do not read all of it; the next section says where to look.

## 3. Read the descriptor

Look at the sections in this order. Each command reads `sweep.json`.

**What is the file?** `format` and `encoding`:

```bash
jq -c '.format | {family, dialect: (.dialect | del(.padding, .blocks))}' sweep.json
```

```json
{"family":"delimited","dialect":{"delimiter":",","quote":"\"","hasHeader":true,"unitsRow":true,"decimalSeparator":"."}}
```

The family is `delimited`, with a comma delimiter, a header row and a units row. `encoding.charset` is `ascii` and `kind.label` is `text`.

**How is it laid out?** `layout.regions` tiles the file with 1-based, inclusive line numbers:

```bash
jq -c '.layout.regions[] | {kind, lines}' sweep.json
```

```json
{"kind":"preamble","lines":[1,7]}
{"kind":"header","lines":[8,8]}
{"kind":"units","lines":[9,9]}
{"kind":"data","lines":[10,29]}
{"kind":"footer","lines":[30,31]}
```

This is what a loader needs: skip 7 preamble lines, read the header on line 8, skip the units row, read data from line 10 to 29, ignore the footer. `hints.dataStartLine` is 10. The preamble region also lists its `keys` (`Lot`, `Wafer`, `Temp`, `Operator`), but not their values (see [section 5](#5-opt-in-to-values)).

**What is in each column?** `table.fields`:

```bash
jq -c '.table.fields[] | {name, type, nulls, min: .numeric.min, max: .numeric.max}' sweep.json
```

```json
{"name":"Freq(MHz)","type":"int","nulls":0,"min":700,"max":2600}
{"name":"Pout(dBm)","type":"float","nulls":0,"min":10,"max":19.5}
{"name":"Gain(dB)","type":"float","nulls":0,"min":15,"max":19.8}
```

Each field also has `nameParts` (the unit read from the header: `Pout`, `dBm`), `nullTokens`, `shapes`, quantiles and more.

**What looks wrong?** `issues` is a list of problems squalify found and described without rejecting the file. Here it is empty (`[]`). A file with a short row has `{"code":"raggedRows","count":1,"firstLines":[17]}`; see [Examples](examples.md#a-padded-export-with-a-limits-block-and-a-broken-row).

**Is it the same kind of file as another?** `fingerprint` hashes the structure without any values:

```bash
jq -c '.fingerprint | {structure, typed}' sweep.json
```

```json
{"structure":"sha256:4bb113e362c81b0f64b1b23f4e9ae219097fd4aebe8f2f592ff8b5947c8e4740","typed":"sha256:9f8de819f1eaca5a6436ec7e9a99f8f52dfb2cc089a841a48fe3812586742e3d"}
```

Two files with the same hashes have the same layout and column types, whatever their values. Compare hashes only when `fingerprint.version` matches.

**Why did it decide that?** Every inference has an entry in `evidence` with the candidates and their scores, for example the delimiter vote (comma scores 0.71, the others 0).

The full descriptor is in [descriptor.md](descriptor.md).

## 4. Check the result in a script

The exit code is 0 on success and 1 on any error, and issues are not errors. To fail a step when a file has issues, see [Gate CI on issues](examples.md#gate-ci-on-issues).

## 5. Opt in to values

By default no data values appear in the descriptor, only names, keys, counts and statistics. `--include-values` adds them all; `--include-values-for <name>` adds the values of the named preamble key, annotation label or column only (repeatable):

```bash
squalify profile sweep-preamble.csv --include-values-for Lot | jq -c '.layout.regions[0] | {keys, values}'
```

```json
{"keys":["Lot","Wafer","Temp","Operator"],"values":["A12",null,null,null]}
```

Only `Lot` is opened; the other entries stay `null`. See [the values policy](user-guide.md#values-policy).

## 6. Use it from C#

```csharp
using squalor.squalify;

var descriptor = Profiler.Profile("sweep-preamble.csv", ProfileOptions.Create(includeValues: false, includeValuesFor: []));

Console.WriteLine($"{descriptor.Format.Family} {descriptor.Fingerprint.Typed} issues={descriptor.Issues.Count}");
byte[] json = DescriptorWriter.ToBytes(descriptor);   // the exact bytes the CLI writes
```

`Profiler.Profile` throws `SqualifyException` when the file cannot be read. The model types (`Descriptor`, `FormatSection`, `TableSection`, ...) are plain records. A worked example is in [Examples](examples.md#use-the-library-to-pick-an-import-route).

## Next steps

- Recipes for drift checks, CI gates and directories: [Examples](examples.md).
- Every option, the formats and what each produces, and troubleshooting: [User guide](user-guide.md).
- What each descriptor member means: [Descriptor schema](descriptor.md).
- Status today: delimited text is fully profiled; other files get the generic text or binary profile, and the schema is a draft until the v1 freeze.
