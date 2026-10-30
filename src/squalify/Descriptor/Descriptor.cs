// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The descriptor: the complete result of profiling one file. The canonical writer emits the keys in the order
/// documented in <c>docs/descriptor.md</c>, not in the order of these parameters. Optional members are null when absent.
/// Producers must build every collection sorted by its documented key; the writer never sorts. Equality is by value:
/// two descriptors are equal when every member is equal, and each list is compared member by member in order (the type
/// of the list does not matter), as is every record the descriptor holds.
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
/// <param name="Scope">What was read, written only when a byte limit was given.</param>
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
    StdfSection? Stdf = null,
    ScopeSection? Scope = null)
{
    /// <summary>The descriptor schema version written as <c>schemaVersion</c>.</summary>
    public const string SchemaVersion = "1.0";

    /// <inheritdoc />
    public bool Equals(Descriptor? other)
    {
        return other is not null
            && EqualityComparer<ToolSection>.Default.Equals(Tool, other.Tool)
            && EqualityComparer<ProfileOptions>.Default.Equals(Options, other.Options)
            && EqualityComparer<Limits>.Default.Equals(Limits, other.Limits)
            && EqualityComparer<FileSection>.Default.Equals(File, other.File)
            && EqualityComparer<EncodingSection>.Default.Equals(Encoding, other.Encoding)
            && EqualityComparer<FormatSection>.Default.Equals(Format, other.Format)
            && EqualityComparer<KindLabel>.Default.Equals(Kind, other.Kind)
            && EqualityComparer<LayoutSection>.Default.Equals(Layout, other.Layout)
            && EqualityComparer<Fingerprint>.Default.Equals(Fingerprint, other.Fingerprint)
            && EqualityComparer<Hints>.Default.Equals(Hints, other.Hints)
            && ValueEquality.ListEquals(Issues, other.Issues)
            && ValueEquality.ListEquals(Evidence, other.Evidence)
            && EqualityComparer<TableSection?>.Default.Equals(Table, other.Table)
            && ValueEquality.ListEquals(Sections, other.Sections)
            && EqualityComparer<BinarySection?>.Default.Equals(Binary, other.Binary)
            && EqualityComparer<StdfSection?>.Default.Equals(Stdf, other.Stdf)
            && EqualityComparer<ScopeSection?>.Default.Equals(Scope, other.Scope);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Tool);
        hash.Add(Options);
        hash.Add(Limits);
        hash.Add(File);
        hash.Add(Encoding);
        hash.Add(Format);
        hash.Add(Kind);
        hash.Add(Layout);
        hash.Add(Fingerprint);
        hash.Add(Hints);
        ValueEquality.AddList(ref hash, Issues);
        ValueEquality.AddList(ref hash, Evidence);
        hash.Add(Table);
        ValueEquality.AddList(ref hash, Sections);
        hash.Add(Binary);
        hash.Add(Stdf);
        hash.Add(Scope);
        return hash.ToHashCode();
    }
}

/// <summary>The <c>tool</c> section.</summary>
/// <param name="Name">The tool name, <c>squalify</c>.</param>
/// <param name="Version">The tool version, <c>x.y.z</c>.</param>
public sealed record ToolSection(string Name, string Version);

/// <summary>The <c>file</c> section. The name is never a path.</summary>
/// <param name="Name">The file name without directories.</param>
/// <param name="Size">Size of the whole file in bytes, also when only a prefix was profiled.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the whole content, or null when only a prefix was read.</param>
public sealed record FileSection(string Name, long Size, string? Sha256);

/// <summary>The <c>scope</c> section: what a byte-limited profile read. It is written only when a limit was given.</summary>
/// <param name="MaxBytes">The byte limit.</param>
/// <param name="BytesRead">The bytes that the profile covers: the limit cut at a record end, or more when the first record is longer.</param>
/// <param name="Complete">True when the profile covers the whole file.</param>
public sealed record ScopeSection(long MaxBytes, long BytesRead, bool Complete);

/// <summary>The <c>encoding</c> section.</summary>
/// <param name="Kind"><c>empty</c>, <c>text</c> or <c>binary</c>.</param>
/// <param name="Charset">The charset, or null when the file is not text.</param>
/// <param name="Bom">The BOM name, or null when there is none.</param>
/// <param name="LineEndings">Line-ending counts.</param>
/// <param name="InvalidBytes">Count of bytes in invalid UTF-8 sequences (UTF-8, ASCII and latin-1 files) or in invalid UTF-16 or UTF-32 units (those charsets).</param>
/// <param name="ControlBytes">Count of control bytes.</param>
/// <param name="InvalidBytesTolerated">True when the charset (<c>ascii</c> or <c>utf-8</c>) was kept although the file holds invalid UTF-8 sequences, under <c>limits.charsetTolerance</c>. Written only when true.</param>
public sealed record EncodingSection(
    string Kind,
    string? Charset,
    string? Bom,
    LineEndings LineEndings,
    long InvalidBytes,
    long ControlBytes,
    bool InvalidBytesTolerated = false);

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
