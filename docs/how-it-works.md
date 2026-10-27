# How squalify works

> **Design stage.** This page describes the agreed design (spec pass, 2026-10-13, amended after the sample-data review the same day). Five slices have landed. sq-01 added the CLI shell, `ToolInfo` and `SqualifyException`. sq-02 added the descriptor model, the canonical JSON writer and the determinism kit (`DetMath`, `XxHash64`, `Ordinal`) and the golden-file test harness. sq-03 added the byte pass, the format registry and the generic text and binary profiles, so `squalify profile` now works on any file. sq-16 added the value classifier (`ValueClassifier`), which classifies one value as empty, true/false, a number, a date or time, or text. sq-04 added the delimited-text profiler: delimiter and quoting, preamble, annotation, header, units, data and footer regions, padding, the decimal separator and per-field type votes. Every source file that does not exist yet is marked *(planned)*. When an architectural review or a slice changes how squalify works, this page and its diagrams are updated in the same change (see "Docs" in `AGENTS.md`).

squalify reads one data file and writes a **descriptor**: a JSON summary of what the file is and what is inside it. Other tools, such as DataBall, read the descriptor to decide how to handle the file. squalify never changes the file and never needs the network.

Each diagram shows one idea. Labels are in plain language, with the code name in small text. Source paths are relative to `src/squalify/`, so the CLI project is written `../squalify.Cli/`.

## 1. The pieces

The `squalify` command is a thin shell around the library. Each kind of file has its own profiler. DuckDB is used only to read Parquet. The descriptor is the only thing that leaves the tool.

```mermaid
flowchart LR
    CLI["squalify command<br/><small>squalify.Cli</small>"] --> Lib
    Host["Your app"] --> Lib
    Lib["Profiler library<br/><small>Profiler.Profile</small>"] --> Profilers["Format profilers<br/><small>delimited, parquet, touchstone,<br/>keyValue, stdf, text, binary</small>"]
    Profilers -. "Parquet only" .-> DuckDB[("DuckDB<br/><small>DuckDB.NET</small>")]
    Lib --> Out["Descriptor JSON"]
    Out --> Consumers["DataBall and other tools"]
```

*Source: `../squalify.Cli/CommandFactory.cs`, `../squalify.Cli/CliApp.cs`, `ToolInfo.cs`, `SqualifyException.cs`, `Descriptor/`, `Json/DescriptorWriter.cs`, `Profiler.cs`, `Formats/FormatRegistry.cs`, `Formats/GenericTextProfiler.cs`, `Formats/GenericBinaryProfiler.cs`, `Delimited/DelimitedProfiler.cs`; the other profilers (Parquet, Touchstone, key=value, STDF) under `Formats/` (planned).* The CLI works today: `squalify --version` prints the tool version, and `squalify profile <file>` writes the descriptor to stdout, or to the `-o` file. The `delimited`, `text` and `binary` profilers exist so far, so every file is described as a delimited table, text, binary or empty; the other profilers in the diagram are planned. A directory is an error.

## 2. One profile run

Every file goes through the same steps. A step that reads the file reads all of it, start to end, and keeps only bounded summaries in memory. The file can be far larger than RAM.

```mermaid
flowchart TD
    A["1. Byte pass: hash, encoding,<br/>text or binary, magic bytes<br/><small>ByteSniffer</small>"] --> B["2. Choose a format<br/><small>FormatRegistry</small>"]
    B --> C["3. Read the structure:<br/>regions, header, records<br/><small>IFormatProfiler</small>"]
    C --> D["4. Profile every field:<br/>types and statistics<br/><small>FieldAccumulator</small>"]
    D --> E["5. Summarize: fingerprints, hints,<br/>unit conventions, kind label<br/><small>Projection, HeaderConventionParser,<br/>KindRules, hints builder</small>"]
    E --> F["6. Write canonical JSON<br/><small>DescriptorWriter</small>"]
```

*Source: `Json/DescriptorWriter.cs`, `Profiler.cs`, `Sniff/ByteSniffer.cs`, `Formats/FormatRegistry.cs`, `Formats/IFormatProfiler.cs`, `Delimited/DelimitedProfiler.cs`; `Fields/`, `Fingerprint/`, `Units/`, `Kind/` (planned).* Steps 3 to 5 are only partly built: the delimited, generic text and generic binary profiles make up step 3 for now, step 4 is limited to the type votes that the delimited profile counts (the other field statistics come later), and the fingerprint and kind label hold fixed placeholder values until their slices land. For delimited text, step 3 is two reads of the file: one while the format is chosen (the structure pass) and one when the delimited profiler has won.

## 3. Choosing a format

Every profiler looks at the file and gives a score from 0 to 1. The highest score wins, and a tie goes to whichever profiler comes first in the registry list. Every score and its reason are kept in the descriptor's evidence, so you can see why a format was chosen. Most profilers score from the byte-pass result alone. A delimited table cannot be recognized that way, so the delimited profiler is handed the file too and reads all of it while it scores (the structure pass of section 4); if it wins, it keeps what it found, so the file is not read again for the same work.

```mermaid
flowchart LR
    S["Byte-pass result and the file<br/><small>SniffResult, IProfileSource</small>"] --> P1["Parquet: magic at both ends → 1.0"]
    S --> P2["STDF: FAR record at start → 1.0"]
    S --> P3["Touchstone: extension + option line → 0.9"]
    S --> P4["Key=value document → up to 0.6"]
    S --> P5["Delimited table: whole-file structure pass → 0.3 + 0.4 × best delimiter score<br/><small>DelimitedProfiler</small>"]
    S --> P6["Plain text / other binary → 0.1–0.2"]
    P1 & P2 & P3 & P4 & P5 & P6 --> W["Highest score wins<br/><small>ties: registry order</small>"]
    W --> Ev["All scores go to evidence"]
```

*Source: `Formats/FormatRegistry.cs`, `Formats/IFormatProfiler.cs`, `Delimited/DelimitedProfiler.cs`, `Formats/GenericTextProfiler.cs`, `Formats/GenericBinaryProfiler.cs`; the other profilers are planned.* A file with no bytes is called empty before any profiler scores it. For files that are not empty, the evidence lists the score of each profiler that is registered, in registry order; today that is delimited, text and binary.

A profiler can also decline a file and say why. When the delimited profiler finds delimiter evidence but gives up, because no delimiter is good enough and at least one candidate was disqualified, it scores 0 and hands back an issue (`tooIrregular`) and its evidence. The registry passes these on even though the profiler lost, so the plain text profile describes the file and the descriptor still explains why it is not treated as a table.

```mermaid
flowchart TD
    D["Delimited profiler scores the file<br/><small>DelimitedProfiler.Score</small>"] --> V{"Is any delimiter good enough?<br/><small>DialectSelector</small>"}
    V -- "yes" --> Keep["Score 0.5 to 0.7, and keeps what it found<br/><small>FormatScore.State</small>"]
    V -- "no, but some were disqualified" --> Dec["Declines with score 0, an issue and evidence<br/><small>FormatScore.NotChosenIssues</small>"]
    V -- "no, none disqualified" --> Plain["Declines with score 0"]
    Dec --> T["Plain text profile wins; the issue is written<br/><small>FormatRegistry.Detect, Profiler</small>"]
    Plain --> T2["Plain text profile wins<br/><small>GenericTextProfiler</small>"]
```

*Source: `Delimited/DelimitedProfiler.cs`, `Delimited/DialectSelector.cs`, `Formats/IFormatProfiler.cs`, `Formats/FormatRegistry.cs`, `Profiler.cs`.*

## 4. Reading a delimited file

A lab CSV is often more than a header and rows. squalify splits the file into regions. It tries every common delimiter at the same time, in one pass over the whole file. It picks the delimiter that gives the longest steady run of rows with the same field count, and whose cells split cleanly. On tables padded with spaces, splitting on spaces looks almost as steady, so cleanliness decides.

```mermaid
flowchart TD
    F["The whole file, decoded once<br/><small>TextScanner</small>"] --> T["One reader per delimiter, side by side:<br/>comma, tab, semicolon, bar, spaces<br/><small>RecordTokenizer</small>"]
    T --> R["Field counts stored as runs<br/><small>FieldCountRle</small>"]
    R --> K["Score each delimiter:<br/>share of rows in the steady run<br/>× share of clean cells<br/><small>DialectSelector</small>"]
    K --> W["Best score wins, if it is good enough:<br/>at least 2 rows (6 for spaces) and a score of 0.5<br/><small>ties: tab beats spaces, then more fields, then fixed order</small>"]
    T -. "quote never closes, or<br/>count changes too often" .-> X["That delimiter is disqualified"]
```

*Source: `Delimited/TextScanner.cs`, `Delimited/RecordTokenizer.cs`, `Delimited/FieldCountRle.cs`, `Delimited/DialectSelector.cs`.* Quotes follow the usual CSV rule (a doubled quote is one quote, and a quoted cell may hold a comma or a line break). A closing quote may be followed by spaces before the comma or the line end, as in padded tables. A stray quote inside a quoted cell, with no comma or line end after it, is kept as plain text, because some instruments write quotes inside a quoted cell without doubling them. A cell counts as clean when it is quoted or contains none of the characters `,` `;` `|` other than its own delimiter, and, for those three delimiters, no tab inside the cell. Spaces never make a cell unclean, and a decimal comma such as `1,5` does not count, so a semicolon file with decimal commas is told apart from a comma file. A delimiter is only chosen when it is good enough: its steady run has at least 2 rows (6 for spaces) and its score is at least 0.5. When tab and spaces tie, tab wins.

The header is not always the first row of that run. Some exports put rows of limits, units or line numbers above the header, lined up with its columns. squalify scores the first rows of the run and picks the one that looks most like names over typed data. The rows above it become **annotation** rows: their labels are kept, their cells are opt-in.

```mermaid
flowchart TD
    S["First 1,000 rows of the steady run<br/><small>DelimitedPass</small>"] --> C["Every row is a header candidate<br/><small>HeaderDetector</small>"]
    C --> E["Only rows with names filled and different, that disagree<br/>with a typed column below, can win<br/><small>HeaderDetector.Evaluate</small>"]
    E --> Sc["Score = filled × not numbers × different<br/>× (0.5 + 0.5 × contrast)<br/><small>HeaderDetector.Evaluate</small>"]
    Sc --> H["Best score wins<br/>a tie goes to the earlier row<br/><small>HeaderDetector.Detect</small>"]
    H --> A["Rows above it:<br/>annotation rows<br/><small>DelimitedPass</small>"]
    H --> U["Row below it is a units row when every non-empty cell<br/>disagrees with its column type<br/><small>empty cells do not count</small>"]
```

*Source: `Delimited/DelimitedPass.cs`, `Delimited/HeaderDetector.cs`.* The contrast is the share of typed columns (columns that are not text) that the candidate disagrees with, so a row of units scores below the header that sits under it. A header with a blank or repeated name is still taken when nothing else qualifies, with an issue. A table made only of text takes its first row as the header and says so in the evidence. A first data row that is all `NULL` is never taken for a units row, so the interleaved separator rows of a DangerTest export do no harm. A comment line (`#`, `!`, `//`) is free text: it is never a header, an annotation row or a label, wherever it sits. The evidence lists the eligible candidates and the winner, and one summary line that counts the rest by reason.

With the dialect and header known, the file falls into regions, each with line and byte ranges. Regions tile the file: a blank line belongs to the region before it.

```mermaid
flowchart TD
    R1["Preamble: Lot=A12, Temp=25, comments<br/><small>keys always kept, values opt-in</small>"] --> RA["Annotation rows (optional): limits, units<br/><small>labels kept, cells opt-in</small>"]
    RA --> R2["Header row: Freq(MHz), Pout(dBm), …<br/><small>best-scoring candidate</small>"]
    R2 --> R3["Units row (optional): MHz, dBm<br/><small>text opt-in</small>"]
    R3 --> R4["Data rows<br/><small>the steady run of k fields</small>"]
    R4 --> R5["Footer (optional)"]
    R4 -. "same header again" .-> R6["Repeated blocks<br/><small>one per site or sweep</small>"]
```

*Source: `Delimited/DelimitedPass.cs`, `Delimited/LineClassifier.cs`, `Delimited/DialectSelector.cs`, `Delimited/HeaderDetector.cs`; `Delimited/BlockDetector.cs` (planned).*

squalify also reports how the table is padded (fixed line length or not) and whether numbers use a decimal comma. It describes a fixed-width table but still reads every row through the tokenizer. Every data row is read a second time to count the type of each field, so the descriptor already says which fields are numbers; the other field statistics come later.

## 5. Profiling one field

Each value is classified on its own: empty, true/false, whole number, decimal number, date, time or text. A field's type is the narrowest type that fits **every** non-empty value. Empty markers such as `NA`, `NA.` or `-` come from a fixed, versioned list that is never learned from the data, and the classifier says which marker it matched, so a consumer can decide which markers it treats as empty. Statistics are exact up to a cap. Past the cap they switch to a fixed approximation that gives the same answer every time, and the descriptor marks them as approximate.

Dates never assume a locale. `6/5/2026` could be June 5 or May 6, so squalify reports both possible orders and marks the field ambiguous. It then leaves out the date range and whether the dates rise or fall, because both would assume one order. The same goes for the clock: a time with `AM` or `PM` is a 12-hour time, and a two-digit year such as `6/5/26` stays text, because squalify would have to guess the century.

The classifier looks at one value at a time and takes the first test that fits, in this order. Two flags ride along: *leading zero* only on whole and decimal numbers, and *looks like a decimal comma* on a decimal number or text. It only reads the value and the file's decimal separator, so it gives the same answer on every machine and in every language setting.

```mermaid
flowchart TD
    V["One value, trimmed of tabs, line breaks, spaces and no-break spaces<br/><small>ValueClassifier.Trim, ValueClassifier.Classify</small>"] --> N{"In the empty list?<br/>NA, NA., N/A, null, NaN, -, blank<br/><small>ValueClassifier.NullVocabulary</small>"}
    N -- "yes" --> Nn["Empty, and which marker matched<br/><small>ValueClass.NullToken</small>"]
    N -- "no" --> B{"true, false, yes, no?"}
    B -- "yes" --> Bb["Boolean"]
    B -- "no" --> Num{"A number?<br/><small>decimal separator from the file</small>"}
    Num -- "whole" --> I["Whole number<br/><small>flag: leading zero</small>"]
    Num -- "decimal" --> F["Decimal number<br/><small>flags: leading zero, looks like a decimal comma</small>"]
    Num -- "no" --> D{"A date, a date and time,<br/>or a time?"}
    D -- "yes" --> Dd["Date or time, with every possible field order and the clock<br/><small>DateOrders</small>"]
    D -- "no" --> S["Text<br/><small>flag: looks like a decimal comma<br/>(for example 1,5 when the separator is a point)</small>"]
```

*Source: `Values/ValueClassifier.cs`, `Values/DateOrders.cs`, `Values/ValueClass.cs`.*

```mermaid
flowchart LR
    V["One value<br/>'12.5'"] --> C["Classify<br/><small>ValueClassifier</small>"]
    C --> T["Join into the field's type<br/><small>int → float → string;<br/>date orders intersect</small>"]
    C --> St["Update statistics<br/><small>count, min/max, mean, shapes</small>"]
    St --> X{"Over the cap?"}
    X -- "no" --> Exact["Exact distinct count and quantiles"]
    X -- "yes" --> Sk["Sketches: HyperLogLog,<br/>Greenwald-Khanna<br/><small>marked approximate</small>"]
```

*Source: `Values/ValueClassifier.cs`, `Fields/FieldAccumulator.cs` (planned), `Sketches/` (planned).*

## 6. What is always included, and what needs `--include-values`

By default a descriptor describes the **shape** of the data and its statistics, not its contents. Literal text from the file appears only when you ask for it: all of it with `--include-values`, or only named keys and columns with `--include-values-for <name>`.

A column with one value for the whole file, such as a serial number, is a special case. Its range would just be that value, so its statistics are opt-in too.

```mermaid
flowchart LR
    subgraph always["Always included"]
        direction TB
        A1["Header names, preamble keys,<br/>STDF test names"]
        A2["Types, counts, shapes"]
        A3["Number and date ranges,<br/>means, quantiles<br/><small>not for one-value columns</small>"]
        A4["Units parsed from header names"]
        A1 ~~~ A2 ~~~ A3 ~~~ A4
    end
    subgraph optin["Only with --include-values"]
        direction TB
        O1["Preamble and key=value values"]
        O2["Most common values, text min/max"]
        O3["Units-row and annotation cells,<br/>comment text"]
        O4["Statistics of one-value columns"]
        O1 ~~~ O2 ~~~ O3 ~~~ O4
    end
    always ~~~ optin
```

*Source: `ValuesPolicy.cs` (planned).*

## 7. Fingerprints

squalify hashes a stripped-down copy of the descriptor that contains no data values. Two files of the same type therefore get the same fingerprint, even when their data differs. There are four fingerprints at different levels of strictness. `structure` uses no column names at all, so it survives a changed column set.

A matching fingerprint does not prove two files are the same kind of file. Many unrelated CSVs share one `structure`, and files whose columns change per run get a new `layout` each time. Consumers combine fingerprints with names and hints.

```mermaid
flowchart LR
    D["Finished descriptor"] --> P["Value-free projection<br/><small>Projection</small>"]
    P --> S["structure<br/><small>format, dialect, region kinds;<br/>no names</small>"]
    P --> L["layout<br/><small>format, delimiter, regions,<br/>preamble keys, column names</small>"]
    P --> T["typed<br/><small>layout + column types</small>"]
    P --> N["names<br/><small>sorted column names only</small>"]
```

*Source: `Fingerprint/Projection.cs` (planned).*

## 8. Kind label and unit conventions

After profiling, two small rule sets read the finished descriptor. They never read the file.

- **Header-unit conventions.** Every column name is checked for how it carries a unit: `I_Total(A)`, `Pout[dBm]`, `VCC_V`, a units row, or none. The most common convention is reported.
- **Kind label.** A fixed, ordered list of rules suggests a generic label, such as `labSweepTable`, `stdfTestLog` or `touchstoneNetwork`. The first rule that matches wins, and every rule's result is kept as evidence.

```mermaid
flowchart LR
    D["Finished descriptor"] --> U["Header-unit conventions<br/><small>HeaderConventionParser</small>"]
    D --> K["Ordered kind rules<br/><small>KindRules</small>"]
    U --> K
    K --> L["kind: labSweepTable<br/><small>+ evidence per rule</small>"]
```

*Source: `Units/`, `Kind/` (planned).*

## 9. Why the output never changes

The same file and the same squalify version always give the same descriptor, byte for byte, on Windows, macOS and Linux. Five guardrails make that true.

```mermaid
flowchart LR
    G1["No time, paths or machine info<br/>in the output"] --> Same
    G2["One thread, input order;<br/>everything sorted before writing<br/><small>Ordinal, CountMap</small>"] --> Same
    G3["Fixed JSON writer: key order,<br/>LF newlines, number format<br/><small>DescriptorWriter, CanonicalNumber</small>"] --> Same
    G4["Platform-independent math and hashing<br/><small>DetMath, XxHash64</small>"] --> Same
    G5["Golden files compared<br/>on all three OSes<br/><small>Golden</small>"] --> Same
    Same["Byte-identical descriptor"]
```

*Source: `Determinism/Ordinal.cs`, `Determinism/DetMath.cs`, `Determinism/XxHash64.cs`, `Descriptor/CountMap.cs`, `Json/DescriptorWriter.cs`, `Json/CanonicalNumber.cs`; `../../tests/squalify.Tests/Golden.cs`.*

Logarithms are the one place where platforms can disagree: the system math library may differ by one bit between operating systems. squalify therefore computes them itself, from additions, multiplications and divisions only, which every CPU rounds identically. A reference table made with an arbitrary-precision tool pins the result bit for bit. Hashing uses squalify's own copy of xxHash64, checked against published test vectors.

## 10. Row classes

Some exports mix several kinds of row in one table: measurement rows, setup rows, error rows and empty separator rows, switching kind on almost every line. squalify groups rows by which columns are filled in and reports each group, how often the kind switches, and how many rows are empty. Column null counts are also given without the empty rows, so separators do not look like missing measurements.

```mermaid
flowchart LR
    R["Each data row"] --> M["Which columns are filled<br/><small>null mask</small>"]
    M --> G["Group rows with the same mask<br/><small>RowClassAccumulator</small>"]
    G --> O["rowClasses: count, columns,<br/>first and last line, switches,<br/>empty rows"]
```

*Source: `Fields/RowClassAccumulator.cs` (planned).*

## 11. Writing the descriptor

The finished descriptor is a tree of plain model objects. The writer turns it into bytes in one fixed way: it writes each object's keys in the order the schema document lists, and it never sorts, because the model already holds every list and map in order. Numbers and text go through fixed rules, so the same model gives the same bytes on every machine.

```mermaid
flowchart TD
    M["Descriptor model:<br/>lists and maps already sorted<br/><small>Descriptor, CountMap</small>"] --> W["Write every object<br/>in the documented key order<br/><small>DescriptorWriter</small>"]
    W --> N["Numbers: shortest round-trip form,<br/>-0 as 0, NaN and Infinity as strings<br/><small>CanonicalNumber</small>"]
    W --> T["Text and layout: UTF-8, no BOM,<br/>LF newlines, 2-space indent,<br/>one trailing LF<br/><small>DescriptorWriter options</small>"]
    N --> B["Descriptor bytes"]
    T --> B
    B -. "tests compare with" .-> G["Golden file<br/><small>Golden</small>"]
```

*Source: `Descriptor/`, `Json/DescriptorWriter.cs`, `Json/CanonicalNumber.cs`; `../../tests/squalify.Tests/Golden.cs`.* The key order is part of the schema: see [descriptor.md](descriptor.md#canonical-form).

## 12. Text, binary or empty?

The byte pass counts every byte of the file once and then applies seven rules, in order. The first rule that fits decides. A file is never rejected: odd bytes are counted, and invalid UTF-8 in ascii or utf-8 text is also reported as an issue. A few stray invalid bytes in an otherwise plain text file do not change its encoding, because a single bad byte in a large log should not turn it into "Latin-1" or "binary"; the allowance is at most 8 per file and at most 1 per 64 KiB.

```mermaid
flowchart LR
    S["Count every byte<br/><small>ByteSniffer</small>"] --> R1["1. No bytes?"]
    R1 --> R2["2. Known magic<br/>such as gzip?"]
    R2 --> R3["3. UTF-16/32<br/>byte order mark?"]
    R3 --> R4["4. NULs in a<br/>UTF-16 pattern?"]
    R4 --> R5["5. Other NULs, or over<br/>5% control bytes?"]
    R5 --> R6["6 and 7. Plain text<br/><small>SniffResult</small>"]
    R1 -. "yes" .-> E["empty"]
    R2 -. "yes" .-> B["binary<br/><small>magic named, not unpacked</small>"]
    R5 -. "yes" .-> B
    R3 -. "yes" .-> T16["text: UTF-16 or UTF-32"]
    R4 -. "yes" .-> T16
    R6 --> T["text: ascii, utf-8 or latin-1"]
```

*Source: `Sniff/ByteSniffer.cs`, `Sniff/SniffResult.cs`, `Sniff/MagicTable.cs`.* The exact thresholds and the magic table are in [descriptor.md](descriptor.md#encoding).
