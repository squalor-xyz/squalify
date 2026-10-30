// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The entry point of the library: profiles one file into a <see cref="Descriptor"/>.</summary>
public static class Profiler
{
    // The pipeline order of the evidence stages: evidence is sorted by stage, then by topic. Later slices append stages.
    private static readonly string[] StageOrder = ["encoding", "format", "dialect", "header", "unit", "type"];

    /// <summary>Profiles a source: the byte pass, the format choice, then the winning format profiler.</summary>
    /// <param name="source">The source.</param>
    /// <param name="options">The options that affect output.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="SqualifyException">
    /// The source cannot be read, its <see cref="IProfileSource.Name"/> is null, empty or contains <c>/</c> or <c>\</c> (a backslash is
    /// refused on every platform, also where a file name may hold one), or
    /// <see cref="ProfileOptions.MaxBytes"/> is zero or negative, <see cref="ProfileOptions.MaxFirstRecordBytes"/> is zero or
    /// negative or is given without <see cref="ProfileOptions.MaxBytes"/>, or the first record of a text file is longer
    /// than <see cref="ProfileOptions.MaxFirstRecordBytes"/> (nothing is profiled), or a limit is given and the source reports
    /// a negative <see cref="IProfileSource.Length"/>.
    /// </exception>
    public static Descriptor Profile(IProfileSource source, ProfileOptions options)
    {
        return Profile(source, options, ProfileLimits.Default);
    }

    /// <summary>Profiles a source with other field caps. Tests use it to inject small ones.</summary>
    /// <param name="source">The source.</param>
    /// <param name="options">The options that affect output.</param>
    /// <param name="fieldLimits">The caps that bound field profiling.</param>
    /// <param name="trace">Receives the peak of the estimated memory, or null.</param>
    /// <returns>The descriptor.</returns>
    internal static Descriptor Profile(IProfileSource source, ProfileOptions options, ProfileLimits fieldLimits, MemoryTrace? trace = null)
    {
        ArgumentNullException.ThrowIfNull(fieldLimits);
        Validate(source, options);

        try
        {
            return ProfileCore(source, options, fieldLimits, trace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SqualifyException($"Cannot read '{source.Name}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Profiles the start of a source and returns the descriptor together with the exact bytes it describes, from one read of
    /// the source: the cut is the one <see cref="ProfileOptions.MaxBytes"/> gives to <see cref="Profile(IProfileSource, ProfileOptions)"/>
    /// (a file whose bytes inside the limit sniff as binary at the limit; otherwise after the last LF, CR or CRLF, in the
    /// code units of the charset sniffed from the bytes inside the limit, inside the limit; a CR that ends the limit takes the
    /// LF after it; with no terminator inside the limit, to the first terminator or the end; a limit shorter than a byte order
    /// mark can sniff differently from the whole file), so the descriptor equals the one <see cref="Profile(IProfileSource, ProfileOptions)"/> gives for the same options, for a source whose
    /// <see cref="IProfileSource.Length"/> is its true size (<see cref="Profile(IProfileSource, ProfileOptions)"/> reads past a
    /// <see cref="IProfileSource.Length"/> that is shorter than the data when the limit is at least that length; this method never does).
    /// The source is opened once. The bytes are buffered, up to <see cref="ProfileOptions.MaxBytes"/> plus the overshoot (one
    /// code unit when a CR ends the limit; the rest of the first record when it is longer than the limit);
    /// <see cref="ProfileOptions.MaxFirstRecordBytes"/> caps the overshoot. A source must report its <see cref="IProfileSource.Length"/>,
    /// and is never read past it.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="options">The options that affect output; <see cref="ProfileOptions.MaxBytes"/> is required.</param>
    /// <returns>The descriptor, the prefix it describes and the scope; <c>file.size</c> is the length of the whole source.</returns>
    /// <exception cref="SqualifyException">
    /// Anything <see cref="Profile(IProfileSource, ProfileOptions)"/> rejects, with the same messages (so a source name that is
    /// null, empty or contains <c>/</c> or a backslash is refused as there, on every platform), and in addition: <see cref="ProfileOptions.MaxBytes"/>
    /// is null (it would buffer the whole source), the source reports a negative length, or the bytes to buffer would not fit
    /// in one array (about 2 GiB).
    /// </exception>
    public static ProfiledPrefix ProfilePrefix(IProfileSource source, ProfileOptions options)
    {
        Validate(source, options);
        if (options.MaxBytes is not { } maxBytes)
        {
            throw new SqualifyException("ProfilePrefix needs a byte limit (MaxBytes): without one it would buffer the whole source.");
        }

        var window = Math.Min(source.Length, maxBytes);
        if (window > Array.MaxLength)
        {
            throw new SqualifyException($"The byte limit of '{source.Name}' is too large to buffer: ProfilePrefix holds the prefix in memory, at most {Array.MaxLength} bytes.");
        }

        using var buffered = new BufferedSource(source, window);
        var descriptor = ProfileBuffered(buffered, options);
        var scope = descriptor.Scope!;
        return new ProfiledPrefix(descriptor, buffered.Prefix((int)scope.BytesRead));
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

    private static Descriptor ProfileBuffered(BufferedSource source, ProfileOptions options)
    {
        try
        {
            return ProfileCore(source, options, ProfileLimits.Default, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SqualifyException($"Cannot read '{source.Name}': {ex.Message}", ex);
        }
    }

    // The checks that need no read.
    private static void Validate(IProfileSource source, ProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        SourceNameGuard.Check(source.Name);

        if (options.MaxBytes is <= 0)
        {
            throw new SqualifyException("The byte limit (--max-bytes) must be a positive number of bytes.");
        }

        if (options.MaxFirstRecordBytes is <= 0)
        {
            throw new SqualifyException("The first-record ceiling (--max-first-record-bytes) must be a positive number of bytes.");
        }

        if (options.MaxFirstRecordBytes is not null && options.MaxBytes is null)
        {
            throw new SqualifyException("The first-record ceiling (--max-first-record-bytes) needs a byte limit (--max-bytes).");
        }

        if (options.MaxBytes is not null && source.Length < 0)
        {
            throw new SqualifyException($"The length of '{source.Name}' is not known: a source must report its length to be limited by a byte limit.");
        }
    }

    private static Descriptor ProfileCore(IProfileSource source, ProfileOptions options, ProfileLimits fieldLimits, MemoryTrace? trace)
    {
        var limits = fieldLimits.ApplyTo(Limits.Default);

        // A byte limit profiles a prefix view of the source: every pass below reads the view, so no stage reads past the cut.
        var whole = source;
        ScopeSection? scope = null;
        SniffResult sniff;
        if (options.MaxBytes is { } maxBytes)
        {
            var scoped = ScopeCut.Apply(source, maxBytes, limits.CharsetTolerance, options.MaxFirstRecordBytes);
            source = scoped.Source;
            sniff = scoped.Sniff;
            scope = scoped.Scope;
        }
        else
        {
            sniff = ByteSniffer.Sniff(source, limits.CharsetTolerance);
        }

        var context = new ProfileContext(options, limits, trace);
        var choice = FormatRegistry.Default.Detect(source, sniff, context);
        var profile = choice.Profiler?.Profile(source, sniff, context, choice.ProfilerScore!)
            ?? new FormatProfile(null, new LayoutSection(Array.Empty<Region>()), new LineEndings(0, 0, 0), Array.Empty<Issue>(), Array.Empty<Evidence>());

        var issues = new List<Issue>(profile.Issues);
        issues.AddRange(choice.NotChosenIssues ?? []);
        var utf8Family = sniff.Charset is "ascii" or "utf-8" or "latin-1";
        var facts = sniff.Facts;
        var invalidBytesTolerated = sniff.Charset is "ascii" or "utf-8" && facts.InvalidSequences > 0;
        if (invalidBytesTolerated)
        {
            issues.Add(LocatedIssue("invalidUtf8", facts.InvalidSequences, facts.InvalidOffsets, source, sniff, profile.Layout));
        }

        // A UTF-16 or UTF-32 charset comes from the BOM or the NUL pattern, so it has no latin-1 fallback and no tolerance:
        // every invalid unit is counted and reported.
        var unicode = InvalidUnicodeScanner.Scan(source, sniff);
        if (unicode.Units > 0)
        {
            issues.Add(LocatedIssue("invalidUnicode", unicode.Units, unicode.Offsets, source, sniff, profile.Layout));
        }

        var evidence = new List<Evidence>(sniff.Evidence) { choice.Evidence };
        evidence.AddRange(profile.Evidence);
        evidence.AddRange(choice.NotChosenEvidence ?? []);

        var projection = Projection.Compute(new ProjectionInput(choice.Family, sniff.Charset, profile.Dialect, profile.Layout, profile.Table));
        if (projection.Excluded > 0)
        {
            issues.Add(new Issue("nameExcludedFromFingerprint", projection.Excluded));
        }

        var descriptor = new Descriptor(
            Tool: new ToolSection(ToolInfo.Name, ToolInfo.Version),
            Options: options,
            Limits: profile.EffectiveLimits ?? limits,
            File: scope is { Complete: false }
                ? new FileSection(whole.Name, whole.Length, null)
                : new FileSection(source.Name, facts.Length, facts.Sha256),
            Encoding: new EncodingSection(
                sniff.Kind,
                sniff.Charset,
                sniff.Bom,
                profile.LineEndings,
                utf8Family ? facts.InvalidBytes : unicode.Bytes,
                facts.ControlBytes,
                invalidBytesTolerated),
            Format: new FormatSection(choice.Family, profile.Dialect, choice.Score),
            Kind: InterimKind(sniff.Kind),
            Layout: profile.Layout,
            Fingerprint: projection.Fingerprint,
            Hints: HintBuilder.Build(source.Name, sniff, profile.Dialect, profile.Layout, profile.Table, profile.StableLines ?? []),
            Issues: Ordinal.SortedBy(issues, static i => i.Code),
            Evidence: SortEvidence(evidence),
            Table: profile.Table,
            Binary: profile.Binary,
            Scope: scope);

        // An allow-list name that matches no key, label or field is reported. The names come from the command line, not
        // from the data, so they are safe to write.
        var unmatched = ValuesPolicy.UnmatchedNames(descriptor);
        if (unmatched.Count > 0)
        {
            issues.Add(new Issue("includeValuesForUnmatched", unmatched.Count, Names: unmatched));
            descriptor = descriptor with { Issues = Ordinal.SortedBy(issues, static i => i.Code) };
        }

        return descriptor;
    }

    // An issue that says where invalid bytes are: the byte offsets, the line of each and the layout region it falls in (one
    // entry each, in the same order). The layout is known by now, so the region is a lookup; the line costs one more read
    // of the source up to the last offset, and only for a file that has invalid bytes.
    private static Issue LocatedIssue(string code, long count, IReadOnlyList<long> offsets, IProfileSource source, SniffResult sniff, LayoutSection layout)
    {
        return new Issue(
            code,
            count,
            FirstLines: InvalidByteLocator.Lines(source, sniff, offsets),
            Offsets: offsets,
            Regions: InvalidByteLocator.RegionNames(layout, offsets));
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
