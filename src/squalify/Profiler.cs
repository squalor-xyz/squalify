// SPDX-License-Identifier: Apache-2.0
using System.Security.Cryptography;
using System.Text;

namespace squalor.squalify;

/// <summary>The entry point of the library: profiles one file into a <see cref="Descriptor"/>.</summary>
public static class Profiler
{
    // The pipeline order of the evidence stages: evidence is sorted by stage, then by topic. Later slices append stages.
    private static readonly string[] StageOrder = ["encoding", "format", "dialect", "header", "type"];

    /// <summary>Profiles a source: the byte pass, the format choice, then the winning format profiler.</summary>
    /// <param name="source">The source.</param>
    /// <param name="options">The options that affect output.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="SqualifyException">The source cannot be read, or its <see cref="IProfileSource.Name"/> is empty or contains <c>/</c> or <c>\</c>.</exception>
    public static Descriptor Profile(IProfileSource source, ProfileOptions options)
    {
        return Profile(source, options, ProfileLimits.Default);
    }

    /// <summary>Profiles a source with other field caps. Tests use it to inject small ones.</summary>
    /// <param name="source">The source.</param>
    /// <param name="options">The options that affect output.</param>
    /// <param name="fieldLimits">The caps that bound field profiling.</param>
    /// <returns>The descriptor.</returns>
    internal static Descriptor Profile(IProfileSource source, ProfileOptions options, ProfileLimits fieldLimits)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fieldLimits);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(source.Name) || source.Name.AsSpan().IndexOfAny('/', '\\') >= 0)
        {
            throw new SqualifyException("The source name must be a bare file name, with no directory part and not empty; a path never reaches the descriptor.");
        }

        try
        {
            return ProfileCore(source, options, fieldLimits);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SqualifyException($"Cannot read '{source.Name}': {ex.Message}", ex);
        }
    }

    /// <summary>Profiles a file.</summary>
    /// <param name="path">The path of a file.</param>
    /// <param name="options">The options that affect output.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="SqualifyException">The path is a directory, or the file cannot be read.</exception>
    public static Descriptor Profile(string path, ProfileOptions options)
    {
        return Profile(new FileSource(path), options);
    }

    private static Descriptor ProfileCore(IProfileSource source, ProfileOptions options, ProfileLimits fieldLimits)
    {
        var limits = fieldLimits.ApplyTo(Limits.Default);
        var sniff = ByteSniffer.Sniff(source, limits.CharsetTolerance);
        var context = new ProfileContext(options, limits);
        var choice = FormatRegistry.Default.Detect(source, sniff, context);
        var profile = choice.Profiler?.Profile(source, sniff, context, choice.ProfilerScore!)
            ?? new FormatProfile(null, new LayoutSection(Array.Empty<Region>()), new LineEndings(0, 0, 0), Array.Empty<Issue>(), Array.Empty<Evidence>());

        var issues = new List<Issue>(profile.Issues);
        issues.AddRange(choice.NotChosenIssues ?? []);
        var utf8Family = sniff.Charset is "ascii" or "utf-8" or "latin-1";
        var facts = sniff.Facts;
        if (sniff.Charset is "ascii" or "utf-8" && facts.InvalidSequences > 0)
        {
            issues.Add(new Issue("invalidUtf8", facts.InvalidSequences, Offsets: facts.InvalidOffsets));
        }

        var evidence = new List<Evidence>(sniff.Evidence) { choice.Evidence };
        evidence.AddRange(profile.Evidence);
        evidence.AddRange(choice.NotChosenEvidence ?? []);

        var extension = Path.GetExtension(source.Name);
        return new Descriptor(
            Tool: new ToolSection(ToolInfo.Name, ToolInfo.Version),
            Options: options,
            Limits: profile.EffectiveLimits ?? limits,
            File: new FileSection(source.Name, facts.Length, facts.Sha256),
            Encoding: new EncodingSection(
                sniff.Kind,
                sniff.Charset,
                sniff.Bom,
                profile.LineEndings,
                utf8Family ? facts.InvalidBytes : 0,
                facts.ControlBytes),
            Format: new FormatSection(choice.Family, profile.Dialect, choice.Score),
            Kind: InterimKind(sniff.Kind),
            Layout: profile.Layout,
            Fingerprint: InterimFingerprint(choice.Family),
            Hints: new Hints(
                Extension: extension.Length == 0 ? null : extension.ToLowerInvariant(),
                Magic: sniff.Magic,
                StableLines: Array.Empty<StableLine>(),
                HeaderFields: null,
                HeaderPrefix: null,
                NonNullFields: null,
                FieldCount: null,
                DataStartLine: null,
                Delimiter: null,
                HeaderConvention: null,
                ParquetColumns: null,
                Stdf: null,
                FileNameShape: FileNameShape.Of(source.Name)),
            Issues: Ordinal.SortedBy(issues, static i => i.Code),
            Evidence: SortEvidence(evidence),
            Table: profile.Table,
            Binary: profile.Binary);
    }

    // Until sq-15 adds the rule table, the kind label follows the byte-pass class. The rule ids are those of the
    // proposed sq-15 table (K09 text, K11 binary, K12 empty).
    private static KindLabel InterimKind(string encodingKind)
    {
        return encodingKind switch
        {
            "empty" => new KindLabel("empty", "K12", 1.0),
            "binary" => new KindLabel("binary", "K11", 1.0),
            _ => new KindLabel("text", "K09", 1.0),
        };
    }

    // Until sq-06 adds the projections, every hash covers only the family: version 0 marks the interim form.
    private static Fingerprint InterimFingerprint(string family)
    {
        var projection = Hash("{\"family\":\"" + family + "\"}");
        return new Fingerprint(0, projection, projection, projection, Hash("[]"));
    }

    private static string Hash(string text)
    {
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    internal static IReadOnlyList<Evidence> SortEvidence(IEnumerable<Evidence> evidence)
    {
        return evidence
            .OrderBy(static e => StageRank(e.Stage))
            .ThenBy(static e => e.Topic, StringComparer.Ordinal)
            .ToArray();
    }

    private static int StageRank(string stage)
    {
        var index = Array.IndexOf(StageOrder, stage);
        return index < 0 ? StageOrder.Length : index;
    }
}
