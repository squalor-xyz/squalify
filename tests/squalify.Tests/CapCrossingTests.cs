// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using squalor.squalify;

namespace squalor.squalify.Tests;

public sealed class CapCrossingTests
{
    private const long Cap = 50;
    private static readonly FieldCaps Caps = new(Cap, Cap, 64, 1_000_000);

    public static TheoryData<string, long, bool> Boundaries()
    {
        var data = new TheoryData<string, long, bool>();
        foreach (var kind in new[] { "int", "float", "date", "dateAmbiguous", "string" })
        {
            data.Add(kind, Cap - 1, false);
            data.Add(kind, Cap, false);
            data.Add(kind, Cap + 1, true);
        }

        return data;
    }

    private static string Value(string kind, long i)
    {
        return kind switch
        {
            "int" => (1000 + i).ToString(CultureInfo.InvariantCulture),
            "float" => (1000 + i).ToString(CultureInfo.InvariantCulture) + ".5",
            "date" => new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            // Day 1..12 and month 1 (read either way) of a growing year: DMY and MDY both stay possible.
            "dateAmbiguous" => ((i % 12) + 1).ToString(CultureInfo.InvariantCulture) + "/01/" + (2000 + (i / 12)).ToString(CultureInfo.InvariantCulture),
            _ => "value-" + i.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static FieldProfile Profile(string kind, long count)
    {
        var accumulator = new FieldAccumulator(Caps);
        for (long i = 0; i < count; i++)
        {
            accumulator.AddText(Value(kind, i));
        }

        return accumulator.Build(0, "f", null, 0);
    }

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void CapCrossing_Boundary(string kind, long count, bool approximate)
    {
        var field = Profile(kind, count);

        // Distinct: exact at the cap, a sketch one past it. Linear counting of so few values is exact.
        Assert.Equal(approximate, field.Distinct!.Approximate);
        Assert.Equal(count, field.Distinct.Value);
        Assert.Equal(approximate, field.Distinct.ErrorBound is not null);

        // Quantiles of the typed fields. A sketch that has seen only 51 values has compressed nothing, so its
        // quantiles are the exact lower nearest ranks.
        if (kind == "int")
        {
            var stats = field.Numeric!;
            Assert.Equal(approximate, stats.Approximate);
            Assert.Equal(approximate, stats.ErrorBound is not null);
            Assert.Equal(DescriptorNumber.Int(1000 + (((50 * count) + 99) / 100) - 1), stats.Quantiles.P50);
            Assert.Equal(DescriptorNumber.Int(1000), stats.Quantiles.P01);
            Assert.Equal(DescriptorNumber.Int(1000 + (((99 * count) + 99) / 100) - 1), stats.Quantiles.P99);
        }
        else if (kind == "float")
        {
            var stats = field.Numeric!;
            Assert.Equal(approximate, stats.Approximate);
            Assert.Equal(1000.5, stats.Quantiles.P01.RealValue);
        }
        else if (kind == "date")
        {
            var stats = field.Temporal!;
            Assert.Equal(approximate, stats.Approximate);
            Assert.Equal(approximate, stats.ErrorBound is not null);
            Assert.Equal("2020-01-01", stats.Quantiles!.P01);
        }
        else if (kind == "dateAmbiguous")
        {
            var stats = field.Temporal!;
            Assert.True(stats.Ambiguous);
            Assert.Null(stats.Quantiles);
        }
        else
        {
            Assert.Null(field.Numeric);
            Assert.Null(field.Temporal);
        }
    }

    [Fact]
    public void CapCrossing_TemporalWithTwoPossibleOrders_KeepsOneSketchPerOrder()
    {
        // 1/01/2000 .. 12/01/2000 and so on: both DMY and MDY stay possible through the cap, and a later value that
        // only reads as day-month-year settles the order after the sketches exist.
        var accumulator = new FieldAccumulator(Caps);
        for (long i = 0; i < Cap + 20; i++)
        {
            accumulator.AddText(Value("dateAmbiguous", i));
        }

        accumulator.AddText("25/01/2030");
        var field = accumulator.Build(0, "f", null, 0);
        var stats = field.Temporal!;
        Assert.Equal(["DMY"], stats.Orders);
        Assert.False(stats.Ambiguous);
        Assert.True(stats.Approximate);

        // Read as day-month-year the values are 2000-01-01 (1/01/2000) first and 2030-01-25 last.
        Assert.Equal("2000-01-01", stats.Min);
        Assert.Equal("2030-01-25", stats.Max);
        Assert.Equal("2000-01-01", stats.Quantiles!.P01);
        Assert.Equal("2030-01-25", stats.Quantiles.P99);

        // The same values read as month-day-year would put 12/01/2000 (December 1) before 1/01/2001, and DMY does not:
        // the sketch kept for DMY orders by day-month-year ticks, not by the temporal code.
        var mdy = new FieldAccumulator(Caps);
        for (long i = 0; i < Cap + 20; i++)
        {
            mdy.AddText(Value("dateAmbiguous", i));
        }

        mdy.AddText("01/25/2030");
        var mdyStats = mdy.Build(0, "f", null, 0).Temporal!;
        Assert.Equal(["MDY"], mdyStats.Orders);
        Assert.Equal("2000-01-01", mdyStats.Min);
        Assert.Equal("2030-01-25", mdyStats.Max);
    }

    [Fact]
    public void CapCrossing_IntegersThenAFloat_ConvertTheSketch()
    {
        var accumulator = new FieldAccumulator(Caps);
        for (long i = 0; i < Cap + 10; i++)
        {
            accumulator.AddText((1000 + i).ToString(CultureInfo.InvariantCulture));
        }

        accumulator.AddText("2000.5");
        var field = accumulator.Build(0, "f", null, 0);
        Assert.Equal("float", field.Type);
        var stats = field.Numeric!;
        Assert.True(stats.Approximate);
        Assert.False(stats.Quantiles.P01.IsInteger);
        Assert.Equal(1000.0, stats.Quantiles.P01.RealValue);
        Assert.Equal(2000.5, stats.Quantiles.P99.RealValue);
    }

    [Fact]
    public void Approximate_FlaggedWithErrorBound()
    {
        var limits = ProfileLimits.Default with { DistinctPerField = 3, QuantilePerField = 3 };
        var builder = new StringBuilder("id,n,d,s\n");
        for (var i = 0; i < 40; i++)
        {
            builder.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append((i * 3).ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append("2026-01-").Append((1 + (i % 28)).ToString("D2", CultureInfo.InvariantCulture)).Append(',')
                .Append("v").Append((i % 5).ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        var descriptor = Profiler.Profile(new BytesSource("t.csv", Encoding.UTF8.GetBytes(builder.ToString())), ProfileOptions.Create(false, []), limits);
        var fields = descriptor.Table!.Fields;

        // Distinct: 40 ids (sketched), 40 numbers (sketched), 28 dates (sketched), 5 strings (sketched as well: the cap is 3).
        foreach (var field in fields)
        {
            Assert.True(field.Distinct!.Approximate, field.Name);
            Assert.Equal(1.04 / 128.0, field.Distinct.ErrorBound);
        }

        Assert.Equal(40, fields[0].Distinct!.Value);
        Assert.Equal(5, fields[3].Distinct!.Value);
        Assert.True(fields[1].Numeric!.Approximate);
        Assert.Equal(0.001, fields[1].Numeric!.ErrorBound);
        Assert.True(fields[2].Temporal!.Approximate);
        Assert.Equal(0.001, fields[2].Temporal!.ErrorBound);

        using var json = JsonDocument.Parse(DescriptorWriter.ToBytes(descriptor));
        var written = json.RootElement.GetProperty("table").GetProperty("fields");
        var distinct = written[1].GetProperty("distinct");
        Assert.True(distinct.GetProperty("approximate").GetBoolean());
        Assert.Equal(0.008125, distinct.GetProperty("errorBound").GetDouble());
        Assert.False(distinct.TryGetProperty("exceeds", out _));
        var numeric = written[1].GetProperty("numeric");
        Assert.True(numeric.GetProperty("approximate").GetBoolean());
        Assert.Equal(0.001, numeric.GetProperty("errorBound").GetDouble());
        Assert.Equal(JsonValueKind.Object, numeric.GetProperty("quantiles").ValueKind);
        Assert.False(numeric.TryGetProperty("exceeds", out _));
        var temporal = written[2].GetProperty("temporal");
        Assert.True(temporal.GetProperty("approximate").GetBoolean());
        Assert.Equal(0.001, temporal.GetProperty("errorBound").GetDouble());
        Assert.False(temporal.TryGetProperty("exceeds", out _));

        // Under the caps nothing is flagged, and there is no error bound to write.
        var exact = Profiler.Profile(new BytesSource("t.csv", Encoding.UTF8.GetBytes("id,n\n1,2\n2,3\n3,4\n")), ProfileOptions.Create(false, []));
        using var exactJson = JsonDocument.Parse(DescriptorWriter.ToBytes(exact));
        var exactField = exactJson.RootElement.GetProperty("table").GetProperty("fields")[1];
        Assert.False(exactField.GetProperty("distinct").GetProperty("approximate").GetBoolean());
        Assert.False(exactField.GetProperty("distinct").TryGetProperty("errorBound", out _));
        Assert.False(exactField.GetProperty("numeric").GetProperty("approximate").GetBoolean());
        Assert.False(exactField.GetProperty("numeric").TryGetProperty("errorBound", out _));
    }
}
