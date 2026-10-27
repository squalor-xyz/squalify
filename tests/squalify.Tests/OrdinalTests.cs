// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using squalor.squalify;

namespace squalor.squalify.Tests;

public class OrdinalTests
{
    [Fact]
    public void Sorted_UsesUtf16CodeUnitOrder_NotCultureOrder()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            // Culture-aware sorting puts "a" before "B" and "ae" before "ä"; ordinal puts upper case first.
            Assert.Equal(["B", "a", "ae", "ä"], Ordinal.Sorted(["a", "ä", "B", "ae"]));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void SortedBy_IsStable_ForEqualKeys()
    {
        var items = new[] { ("b", 1), ("a", 2), ("b", 0), ("a", 1) };
        var sorted = Ordinal.SortedBy(items, static i => i.Item1);
        Assert.Equal([("a", 2), ("a", 1), ("b", 1), ("b", 0)], sorted);
    }

    [Fact]
    public void AsciiFold_IsInvariant_UnderTurkishCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("title", Ordinal.AsciiFold("TITLE"));
            Assert.Equal("i", Ordinal.AsciiFold("I"));
            Assert.Equal("i\u0130", Ordinal.AsciiFold("I\u0130"));
            Assert.Equal("title", Ordinal.AsciiFold("TITLE".AsSpan()));
            Assert.Equal("i\u0130", Ordinal.AsciiFold("I\u0130".AsSpan()));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AsciiFold_SpanAndString_AgreeAndHandleEmpty()
    {
        const string text = "AbC-xYz 09 \u00C9\u00E9";
        Assert.Equal(Ordinal.AsciiFold(text), Ordinal.AsciiFold(text.AsSpan()));
        Assert.Equal("abc-xyz 09 \u00C9\u00E9", Ordinal.AsciiFold(text.AsSpan()));
        Assert.Equal(string.Empty, Ordinal.AsciiFold(ReadOnlySpan<char>.Empty));
    }

    [Fact]
    public void Compare_IsOrdinal()
    {
        Assert.True(Ordinal.Compare("Z", "a") < 0);
        Assert.True(Ordinal.Compare("a", "a") == 0);
    }

    [Fact]
    public void Invariant_FormatsWithoutGrouping()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("-1234567", Ordinal.Invariant(-1234567));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void CountMap_SortsOrdinally_AndNumericKeysNumerically()
    {
        var ordinal = CountMap.FromOrdinal([new("b", 1), new("B", 2), new("a", 3)]);
        Assert.Equal(["B", "a", "b"], ordinal.Select(static e => e.Key));

        var numeric = CountMap.FromNumeric([new(10, 1), new(2, 1), new(9, 1)]);
        Assert.Equal(["2", "9", "10"], numeric.Select(static e => e.Key));

        Assert.Throws<ArgumentException>(() => CountMap.FromOrdinal([new("a", 1), new("a", 2)]));
        Assert.Throws<ArgumentException>(() => CountMap.FromNumeric([new(1, 1), new(1, 2)]));
    }

    [Fact]
    public void ProfileOptions_Create_SortsAllowList()
    {
        var options = ProfileOptions.Create(false, ["b", "B", "a"]);
        Assert.Equal(["B", "a", "b"], options.IncludeValuesFor);
    }

    [Fact]
    public void ProfileOptions_Create_DropsExactDuplicates_ButKeepsNamesThatDifferInCase()
    {
        var options = ProfileOptions.Create(false, ["b", "a", "b", "B", "a"]);
        Assert.Equal(["B", "a", "b"], options.IncludeValuesFor);
    }
}
