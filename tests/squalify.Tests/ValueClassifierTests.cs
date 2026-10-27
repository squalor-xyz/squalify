// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using squalor.squalify;

namespace squalor.squalify.Tests;

public class ValueClassifierTests
{
    private static T UnderCulture<T>(string name, Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static ValueClass Classify(string value, char separator = '.')
    {
        return ValueClassifier.Classify(value, separator);
    }

    [Theory]
    // Null vocabulary, case-insensitive, trimmed.
    [InlineData("", '.', "Null", "None")]
    [InlineData("   ", '.', "Null", "None")]
    [InlineData("NA", '.', "Null", "None")]
    [InlineData("na", '.', "Null", "None")]
    [InlineData("NA.", '.', "Null", "None")]
    [InlineData(" N/A ", '.', "Null", "None")]
    [InlineData("n/a", '.', "Null", "None")]
    [InlineData("null", '.', "Null", "None")]
    [InlineData("NULL", '.', "Null", "None")]
    [InlineData("NaN", '.', "Null", "None")]
    [InlineData("nan", '.', "Null", "None")]
    [InlineData("-", '.', "Null", "None")]
    // Booleans are only true, false, yes and no.
    [InlineData("true", '.', "Bool", "None")]
    [InlineData("TRUE", '.', "Bool", "None")]
    [InlineData("False", '.', "Bool", "None")]
    [InlineData("yes", '.', "Bool", "None")]
    [InlineData("No", '.', "Bool", "None")]
    [InlineData("y", '.', "String", "None")]
    [InlineData("on", '.', "String", "None")]
    [InlineData("t", '.', "String", "None")]
    // Integers.
    [InlineData("0", '.', "Int", "None")]
    [InlineData("42", '.', "Int", "None")]
    [InlineData("-17", '.', "Int", "None")]
    [InlineData("+5", '.', "Int", "None")]
    [InlineData("007", '.', "Int", "LeadingZero")]
    [InlineData("-007", '.', "Int", "LeadingZero")]
    [InlineData("9223372036854775807", '.', "Int", "None")]
    [InlineData("-9223372036854775808", '.', "Int", "None")]
    [InlineData("9223372036854775808", '.', "Float", "None")]
    // Floats.
    [InlineData("1.5", '.', "Float", "None")]
    [InlineData("-0.25", '.', "Float", "None")]
    [InlineData(".5", '.', "Float", "None")]
    [InlineData("5.", '.', "Float", "None")]
    [InlineData("1e5", '.', "Float", "None")]
    [InlineData("1E-3", '.', "Float", "None")]
    [InlineData("+1.5e+3", '.', "Float", "None")]
    [InlineData("00.5", '.', "Float", "LeadingZero")]
    [InlineData("1e999", '.', "String", "None")]
    [InlineData("Infinity", '.', "String", "None")]
    [InlineData("-Infinity", '.', "String", "None")]
    // Decimal comma: 1,5 is a string under '.', a float under ','.
    [InlineData("1,5", '.', "String", "CommaDecimalLike")]
    [InlineData("1,5", ',', "Float", "CommaDecimalLike")]
    [InlineData("-1,5", ',', "Float", "CommaDecimalLike")]
    [InlineData("1,5e3", ',', "Float", "None")]
    [InlineData("1.5", ',', "String", "None")]
    [InlineData("1.234,5", ',', "String", "None")]
    [InlineData("1,000", '.', "String", "CommaDecimalLike")]
    [InlineData("12", ',', "Int", "None")]
    // Strings.
    [InlineData("abc", '.', "String", "None")]
    [InlineData("12abc", '.', "String", "None")]
    [InlineData("1 2", '.', "String", "None")]
    [InlineData("0x10", '.', "String", "None")]
    [InlineData("1_000", '.', "String", "None")]
    [InlineData("--", '.', "String", "None")]
    [InlineData("+", '.', "String", "None")]
    [InlineData("1.2.3", '.', "String", "None")]
    [InlineData("١٢٣", '.', "String", "None")]
    [InlineData("5\0", '.', "String", "None")]
    // leadingZero belongs to int and float votes only.
    [InlineData("01/02/2026", '.', "Date", "None")]
    [InlineData("09:05", '.', "Time", "None")]
    [InlineData("007abc", '.', "String", "None")]
    [InlineData("007", ',', "Int", "LeadingZero")]
    [InlineData("007,5", ',', "Float", "LeadingZero, CommaDecimalLike")]
    // Trimming uses a fixed set: tab, line breaks, space and NBSP.
    [InlineData("\u00a0NA\u00a0", '.', "Null", "None")]
    [InlineData("\t12\r\n", '.', "Int", "None")]
    [InlineData("\u00a01.5\u00a0", '.', "Float", "None")]
    [InlineData("\u2028NA", '.', "String", "None")]
    [InlineData("NA\u2028", '.', "String", "None")]
    [InlineData("\u3000NA", '.', "String", "None")]
    [InlineData("\u30001", '.', "String", "None")]
    [InlineData("\u20031", '.', "String", "None")]
    public void ValueClassifier_Theory(string value, char separator, string vote, string flags)
    {
        var result = UnderCulture("tr-TR", () => ValueClassifier.Classify(value, separator));

        Assert.Equal(vote, result.Vote.ToString());
        Assert.Equal(flags, result.Flags.ToString());
    }

    [Theory]
    [InlineData("2026-03-17", "YMD")]
    [InlineData("0001-01-01", "YMD")]
    [InlineData("2026-3-7", "YMD")]
    [InlineData("2026/03/17", "YMD")]
    [InlineData("2026.03.17", "YMD")]
    [InlineData("6/5/2026", "DMY,MDY")]
    [InlineData("12/12/2026", "DMY,MDY")]
    [InlineData("3/17/2026", "MDY")]
    [InlineData("17.3.2026", "DMY")]
    [InlineData("17-03-2026", "DMY")]
    [InlineData("2/29/2024", "MDY")]
    [InlineData("29/2/2024", "DMY")]
    [InlineData("2/29/2000", "MDY")]
    public void Date_Orders_Theory(string value, string orders)
    {
        var result = UnderCulture("tr-TR", () => Classify(value));

        Assert.Equal("Date", result.Vote.ToString());
        Assert.Equal(orders, string.Join(',', result.Orders.Names()));
        Assert.Equal(ClockKind.None, result.Clock);
    }

    [Theory]
    [InlineData("2/30/2026")]
    [InlineData("2/29/2026")]
    [InlineData("2/29/1900")]
    [InlineData("2026-13-01")]
    [InlineData("2026-02-30")]
    [InlineData("0000-01-01")]
    [InlineData("13/13/2026")]
    [InlineData("1/2/26")]
    [InlineData("6/5/26")]
    [InlineData("2026-03/17")]
    [InlineData("3/17/12345")]
    [InlineData("1/2026")]
    [InlineData("1/2/3/2026")]
    [InlineData("1/2026/3")]
    public void Date_Invalid_IsString_WithNoOrders(string value)
    {
        var result = UnderCulture("tr-TR", () => Classify(value));

        Assert.Equal(ValueVote.String, result.Vote);
        Assert.Equal(DateOrders.None, result.Orders);
        Assert.Null(result.Pattern);
    }

    [Fact]
    public void DateTime_TwelveHourClock()
    {
        var result = UnderCulture("tr-TR", () => Classify("10/8/2026 11:00:22 AM"));

        Assert.Equal(ValueVote.DateTime, result.Vote);
        Assert.Equal(ClockKind.H12, result.Clock);
        Assert.Equal(["DMY", "MDY"], result.Orders.Names());

        var bad = UnderCulture("tr-TR", () => Classify("13:00 PM"));
        Assert.Equal(ValueVote.String, bad.Vote);
        Assert.Equal(ClockKind.None, bad.Clock);
    }

    [Theory]
    [InlineData("11:00:22", "Time", "H24", "HH:mm:ss")]
    [InlineData("9:05", "Time", "H24", "H:mm")]
    [InlineData("0:00", "Time", "H24", "H:mm")]
    [InlineData("23:59:59.250", "Time", "H24", "HH:mm:ss.f+")]
    [InlineData("12:00 AM", "Time", "H12", "hh:mm tt")]
    [InlineData("12:59 PM", "Time", "H12", "hh:mm tt")]
    [InlineData("1:00 AM", "Time", "H12", "h:mm tt")]
    [InlineData("0:30 AM", "String", "None", "")]
    [InlineData("00:00 PM", "String", "None", "")]
    [InlineData("1:05pm", "Time", "H12", "h:mmtt")]
    [InlineData("1:05 pm", "Time", "H12", "h:mm tt")]
    [InlineData("12:30:15.5 PM", "Time", "H12", "hh:mm:ss.f+ tt")]
    [InlineData("13:00 PM", "String", "None", "")]
    [InlineData("13:00 AM", "String", "None", "")]
    [InlineData("24:00", "String", "None", "")]
    [InlineData("10:60", "String", "None", "")]
    [InlineData("10:00:60", "String", "None", "")]
    [InlineData("7:5", "String", "None", "")]
    [InlineData("10:00:", "String", "None", "")]
    [InlineData("10:00.5", "String", "None", "")]
    [InlineData("10:00:00.", "String", "None", "")]
    [InlineData("10:00 XM", "String", "None", "")]
    [InlineData("10:00 上午", "String", "None", "")]
    [InlineData("10:00 ＡＭ", "String", "None", "")]
    [InlineData("10/8/2026 11:00:22 AM", "DateTime", "H12", "nn/n/yyyy hh:mm:ss tt")]
    [InlineData("10/8/2026 11:00:22", "DateTime", "H24", "nn/n/yyyy HH:mm:ss")]
    [InlineData("3/17/2026 9:05", "DateTime", "H24", "n/nn/yyyy H:mm")]
    [InlineData("2026-03-17T10:00", "DateTime", "H24", "yyyy-nn-nnTHH:mm")]
    [InlineData("2026-03-17T10:00:00", "DateTime", "H24", "yyyy-nn-nnTHH:mm:ss")]
    [InlineData("2026-03-17T10:00:00Z", "DateTime", "H24", "yyyy-nn-nnTHH:mm:ssZ")]
    [InlineData("2026-03-17T10:00:00.123456+01:00", "DateTime", "H24", "yyyy-nn-nnTHH:mm:ss.f+zzz")]
    [InlineData("2026-03-17 10:00:00-0530", "DateTime", "H24", "yyyy-nn-nn HH:mm:sszzzz")]
    [InlineData("2026-03-17 10:00:00+01", "DateTime", "H24", "yyyy-nn-nn HH:mm:sszz")]
    [InlineData("2026-03-17 10:00", "DateTime", "H24", "yyyy-nn-nn HH:mm")]
    [InlineData("2026-03-17 10:00 PM", "DateTime", "H12", "yyyy-nn-nn hh:mm tt")]
    [InlineData("2026-03-17T9:00", "String", "None", "")]
    [InlineData("2026-03-17T10:00 PM", "String", "None", "")]
    [InlineData("2026-3-17T10:00", "String", "None", "")]
    [InlineData("2026-03-17T25:00", "String", "None", "")]
    [InlineData("2026-02-30T10:00", "String", "None", "")]
    [InlineData("2026-03-17T10:00:00+25:00", "String", "None", "")]
    [InlineData("2026-03-17T10:00:00+01:60", "String", "None", "")]
    [InlineData("10/8/2026 13:00 PM", "String", "None", "")]
    [InlineData("10/8/2026 0:30 AM", "String", "None", "")]
    [InlineData("2026-03-17 00:00 PM", "String", "None", "")]
    [InlineData("10/8/2026  11:00", "String", "None", "")]
    [InlineData("10/8/2026T11:00", "String", "None", "")]
    [InlineData("10/8/2026 11:00:22 上午", "String", "None", "")]
    [InlineData("10/8/2026 11:00Z", "String", "None", "")]
    public void Time_Clock_AndPatterns(string value, string vote, string clock, string pattern)
    {
        var result = UnderCulture("tr-TR", () => Classify(value));

        Assert.Equal(vote, result.Vote.ToString());
        Assert.Equal(clock, result.Clock.ToString());
        Assert.Equal(pattern.Length == 0 ? null : pattern, result.Pattern);
    }

    [Theory]
    [InlineData("2026-03-17", "yyyy-nn-nn")]
    [InlineData("6/5/2026", "n/n/yyyy")]
    [InlineData("17.3.2026", "nn.n.yyyy")]
    [InlineData("03-17-2026", "nn-nn-yyyy")]
    public void Date_Pattern_IsOrderNeutral(string value, string pattern)
    {
        Assert.Equal(pattern, Classify(value).Pattern);
    }

    [Fact]
    public void DateTime_Orders_FollowTheDatePart()
    {
        Assert.Equal(["YMD"], Classify("2026-03-17T10:00:00").Orders.Names());
        Assert.Equal(["MDY"], Classify("3/17/2026 9:05").Orders.Names());
        Assert.Equal(["DMY"], Classify("17.3.2026 9:05").Orders.Names());
    }

    [Theory]
    [InlineData("12")]
    [InlineData("2026-03-17")]
    [InlineData("1.5")]
    [InlineData("abc")]
    public void NonNullValues_HaveNoNullToken(string value)
    {
        var result = Classify(value);

        Assert.NotEqual(ValueVote.Null, result.Vote);
        Assert.Null(result.NullToken);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("NA", "NA")]
    [InlineData("na", "NA")]
    [InlineData("NA.", "NA.")]
    [InlineData(" na. ", "NA.")]
    [InlineData("N/A", "N/A")]
    [InlineData("n/a", "N/A")]
    [InlineData("null", "null")]
    [InlineData("NULL", "null")]
    [InlineData("NaN", "NaN")]
    [InlineData("NAN", "NaN")]
    [InlineData("-", "-")]
    public void Null_ReportsTheMatchedVocabularyEntry(string value, string entry)
    {
        var result = UnderCulture("tr-TR", () => Classify(value));

        Assert.Equal(ValueVote.Null, result.Vote);
        Assert.Equal(entry, result.NullToken);
        Assert.Contains(result.NullToken, ValueClassifier.NullVocabulary);
    }

    [Theory]
    [InlineData("NAx")]
    [InlineData("--")]
    [InlineData("N A")]
    [InlineData("nul")]
    [InlineData("NA..")]
    public void NearMisses_AreNotNull(string value)
    {
        var result = Classify(value);

        Assert.Equal(ValueVote.String, result.Vote);
        Assert.Null(result.NullToken);
    }

    [Fact]
    public void TrimChars_AreAFixedSet_AndTrimUsesThem()
    {
        Assert.Equal("\t\n\v\f\r \u00a0", ValueClassifier.TrimChars);
        Assert.Equal("a b", ValueClassifier.Trim("\u00a0\t a b\v\f\u00a0 ").ToString());
        Assert.Equal("\u2028a\u3000", ValueClassifier.Trim("\u2028a\u3000").ToString());
        Assert.Equal("\u0085a", ValueClassifier.Trim("\u0085a").ToString());
    }

    [Fact]
    public void NullVocabulary_IsFixed_AndVersioned()
    {
        Assert.Equal(["", "NA", "NA.", "N/A", "null", "NaN", "-"], ValueClassifier.NullVocabulary);
        Assert.Equal(1, ValueClassifier.NullVocabularyVersion);
        Assert.Equal(1, Limits.Default.NullTokens);
    }

    [Fact]
    public void Classify_DoesNotDependOnTheCurrentCulture()
    {
        string[] values =
        [
            "1.5", "1,5", "1e3", "007", "NULL", "TRUE", "10/8/2026 11:00:22 AM", "2026-03-17T10:00:00Z", "13:00 PM", "-",
        ];

        foreach (var separator in new[] { '.', ',' })
        {
            var expected = values.Select(v => ValueClassifier.Classify(v, separator)).ToArray();
            foreach (var culture in new[] { "tr-TR", "de-DE", "fr-FR", "ar-SA", "th-TH" })
            {
                var actual = UnderCulture(culture, () => values.Select(v => ValueClassifier.Classify(v, separator)).ToArray());
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void Classify_RejectsAnUnsupportedDecimalSeparator()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ValueClassifier.Classify("1", ';'));
    }
}
