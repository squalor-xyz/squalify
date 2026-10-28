// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The descriptor: the complete result of profiling one file. The canonical writer emits the keys in the order
/// documented in <c>docs/descriptor.md</c>, not in the order of these parameters. Optional members are null when absent.
/// Producers must build every collection sorted by its documented key; the writer never sorts.
/// </summary>
/// <param name="Tool">The tool that wrote the descriptor.</param>
/// <param name="Options">Options that affect output.</param>
/// <param name="Limits">Caps and table versions in effect.</param>
/// <param name="File">The profiled file.</param>
/// <param name="Encoding">The byte-level encoding result.</param>
/// <param name="Format">The chosen format family and its dialect.</param>
/// <param name="Kind">The generic kind label.</param>
/// <param name="Layout">Regions and header convention.</param>
/// <param name="Fingerprint">The structural fingerprints.</param>
/// <param name="Hints">Detect-rule hints.</param>
/// <param name="Issues">Issues; the caller sorts by code, ordinally.</param>
/// <param name="Evidence">The inference trail.</param>
/// <param name="Table">The table profile, for tabular files.</param>
/// <param name="Sections">Key=value sections, for key=value documents.</param>
/// <param name="Binary">The binary profile, for binary files.</param>
/// <param name="Stdf">The STDF profile, for STDF files.</param>
public sealed record Descriptor(
    ToolSection Tool,
    ProfileOptions Options,
    Limits Limits,
    FileSection File,
    EncodingSection Encoding,
    FormatSection Format,
    KindLabel Kind,
    LayoutSection Layout,
    Fingerprint Fingerprint,
    Hints Hints,
    IReadOnlyList<Issue> Issues,
    IReadOnlyList<Evidence> Evidence,
    TableSection? Table = null,
    IReadOnlyList<KeyValueSection>? Sections = null,
    BinarySection? Binary = null,
    StdfSection? Stdf = null)
{
    /// <summary>The descriptor schema version written as <c>schemaVersion</c>.</summary>
    public const string SchemaVersion = "1.0";
}

/// <summary>The <c>tool</c> section.</summary>
/// <param name="Name">The tool name, <c>squalify</c>.</param>
/// <param name="Version">The tool version, <c>x.y.z</c>.</param>
public sealed record ToolSection(string Name, string Version);

/// <summary>The <c>file</c> section. The name is never a path.</summary>
/// <param name="Name">The file name without directories.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the content.</param>
public sealed record FileSection(string Name, long Size, string Sha256);

/// <summary>The <c>encoding</c> section.</summary>
/// <param name="Kind"><c>empty</c>, <c>text</c> or <c>binary</c>.</param>
/// <param name="Charset">The charset, or null when the file is not text.</param>
/// <param name="Bom">The BOM name, or null when there is none.</param>
/// <param name="LineEndings">Line-ending counts.</param>
/// <param name="InvalidBytes">Count of bytes in invalid UTF-8 sequences (UTF-8, ASCII and latin-1 files) or in invalid UTF-16 or UTF-32 units (those charsets).</param>
/// <param name="ControlBytes">Count of control bytes.</param>
public sealed record EncodingSection(
    string Kind,
    string? Charset,
    string? Bom,
    LineEndings LineEndings,
    long InvalidBytes,
    long ControlBytes);

/// <summary>Line-ending counts.</summary>
/// <param name="Crlf">Count of CRLF endings.</param>
/// <param name="Lf">Count of lone LF endings.</param>
/// <param name="Cr">Count of lone CR endings.</param>
public sealed record LineEndings(long Crlf, long Lf, long Cr);

/// <summary>The <c>kind</c> section: a generic label with the rule that chose it.</summary>
/// <param name="Label">The kind label, for example <c>labSweepTable</c>.</param>
/// <param name="RuleId">The matching rule, for example <c>K05</c>.</param>
/// <param name="Confidence">Confidence between 0 and 1.</param>
public sealed record KindLabel(string Label, string RuleId, double Confidence);

/// <summary>The <c>fingerprint</c> section.</summary>
/// <param name="Version">The projection version.</param>
/// <param name="Structure"><c>sha256:</c> hash with no names.</param>
/// <param name="Layout"><c>sha256:</c> hash with names and layout.</param>
/// <param name="Typed"><c>sha256:</c> hash of layout plus types.</param>
/// <param name="Names"><c>sha256:</c> hash of the sorted names.</param>
public sealed record Fingerprint(long Version, string Structure, string Layout, string Typed, string Names);
