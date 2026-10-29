// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// sq-42: the bounds on the null spellings (64 distinct spellings of at most 256 characters) are published in
/// <c>limits.nullSpellings { max, maxChars }</c>, always written, and are the bounds the profiler enforces (sq-43 adds
/// <c>maxDistinctOther</c>, tested in <see cref="NullSpellingsOtherDistinctTests"/>).
/// </summary>
public sealed class NullSpellingLimitsTests
{
    private static Descriptor ProfileText(string name, string text)
    {
        return Profiler.Profile(new BytesSource(name, Encoding.UTF8.GetBytes(text)), ProfileOptions.Create(false, Array.Empty<string>()));
    }

    private static JsonElement WrittenLimits(Descriptor descriptor)
    {
        using var document = JsonDocument.Parse(DescriptorWriter.ToBytes(descriptor));
        return document.RootElement.GetProperty("limits").Clone();
    }

    private static (long Max, long MaxChars) PublishedBounds(Descriptor descriptor)
    {
        var limits = WrittenLimits(descriptor);
        Assert.True(limits.TryGetProperty("nullSpellings", out var bounds), "limits.nullSpellings is not written");
        return (bounds.GetProperty("max").GetInt64(), bounds.GetProperty("maxChars").GetInt64());
    }

    private static string DocsText(string file)
    {
        var path = Path.GetFullPath(Path.Combine(Golden.SourceFixturesRoot(), "..", "..", "..", "docs", file));
        Assert.True(File.Exists(path), "docs/" + file + " not found at " + path);
        return File.ReadAllText(path);
    }

    private static string Section(string text, string heading)
    {
        var start = text.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "no section '" + heading + "'");
        var level = heading.TakeWhile(static c => c == '#').Count();
        var next = Regex.Match(text[(start + heading.Length + 2)..], "\n#{1," + level.ToString(CultureInfo.InvariantCulture) + "} ");
        return next.Success ? text.Substring(start, heading.Length + 2 + next.Index) : text[start..];
    }

    // ---- Published ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Published_EveryDescriptorWithLimits_CarriesTheBounds_AndTheyAreTheCountersConstants()
    {
        Descriptor[] descriptors =
        [
            ProfileText("t.csv", "a,b\n1,2\n3,4\n"),
            ProfileText("t.txt", "just some prose\nand a second line\n"),
            Profiler.Profile(new BytesSource("t.bin", [0, 1, 2, 3, 0xFF, 0xFE, 0, 0, 7, 9]), ProfileOptions.Create(false, Array.Empty<string>())),
            ProfileText("t.dat", string.Empty),
            TestModels.Minimal(),
            TestModels.FullDelimited(),
            TestModels.FullBinaryStdf(),
        ];

        foreach (var descriptor in descriptors)
        {
            var (max, maxChars) = PublishedBounds(descriptor);

            // The pinned values of this tool version, and the very constants that SpellingCounter enforces.
            Assert.Equal(64, max);
            Assert.Equal(256, maxChars);
            Assert.Equal(SpellingCounter.MaxSpellings, max);
            Assert.Equal(SpellingCounter.MaxLength, maxChars);
        }

        Assert.Equal(new NullSpellingLimits(SpellingCounter.MaxSpellings, SpellingCounter.MaxLength, SpellingCounter.MaxDistinctOther), Limits.Default.NullSpellings);
    }

    [Fact]
    public void Published_IsOneObject_WithMaxAndMaxCharsInThatOrder()
    {
        var limits = WrittenLimits(TestModels.FullDelimited());
        var bounds = limits.GetProperty("nullSpellings");
        Assert.Equal(JsonValueKind.Object, bounds.ValueKind);
        Assert.Equal(["max", "maxChars", "maxDistinctOther"], bounds.EnumerateObject().Select(static p => p.Name).ToArray());
        Assert.False(limits.TryGetProperty("maxNullSpellings", out _));
        Assert.False(limits.TryGetProperty("maxNullSpellingChars", out _));
    }

    [Fact]
    public void Published_AWrittenValueIsTheModelsValue_NotAFixedText()
    {
        // The writer writes what the model holds, so a changed bound is a changed number (each member from its own property).
        var plain = TestModels.FullDelimited();
        var changed = plain with { Limits = plain.Limits with { NullSpellings = new NullSpellingLimits(7, 11, 5) } };
        Assert.Equal((7L, 11L), PublishedBounds(changed));
    }

    [Fact]
    public void Record_ALimitsBuiltWithoutTheMember_CompilesAndReadsAsTheDefault()
    {
        // The 14-argument form of sq-38 still compiles: the member is trailing and optional, and null reads as the default.
        var built = new Limits(10000, 100000, 64, 53312, 1000, 1000, 65536, 100000, 64, new CharsetTolerance(8, 65536), 10000, new Budget(10000000, 2000000, 16000000), 1, 1);
        Assert.Null(built.NullSpellings);
        var descriptor = TestModels.Minimal() with { Limits = built };
        Assert.Equal((64L, 256L), PublishedBounds(descriptor));
        Assert.Equal(
            WrittenLimits(TestModels.Minimal() with { Limits = built with { NullSpellings = NullSpellingLimits.Default } }).GetRawText(),
            WrittenLimits(descriptor).GetRawText());
    }

    [Fact]
    public void Record_TheLimitsOfAProfiledTable_HoldTheDefaultBounds()
    {
        var descriptor = ProfileText("t.csv", "a,b\n1,2\n3,4\n");
        Assert.Equal(NullSpellingLimits.Default, descriptor.Limits.NullSpellings ?? NullSpellingLimits.Default);
        Assert.Equal((64L, 256L), PublishedBounds(descriptor));
    }

    // ---- Enforced -------------------------------------------------------------------------------------------------------

    private static string Spelled(int spaces)
    {
        return "\"" + new string(' ', spaces) + "NA\"";
    }

    [Fact]
    public void Enforced_AFieldKeepsThePublishedNumberOfSpellings_AndCountsTheNextOneInOther()
    {
        var (max, _) = PublishedBounds(ProfileText("t.csv", "a,b\n1,2\n"));
        var text = new StringBuilder("A(id),V\n");
        for (var i = 0; i <= max; i++)
        {
            // max + 1 distinct spellings of one null, each of a length under the character bound.
            text.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Spelled(i)).Append('\n');
        }

        var descriptor = ProfileText("t.csv", text.ToString());
        var field = descriptor.Table!.Fields.Single(static f => f.Name == "V");
        Assert.Equal(max, field.NullSpellings!.Count);
        Assert.Equal(1, field.NullSpellingsOther);
        Assert.Equal(max + 1, field.Nulls);
        Assert.Equal(field.Nulls, field.NullSpellings.Sum(static e => e.Value) + field.NullSpellingsOther);
    }

    [Fact]
    public void Enforced_AFieldKeepsASpellingOfThePublishedLength_AndCountsOneCharacterMoreInOther()
    {
        var (_, maxChars) = PublishedBounds(ProfileText("t.csv", "a,b\n1,2\n"));
        var atBound = Spelled((int)maxChars - 2);
        var past = Spelled((int)maxChars - 1);
        var descriptor = ProfileText("t.csv", "A(id),V\n1," + atBound + "\n2," + past + "\n3," + past + "\n");
        var field = descriptor.Table!.Fields.Single(static f => f.Name == "V");
        var kept = Assert.Single(field.NullSpellings!);
        Assert.Equal(maxChars, kept.Key.Length);
        Assert.Equal(2, field.NullSpellingsOther);
    }

    [Fact]
    public void Enforced_TheAllNullRowSpellings_KeepTheSameBounds()
    {
        var (max, maxChars) = PublishedBounds(ProfileText("t.csv", "a,b\n1,2\n"));
        var text = new StringBuilder("A(id),B\n");
        for (var i = 0; i <= max; i++)
        {
            text.Append("1,x\n").Append(Spelled(i)).Append(',').Append(Spelled(i)).Append('\n');
        }

        // One more row of a spelling one character past the length bound.
        text.Append(Spelled((int)maxChars - 1)).Append(',').Append(Spelled((int)maxChars - 1)).Append('\n');
        var rowClasses = ProfileText("t.csv", text.ToString()).Table!.RowClasses!;
        Assert.Equal(max + 2, rowClasses.AllNullRows);
        Assert.Equal(max, rowClasses.AllNullRowSpellings!.Count);
        Assert.Equal(2, rowClasses.AllNullRowSpellingsOther);
        Assert.Equal(0, rowClasses.AllNullRowSpellingsMixed);
    }

    // ---- Documented -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Documented_DescriptorMd_DescribesTheObject_InTheLimitsSection_TheKeyOrderAndTheSubset()
    {
        var descriptor = DocsText("descriptor.md");

        var limits = Section(descriptor, "## `limits`");
        Assert.Contains("\"nullSpellings\": { \"max\": 64, \"maxChars\": 256, \"maxDistinctOther\": 64 }", limits, StringComparison.Ordinal);
        Assert.Contains("`nullSpellings` (sq-42) is **always** written", limits, StringComparison.Ordinal);
        Assert.Contains("`maxChars` (256)", limits, StringComparison.Ordinal);

        var keyOrder = Section(descriptor, "### Key order of nested objects");
        var limitsRow = keyOrder.Split('\n').Single(static l => l.StartsWith("| `limits` |", StringComparison.Ordinal));
        Assert.Matches("`sketches`[^|]*`nullSpellings`[^|]*`nullTokens`", limitsRow);
        Assert.Contains("| `limits.nullSpellings` | `max`, `maxChars`, `maxDistinctOther` |", keyOrder, StringComparison.Ordinal);

        var subset = Section(descriptor, "## Consumer subset");
        Assert.Contains("| `limits.nullSpellings.max` | S |", subset, StringComparison.Ordinal);
        Assert.Contains("| `limits.nullSpellings.maxChars` | S |", subset, StringComparison.Ordinal);

        var policy = Section(descriptor, "## Versioning policy");
        Assert.Contains("sq-42 adds, again without a bump: `limits.nullSpellings`", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void Documented_TheSentenceThatTheBoundsAreNotInLimits_IsGone()
    {
        foreach (var file in new[] { "descriptor.md", "design.md" })
        {
            var text = DocsText(file);
            Assert.DoesNotContain("not in `limits`", text, StringComparison.Ordinal);
            Assert.DoesNotContain("fixed; not in", text, StringComparison.Ordinal);
        }

        Assert.Contains("`limits.nullSpellings`", DocsText("design.md"), StringComparison.Ordinal);
    }
}
