// SPDX-License-Identifier: Apache-2.0
using squalor.squalify;

namespace squalor.squalify.Tests;

public class FormatRegistryTests
{
    private sealed class FakeProfiler : IFormatProfiler
    {
        private readonly double _score;
        private readonly FormatScore? _full;

        public FakeProfiler(string family, double score)
        {
            Family = family;
            _score = score;
        }

        public FakeProfiler(string family, FormatScore full)
        {
            Family = family;
            _score = full.Score;
            _full = full;
        }

        public string Family { get; }

        public FormatScore Score(IProfileSource source, SniffResult sniff, ProfileContext context) => _full ?? new FormatScore(_score, "fake " + Family);

        public FormatProfile Profile(IProfileSource source, SniffResult sniff, ProfileContext context, FormatScore score) => throw new NotSupportedException();
    }

    private static readonly IProfileSource Source = new BytesSource("a.txt", TestBytes.Ascii("hello\n"));

    private static readonly ProfileContext Context = new(ProfileOptions.Create(false, Array.Empty<string>()), Limits.Default);

    private static SniffResult SniffText()
    {
        return ByteSniffer.Sniff(new BytesSource("a.txt", TestBytes.Ascii("hello\n")), new CharsetTolerance(8, 65536));
    }

    [Fact]
    public void Detect_EvidenceListsEveryProfilerScore_InRegistryOrder()
    {
        // Given out of order on purpose: the registry order is stdf, touchstone, keyValue, delimited, text, binary.
        var registry = new FormatRegistry([new FakeProfiler("text", 0.1), new FakeProfiler("stdf", 0.0), new FakeProfiler("binary", 0.1), new FakeProfiler("delimited", 0.4), new FakeProfiler("keyValue", 0.2)]);
        var choice = registry.Detect(Source, SniffText(), Context);

        Assert.Equal("format", choice.Evidence.Stage);
        Assert.Equal("family", choice.Evidence.Topic);
        Assert.Equal("delimited", choice.Evidence.Chosen);
        Assert.Equal(new[] { "stdf", "keyValue", "delimited", "text", "binary" }, choice.Evidence.Candidates.Select(static c => c.Value).ToArray());
        Assert.Equal(new[] { 0.0, 0.2, 0.4, 0.1, 0.1 }, choice.Evidence.Candidates.Select(static c => c.Score).ToArray());
        Assert.Equal("fake stdf", choice.Evidence.Candidates[0].Reason);
        Assert.Equal("delimited", choice.Family);
        Assert.Equal(0.4, choice.Score);
    }

    [Fact]
    public void Detect_Tie_GoesToTheEarlierProfilerInTheRegistryOrder()
    {
        var registry = new FormatRegistry([new FakeProfiler("binary", 0.5), new FakeProfiler("text", 0.5), new FakeProfiler("delimited", 0.5)]);
        Assert.Equal("delimited", registry.Detect(Source, SniffText(), Context).Family);
    }

    [Fact]
    public void Detect_HigherScoreBeatsEarlierPosition()
    {
        var registry = new FormatRegistry([new FakeProfiler("stdf", 0.2), new FakeProfiler("binary", 0.3)]);
        Assert.Equal("binary", registry.Detect(Source, SniffText(), Context).Family);
    }

    [Fact]
    public void Registry_UnknownOrDuplicateFamily_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FormatRegistry([new FakeProfiler("yaml", 1)]));
        Assert.Throws<ArgumentException>(() => new FormatRegistry([new FakeProfiler("text", 1), new FakeProfiler("text", 1)]));
    }

    [Fact]
    public void Registry_Order_IsTheDocumentedOne()
    {
        Assert.Equal(new[] { "stdf", "touchstone", "keyValue", "delimited", "text", "binary" }, FormatRegistry.Order.ToArray());
    }

    [Fact]
    public void Registry_Default_RegistersDelimitedTextAndBinary()
    {
        var choice = FormatRegistry.Default.Detect(Source, SniffText(), Context);
        Assert.Equal(new[] { "delimited", "text", "binary" }, choice.Evidence.Candidates.Select(static c => c.Value).ToArray());
        Assert.Equal("text", choice.Family);
        Assert.Equal(0.1, choice.Score);
        Assert.Equal(new[] { 0.0, 0.1, 0.0 }, choice.Evidence.Candidates.Select(static c => c.Score).ToArray());
    }

    [Fact]
    public void Registry_Default_BinaryScoresPointTwoWithANamedMagic()
    {
        var gzip = ByteSniffer.Sniff(new BytesSource("a.gz", TestBytes.Hex("1F 8B 08 00 00")), new CharsetTolerance(8, 65536));
        var choice = FormatRegistry.Default.Detect(Source, gzip, Context);
        Assert.Equal("binary", choice.Family);
        Assert.Equal(0.2, choice.Score);
        Assert.Contains("gzip", choice.Evidence.Candidates.Single(static c => c.Value == "binary").Reason);

        var plain = ByteSniffer.Sniff(new BytesSource("a.bin", TestBytes.Hex("01 02 03 04 05 06")), new CharsetTolerance(8, 65536));
        var plainChoice = FormatRegistry.Default.Detect(Source, plain, Context);
        Assert.Equal("binary", plainChoice.Family);
        Assert.Equal(0.1, plainChoice.Score);
    }

    [Fact]
    public void Registry_Default_EmptyFileIsEmptyAndEveryProfilerScoresZero()
    {
        var empty = ByteSniffer.Sniff(new BytesSource("a", Array.Empty<byte>()), new CharsetTolerance(8, 65536));
        var choice = FormatRegistry.Default.Detect(Source, empty, Context);
        Assert.Equal("empty", choice.Family);
        Assert.Equal("empty", choice.Evidence.Chosen);
        Assert.Null(choice.Profiler);
        Assert.Equal(new[] { 0.0, 0.0, 0.0 }, choice.Evidence.Candidates.Select(static c => c.Score).ToArray());
    }

    [Fact]
    public void Detect_LosingProfilerNotChosenIssues_AreCarried_AndTheWinnersAreNot()
    {
        var declined = new FormatScore(0.0, "declined", NotChosenIssues: [new Issue("tooIrregular", 1)], NotChosenEvidence: [new Evidence("dialect", "delimiter", null, [])]);
        var winner = new FormatScore(0.9, "won", NotChosenIssues: [new Issue("neverWritten", 1)]);
        var registry = new FormatRegistry([new FakeProfiler("delimited", declined), new FakeProfiler("text", winner)]);
        var choice = registry.Detect(Source, SniffText(), Context);
        Assert.Equal("text", choice.Family);
        Assert.Equal(new[] { "tooIrregular" }, choice.NotChosenIssues!.Select(static i => i.Code).ToArray());
        Assert.Equal(new[] { "dialect" }, choice.NotChosenEvidence!.Select(static e => e.Stage).ToArray());
        Assert.Same(winner, choice.ProfilerScore);
    }
}
