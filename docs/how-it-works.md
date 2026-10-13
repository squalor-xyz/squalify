# How squalify works

> **Design stage.** This page describes the agreed design (spec pass, 2026-10-13). No slice has landed yet, so every source file is marked *(planned)*. When an architectural review or a slice changes how squalify works, this page and its diagrams are updated in the same change (see "Docs" in `AGENTS.md`).

squalify reads one data file and writes a **descriptor**: a JSON summary of what the file is and what is inside it. Other tools, such as DataBall, read the descriptor to decide how to handle the file. squalify never changes the file and never needs the network.

Each diagram shows one idea. Labels are in plain language, with the code name in small text. Source paths are relative to `src/squalify/`.

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

*Source: `Profiler.cs`, `Formats/` (planned).*

## 2. One profile run

Every file goes through the same steps. A step that reads the file reads all of it, start to end, and keeps only bounded summaries in memory. The file can be far larger than RAM.

```mermaid
flowchart TD
    A["1. Byte pass: hash, encoding,<br/>text or binary, magic bytes<br/><small>ByteSniffer</small>"] --> B["2. Choose a format<br/><small>FormatRegistry</small>"]
    B --> C["3. Read the structure:<br/>regions, header, records<br/><small>IFormatProfiler</small>"]
    C --> D["4. Profile every field:<br/>types and statistics<br/><small>FieldAccumulator</small>"]
    D --> E["5. Summarize: fingerprints, hints,<br/>unit conventions, kind label<br/><small>Fingerprint, Hints, Kind</small>"]
    E --> F["6. Write canonical JSON<br/><small>DescriptorWriter</small>"]
```

*Source: `Profiler.cs`, `Sniff/`, `Json/DescriptorWriter.cs` (planned).*

## 3. Choosing a format

Every profiler looks at the byte-pass result and gives a score from 0 to 1. The highest score wins, and a tie goes to whichever profiler comes first in the registry list. Every score and its reason are kept in the descriptor's evidence, so you can see why a format was chosen.

```mermaid
flowchart LR
    S["Byte-pass result<br/><small>SniffResult</small>"] --> P1["Parquet: magic at both ends → 1.0"]
    S --> P2["STDF: FAR record at start → 1.0"]
    S --> P3["Touchstone: extension + option line → 0.9"]
    S --> P4["Key=value document → up to 0.6"]
    S --> P5["Delimited table → up to 0.7"]
    S --> P6["Plain text / other binary → 0.1–0.2"]
    P1 & P2 & P3 & P4 & P5 & P6 --> W["Highest score wins<br/><small>ties: registry order</small>"]
    W --> Ev["All scores go to evidence"]
```

*Source: `Formats/FormatRegistry.cs` (planned).*

## 4. Reading a delimited file

A lab CSV is often more than a header and rows. squalify splits the file into regions. It tries every common delimiter at the same time and picks the one that gives the longest steady run of rows with the same field count.

```mermaid
flowchart TD
    R1["Preamble: Lot=A12, Temp=25, comments<br/><small>keys always kept, values opt-in</small>"] --> R2["Header row: Freq(MHz), Pout(dBm), …"]
    R2 --> R3["Units row (optional): MHz, dBm<br/><small>text opt-in</small>"]
    R3 --> R4["Data rows<br/><small>the steady run of k fields</small>"]
    R4 --> R5["Footer (optional)"]
    R4 -. "same header again" .-> R6["Repeated blocks<br/><small>one per site or sweep</small>"]
```

*Source: `Delimited/DialectSelector.cs`, `Delimited/HeaderDetector.cs`, `Delimited/BlockDetector.cs` (planned).*

## 5. Profiling one field

Each value is classified on its own: empty, true/false, whole number, decimal number, date, time or text. A field's type is the narrowest type that fits **every** non-empty value. Statistics are exact up to a cap. Past the cap they switch to a fixed approximation that gives the same answer every time, and the descriptor marks them as approximate.

```mermaid
flowchart LR
    V["One value<br/>'12.5'"] --> C["Classify<br/><small>ValueClassifier</small>"]
    C --> T["Join into the field's type<br/><small>int → float → string</small>"]
    C --> St["Update statistics<br/><small>count, min/max, mean, shapes</small>"]
    St --> X{"Over the cap?"}
    X -- "no" --> Exact["Exact distinct count and quantiles"]
    X -- "yes" --> Sk["Sketches: HyperLogLog,<br/>Greenwald-Khanna<br/><small>marked approximate</small>"]
```

*Source: `Values/ValueClassifier.cs`, `Fields/FieldAccumulator.cs`, `Sketches/` (planned).*

## 6. What is always included, and what needs `--include-values`

By default a descriptor describes the **shape** of the data and its statistics, not its contents. Literal text from the file appears only when you ask for it.

```mermaid
flowchart LR
    subgraph always["Always included"]
        direction TB
        A1["Header names, preamble keys,<br/>STDF test names"]
        A2["Types, counts, shapes"]
        A3["Number and date ranges,<br/>means, quantiles"]
        A4["Units parsed from header names"]
        A1 ~~~ A2 ~~~ A3 ~~~ A4
    end
    subgraph optin["Only with --include-values"]
        direction TB
        O1["Preamble and key=value values"]
        O2["Most common values, text min/max"]
        O3["Units-row text, comment text"]
        O1 ~~~ O2 ~~~ O3
    end
    always ~~~ optin
```

*Source: `ValuesPolicy.cs` (planned).*

## 7. Fingerprints

squalify hashes a stripped-down copy of the descriptor that contains no data values. Two files of the same type therefore get the same fingerprint, even when their data differs. There are three fingerprints at different levels of strictness.

```mermaid
flowchart LR
    D["Finished descriptor"] --> P["Value-free projection<br/><small>Projection</small>"]
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
    G2["One thread, input order;<br/>everything sorted before writing"] --> Same
    G3["Fixed JSON writer: key order,<br/>LF newlines, number format<br/><small>DescriptorWriter</small>"] --> Same
    G4["Platform-independent math and hashing<br/><small>DetMath, XxHash64</small>"] --> Same
    G5["Golden files compared<br/>on all three OSes"] --> Same
    Same["Byte-identical descriptor"]
```

*Source: `Determinism/`, `Json/` (planned); `tests/squalify.Tests/Golden.cs` (planned).*
