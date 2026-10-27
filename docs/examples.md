# Examples

Task recipes. Every command was run on a committed fixture under `tests/squalify.Tests/fixtures/`, and the output is trimmed (`...` marks a cut). Run them from a clone of the repository, or download the fixture first (see [Getting started](getting-started.md#2-profile-a-file)). Most recipes use [`jq`](https://jqlang.org). New to squalify? Start with [Getting started](getting-started.md).

## A lab export with a preamble and a units row

`tests/squalify.Tests/fixtures/lab/sweep-preamble.csv` has a comment line and `Lot=A12`-style lines on top (7 lines), the header `Freq(MHz),Pout(dBm),Gain(dB)`, a units row `MHz,dBm,dB`, 20 data rows and a 2-line footer.

```bash
squalify profile tests/squalify.Tests/fixtures/lab/sweep-preamble.csv | jq -c '.layout.regions[] | {kind, lines, keys}'
```

```json
{"kind":"preamble","lines":[1,7],"keys":["Lot","Wafer","Temp","Operator"]}
{"kind":"header","lines":[8,8],"keys":null}
{"kind":"units","lines":[9,9],"keys":null}
{"kind":"data","lines":[10,29],"keys":null}
{"kind":"footer","lines":[30,31],"keys":null}
```

One column in full (`.table.fields[1]`, compacted):

```json
{"name":"Pout(dBm)","nameParts":{"base":"Pout","unit":"dBm","convention":"nameParenUnit"},"type":"float","min":10,"max":19.5,"monotonic":"increasing","unique":true}
```

The unit comes from the header (`dBm`), the type is inferred from all 20 rows, and the column is strictly increasing. A loader for this file needs: skip the preamble, read the header on line 8, skip the units row, read lines 10 to 29.

## A padded export with a limits block and a broken row

`tests/squalify.Tests/fixtures/delimited/padded-annotated.csv` has `Key :,value` lines on top, `Upper_limit`, `Lower_limit` and `Unit` rows above the header, `NA.` for missing values, padding to a constant line length, and one row that is missing a comma.

```bash
squalify profile tests/squalify.Tests/fixtures/delimited/padded-annotated.csv \
  | jq -c '{padding: .format.dialect.padding, regions: [.layout.regions[] | [.kind, .lines[0], .lines[1]]], nulls: [.table.fields[] | select(.nulls > 0) | {name, nullTokens}], issues}'
```

```json
{"padding":{"trim":true,"paddedShare":0.9726027397260274,"constantLineLength":true,"lineLength":74},"regions":[["preamble",1,5],["annotation",6,9],["header",10,10],["data",11,20]],"nulls":[{"name":"Gain(dB)","nullTokens":{"NA.":2}}],"issues":[{"code":"raggedRows","count":1,"firstLines":[17]}]}
```

A loader for this file needs: skip the 5 preamble lines and the 4 annotation rows, trim padding, treat `NA.` as null, read data from line 11, and expect line 17 to be short. The annotation rows (`layout.regions[]` of kind `annotation`) describe the limits and units rows: label, type of the cells and null tokens.

## Opt in to values

`--include-values` adds every value. `--include-values-for` opens only the named preamble keys, annotation labels or columns. In `tests/squalify.Tests/fixtures/consumer/lab-sweep.csv` the column `Mode` is `CW` or `OFDM`:

```bash
squalify profile tests/squalify.Tests/fixtures/consumer/lab-sweep.csv --include-values-for Mode \
  | jq -c '.table.fields[] | select(.name == "Mode") | {name, top, text}'
```

```json
{"name":"Mode","top":{"items":[{"value":"OFDM","count":5},{"value":"CW","count":3}],"approximate":false},"text":{"min":"CW","max":"OFDM"}}
```

Without the option the same command prints `{"name":"Mode","top":null,"text":null}`. A name that matches nothing is reported as the issue `includeValuesForUnmatched` (`--include-values-for Nope` gives `[{"code":"includeValuesForUnmatched","count":1,"names":["Nope"]}]`). See [the values policy](user-guide.md#values-policy).

## Check for schema drift

Fingerprints hash a value-free projection, so files with the same structure hash the same whatever their values. Make three files from `lab-sweep.csv`: a copy, a copy with other values, and a copy without the last column:

```bash
F=tests/squalify.Tests/fixtures/consumer/lab-sweep.csv
cp $F monday.csv
sed 's/OFDM/QAM/; s/2400/2450/' $F > tuesday.csv
cut -d, -f1-7 $F > wednesday.csv
for f in monday.csv tuesday.csv wednesday.csv; do echo "$f $(squalify profile "$f" | jq -r .fingerprint.typed)"; done
```

```text
monday.csv sha256:d13dc0540af34c133446bee30c654579ada0531cb7d23c8b0b7d29499fdb29ec
tuesday.csv sha256:d13dc0540af34c133446bee30c654579ada0531cb7d23c8b0b7d29499fdb29ec
wednesday.csv sha256:359399f2904639be117b10f9ad1fbff99edde6c6fd474d236db019bf0b8f1f45
```

`tuesday.csv` has different values and the same hash; `wednesday.csv` lost a column and has a new one. The four hashes answer different questions: `structure` ignores names and column count, `layout` and `typed` include the names (`typed` also the types), and `names` is the sorted name set. Compare hashes only when `fingerprint.version` matches.

## Triage an unknown file

`tests/squalify.Tests/fixtures/binary/sample.gz`, renamed so the name tells nothing. squalify names the magic and does not decompress:

```bash
cp tests/squalify.Tests/fixtures/binary/sample.gz mystery.bin
squalify profile mystery.bin | jq -c '{family: .format.family, magic: .hints.magic}'
```

```json
{"family":"binary","magic":{"name":"gzip","offset":0,"hex":"1f8b"}}
```

A binary file with no known magic has `"magic":null`; `tests/squalify.Tests/fixtures/binary/random.bin` also reports `binary.bytes.entropy` of about 7.95 (random bytes). A zero-length file has family `empty`.

## Gate CI on issues

`jq -e` exits non-zero when its result is `false` or `null`, so it turns an empty `issues` list into a pass:

```bash
squalify profile tests/squalify.Tests/fixtures/lab/sweep-preamble.csv | jq -e '.issues == []' > /dev/null; echo $?
squalify profile tests/squalify.Tests/fixtures/delimited/padded-annotated.csv | jq -e '.issues == []' > /dev/null; echo $?
```

```text
0
1
```

The first file is clean; the second has the `raggedRows` issue. To gate on one code only, use `jq -e '[.issues[].code] | index("raggedRows") | not'`. In a pipeline, set `set -o pipefail` so a squalify error (exit code 1) is not hidden by `jq`.

## Profile a directory

squalify takes one file, and a directory is an error. Loop in the shell and keep one line per file:

```bash
for f in tests/squalify.Tests/fixtures/delimited/*.csv; do
  squalify profile "$f" | jq -r '[.file.name, .format.family, .format.dialect.delimiter, .table.rows, ([.issues[].code] | join(","))] | @tsv'
done
```

```text
leading-separator-rows.csv	delimited	,	140	
padded-annotated-no-units.csv	delimited	,	10	raggedRows
padded-annotated.csv	delimited	,	10	raggedRows
semicolon-decimal-comma.csv	delimited	;	8	
semicolon-preamble.csv	delimited	;	8	
semiconductor-sweep.csv	delimited	,	81	
```

To keep a descriptor next to each file, write `squalify profile "$f" -o "$f.squalify.json"` in the loop. Files that are not delimited have no `table`, so `.table.rows` is `null`.

## Use the library to pick an import route

A small console program that profiles each path it is given and says how to load it. It needs only the `squalor.squalify` package:

```csharp
using squalor.squalify;

var options = ProfileOptions.Create(includeValues: false, includeValuesFor: []);

foreach (var path in args)
{
    try
    {
        var d = Profiler.Profile(path, options);
        var codes = string.Join(",", d.Issues.Select(i => i.Code));
        var route = d.Format.Family switch
        {
            "delimited" when d.Issues.Count == 0 => $"load as a table ({d.Table!.Rows} rows)",
            "delimited" => $"load as a table, but review: {codes}",
            "binary" => $"binary, magic {d.Hints.Magic?.Name ?? "unknown"}",
            _ => $"{d.Format.Family}: no loader",
        };
        Console.WriteLine($"{d.File.Name}: {route}");
    }
    catch (SqualifyException ex)
    {
        Console.Error.WriteLine(ex.Message);
    }
}
```

On the lab export, the padded export, the gzip sample, a plain text file and a missing path:

```text
sweep-preamble.csv: load as a table (20 rows)
padded-annotated.csv: load as a table, but review: raggedRows
sample.gz: binary, magic gzip
plain.txt: text: no loader
Cannot read 'nope.csv': file not found.
```

(The last message names the full path you passed.) The descriptor model is a set of records; the members are listed in [descriptor.md](descriptor.md), and the API in the [user guide](user-guide.md#library).
