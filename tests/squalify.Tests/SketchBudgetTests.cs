// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Text;
using System.Text.Json;
using squalor.squalify;
using Xunit.Abstractions;

namespace squalor.squalify.Tests;

/// <summary>sq-38: the per-field sketches are scaled by field count so that all of them fit the memory allowance.</summary>
public sealed class SketchBudgetTests(ITestOutputHelper output)
{
    private const int WideFields = 2_000;

    // The default caps: at 2,000 fields the budget division leaves 5,000 quantile values and 1,000 distinct values per
    // field, so 5,200 rows cross both in every field.
    private const int Rows = 5_200;

    // Five kinds of column, in rotation: an integer, a float, a string id, an ISO date and an ambiguous date (day and
    // month both 1..12, so day-month-year and month-day-year both stay possible). Cells have a fixed width, and every
    // column has thousands of distinct values, so every field crosses its distinct cap and, but the string, its quantile cap.
    private static readonly int[] Widths = [9, 10, 13, 10, 10];

    // The wide profile is made once and shared by the tests that read it (it takes several seconds).
    private static readonly Lazy<(Descriptor Descriptor, long FinalBytes)> Wide = new(() => Run(WideFields, ProfileLimits.Default));

    // The sum of the estimated bytes of every accumulator at the last sample of the pass, when every field is past its
    // caps: the exact buffers are freed and only the sketches (and the fixed share of each field) remain. The peak
    // before that is the exact values, which the exact-value budgets bound (MemoryBudget.Effective), not the sketches.
    private static (Descriptor Descriptor, long FinalBytes) Run(int columns, ProfileLimits limits)
    {
        long last = 0;
        var trace = new MemoryTrace { Observer = bytes => last = bytes };
        var descriptor = Profiler.Profile(new KindsSource(columns, Rows), ProfileOptions.Create(false, []), limits, trace);
        return (descriptor, last);
    }

    [Fact]
    public void WideStream_EveryFieldPastItsCaps_FitsTheAllowance()
    {
        var (descriptor, finalBytes) = Wide.Value;
        var fields = descriptor.Table!.Fields;
        Assert.Equal(WideFields, fields.Count);

        // The generated stream does cross the caps in every field: a sketched distinct count, and sketched quantiles
        // for the numeric and the date columns.
        foreach (var field in fields)
        {
            Assert.True(field.Distinct!.Approximate, field.Name);
            Assert.True(field.Numeric?.Approximate ?? field.Temporal?.Approximate ?? true, field.Name);
        }

        Assert.True(fields[0].Numeric!.Approximate);
        Assert.True(fields[3].Temporal!.Approximate is true);
        Assert.True(fields[4].Temporal!.Ambiguous);

        // The summed estimate of every accumulator (and the row-class state) stays within the allowance.
        output.WriteLine($"{WideFields} fields past their caps: {finalBytes} estimated bytes, sketch budget {MemoryBudget.SketchBudgetBytes}");
        Assert.True(finalBytes <= MemoryBudget.SketchBudgetBytes, $"the summed estimate is {finalBytes} bytes, above the sketch budget of {MemoryBudget.SketchBudgetBytes}");
        Assert.True(finalBytes > MemoryBudget.SketchBudgetBytes / 4, $"the summed estimate is only {finalBytes} bytes: the sketches were not made");
    }

    [Fact]
    public void WideStream_ErrorMembers_MatchTheScaledPrecision()
    {
        var descriptor = Wide.Value.Descriptor;

        // 2,000 fields: the share of a field is 33,554 bytes, which the table in docs/design.md maps to precision 12
        // (4,096 registers, relative standard error 1.04 / 64) and epsilon 1 / 250.
        const double RelativeStdError = 1.04 / 64.0;
        const double RankError = 0.004;
        foreach (var field in descriptor.Table!.Fields)
        {
            Assert.Equal(RelativeStdError, field.Distinct!.RelativeStdError);
            if (field.Numeric is { Approximate: true } numeric)
            {
                Assert.Equal(RankError, numeric.RankError);
            }

            if (field.Temporal is { Approximate: true } temporal)
            {
                Assert.Equal(RankError, temporal.RankError);
            }
        }

        // The same values are recorded once in limits.sketches, next to the caps they go with.
        using var json = JsonDocument.Parse(DescriptorWriter.ToBytes(descriptor));
        var limits = json.RootElement.GetProperty("limits");
        Assert.True(limits.TryGetProperty("sketches", out var sketches), "limits.sketches is not written for a table of 2,000 fields");
        Assert.Equal(12, sketches.GetProperty("hllPrecision").GetInt32());
        Assert.Equal(RankError, sketches.GetProperty("rankError").GetDouble());
        Assert.Equal(MemoryBudget.SketchBudgetBytes, sketches.GetProperty("budgetBytes").GetInt64());

        // And in the written fields.
        var written = json.RootElement.GetProperty("table").GetProperty("fields");
        Assert.Equal(RelativeStdError, written[2].GetProperty("distinct").GetProperty("relativeStdError").GetDouble());
        Assert.Equal(RankError, written[0].GetProperty("numeric").GetProperty("rankError").GetDouble());
        Assert.Equal(RankError, written[3].GetProperty("temporal").GetProperty("rankError").GetDouble());
    }

    [Fact]
    public void NarrowStream_KeepsTheFullPrecision_AndWritesTheDefaultSketchLimits()
    {
        // 20 fields cross the same caps as the 2,000 above (1,000 distinct and 5,000 quantile values; the budget gives a
        // table of 100 fields or fewer 10,000 and 100,000, so they are set here): nothing is scaled, and every field
        // reports the full precision.
        var (descriptor, _) = Run(20, ProfileLimits.Default with { DistinctPerField = 1_000, QuantilePerField = 5_000 });
        foreach (var field in descriptor.Table!.Fields)
        {
            Assert.Equal(1.04 / 128.0, field.Distinct!.RelativeStdError);
            Assert.True(field.Numeric?.RankError is null or 0.001);
            Assert.True(field.Temporal?.RankError is null or 0.001);
        }

        Assert.Equal(0.001, descriptor.Table.Fields[0].Numeric!.RankError);
        Assert.Equal(0.001, descriptor.Table.Fields[3].Temporal!.RankError);
        AssertDefaultSketches(descriptor);
    }

    private static void AssertDefaultSketches(Descriptor descriptor)
    {
        using var json = JsonDocument.Parse(DescriptorWriter.ToBytes(descriptor));
        Assert.True(json.RootElement.GetProperty("limits").TryGetProperty("sketches", out var sketches), "limits.sketches is not written");
        Assert.Equal(14, sketches.GetProperty("hllPrecision").GetInt32());
        Assert.Equal(0.001, sketches.GetProperty("rankError").GetDouble());
        Assert.Equal(67_108_864L, sketches.GetProperty("budgetBytes").GetInt64());
    }

    [Fact]
    public void Sketches_AreWrittenForEveryDescriptor_NotOnlyForAWideTable()
    {
        // A narrow table, a text file, a binary file and an empty file all carry limits.sketches with the full precision.
        AssertDefaultSketches(Profiler.Profile(new BytesSource("t.csv", Encoding.ASCII.GetBytes("a,b\n1,2\n3,4\n")), ProfileOptions.Create(false, [])));
        AssertDefaultSketches(Profiler.Profile(new BytesSource("t.txt", Encoding.ASCII.GetBytes("just some prose\nand a second line\n")), ProfileOptions.Create(false, [])));
        AssertDefaultSketches(Profiler.Profile(new BytesSource("t.bin", [0, 1, 2, 3, 0xFF, 0xFE, 0, 0, 7, 9]), ProfileOptions.Create(false, [])));
        AssertDefaultSketches(Profiler.Profile(new BytesSource("t.dat", []), ProfileOptions.Create(false, [])));

        // A Limits built without the member reads as the full precision, and so does the default.
        AssertDefaultSketches(TestModels.FullDelimited());
        Assert.Equal(new SketchLimits(14, 0.001, 67_108_864), Limits.Default.Sketches);
    }

    [Theory]
    [InlineData(0, 14, 1_000)]
    [InlineData(1, 14, 1_000)]
    [InlineData(300, 14, 1_000)]
    [InlineData(547, 14, 1_000)]
    [InlineData(548, 13, 500)]
    [InlineData(1_077, 13, 500)]
    [InlineData(1_078, 12, 250)]
    [InlineData(2_000, 12, 250)]
    [InlineData(2_083, 12, 250)]
    [InlineData(2_084, 11, 125)]
    [InlineData(3_910, 11, 125)]
    [InlineData(3_911, 10, 64)]
    [InlineData(6_853, 10, 64)]
    [InlineData(6_854, 9, 32)]
    [InlineData(11_275, 9, 32)]
    [InlineData(11_276, 8, 16)]
    [InlineData(16_644, 8, 16)]
    [InlineData(16_645, 8, 16)]
    [InlineData(10_000_000, 8, 16)]
    public void SketchPrecision_IsAFunctionOfTheFieldCount_AtTheDocumentedBoundaries(long fields, int hllPrecision, int inverseEpsilon)
    {
        // The boundaries come from the table in docs/design.md: the nominal bytes of a field's sketches (a HyperLogLog of
        // 2^p + 64 bytes and two summaries of 52 / epsilon + 1,024) against 64 MiB divided by the field count.
        Assert.Equal((hllPrecision, inverseEpsilon), MemoryBudget.SketchPrecision(fields));

        var caps = MemoryBudget.Effective(Limits.Default, fields);
        Assert.Equal(hllPrecision, caps.HllPrecision);
        Assert.Equal(inverseEpsilon, caps.GkInverseEpsilon);
    }

    [Fact]
    public void SketchBudget_IsItsOwnConstant_AndTheLevelChoiceReadsIt()
    {
        // sq-38 owner decision 2026-10-29: the sketches have a budget of their own (64 MiB today), apart from the
        // allowance for read buffers and headroom that CeilingBytes adds.
        Assert.Equal(67_108_864L, MemoryBudget.SketchBudgetBytes);
        Assert.Equal(67_108_864L, MemoryBudget.AllowanceBytes);

        // The level choice reads the budget it is given, and the default is SketchBudgetBytes.
        Assert.Equal(MemoryBudget.SketchLevel(1_000), MemoryBudget.SketchLevel(1_000, MemoryBudget.SketchBudgetBytes));
        Assert.Equal(1, MemoryBudget.SketchLevel(1_000));
        Assert.Equal(0, MemoryBudget.SketchLevel(1_000, 2 * MemoryBudget.SketchBudgetBytes));
        Assert.Equal(6, MemoryBudget.SketchLevel(1_000, 1_000_000));
        Assert.Equal((12, 250), MemoryBudget.SketchPrecision(2_000, MemoryBudget.SketchBudgetBytes));
        Assert.Equal((14, 1_000), MemoryBudget.SketchPrecision(2_000, 10 * MemoryBudget.SketchBudgetBytes));

        // The recorded budget is the sketch budget.
        Assert.Equal(MemoryBudget.SketchBudgetBytes, MemoryBudget.Record(Limits.Default, MemoryBudget.Effective(Limits.Default, 2_000)).Sketches!.BudgetBytes);
    }

    [Fact]
    public void SketchBytesPerField_AtTheLevels_AreThePublishedFigures()
    {
        Assert.Equal(122_496, MemoryBudget.SketchBytesPerField(14, 1_000));
        Assert.Equal(62_304, MemoryBudget.SketchBytesPerField(13, 500));
        Assert.Equal(32_208, MemoryBudget.SketchBytesPerField(12, 250));
        Assert.Equal(17_160, MemoryBudget.SketchBytesPerField(11, 125));
        Assert.Equal(9_792, MemoryBudget.SketchBytesPerField(10, 64));
        Assert.Equal(5_952, MemoryBudget.SketchBytesPerField(9, 32));
        Assert.Equal(4_032, MemoryBudget.SketchBytesPerField(8, 16));
    }

    [Fact]
    public void SketchPrecision_NeverGetsFinerAsFieldsAreAdded_AndFitsWhereTheLevelsAllow()
    {
        var previous = (HyperLogLog.Precision, GreenwaldKhanna.InverseEpsilon);
        for (long fields = 1; fields <= 40_000; fields += 7)
        {
            var current = MemoryBudget.SketchPrecision(fields);
            Assert.True(current.HllPrecision <= previous.Item1 && current.GkInverseEpsilon <= previous.Item2, $"{fields} fields");
            previous = current;

            // Up to 16,644 fields the coarsest level still fits: past the floor the allowance is exceeded.
            if (fields <= 16_644)
            {
                Assert.True(fields * MemoryBudget.SketchBytesPerField(current.HllPrecision, current.GkInverseEpsilon) <= MemoryBudget.SketchBudgetBytes, $"{fields} fields");
            }
        }
    }

    [Fact]
    public void Record_WritesSketchLimits_ForEveryTable_AtTheScaledPrecision()
    {
        var narrow = MemoryBudget.Record(Limits.Default, MemoryBudget.Effective(Limits.Default, 547));
        Assert.Equal(SketchLimits.Default, narrow.Sketches);

        var wide = MemoryBudget.Record(Limits.Default, MemoryBudget.Effective(Limits.Default, 548));
        Assert.Equal(new SketchLimits(13, 0.002, MemoryBudget.SketchBudgetBytes), wide.Sketches);

        var wider = MemoryBudget.Record(Limits.Default, MemoryBudget.Effective(Limits.Default, 2_000));
        Assert.Equal(new SketchLimits(12, 0.004, MemoryBudget.SketchBudgetBytes), wider.Sketches);
        Assert.Equal(1_000, wider.DistinctPerField);
        Assert.Equal(5_000, wider.QuantilePerField);
    }

    [Fact]
    public void Accumulator_ReportsTheErrorOfItsOwnPrecision()
    {
        // 700 values: a distinct cap of 100 and a quantile cap of 200 are crossed by an integer, a date that reads two ways, and a string.
        var caps = new FieldCaps(100, 200, 64, 100_000, HllPrecision: 9, GkInverseEpsilon: 32);
        var ints = new FieldAccumulator(caps);
        var dates = new FieldAccumulator(caps);
        var ids = new FieldAccumulator(caps);
        for (var i = 0; i < 700; i++)
        {
            ints.AddText((1_000 + i).ToString(CultureInfo.InvariantCulture));
            dates.AddText(((i % 12) + 1).ToString(CultureInfo.InvariantCulture) + "/" + (((i / 12) % 12) + 1).ToString(CultureInfo.InvariantCulture) + "/" + (1990 + (i / 144)).ToString(CultureInfo.InvariantCulture));
            ids.AddText("id-" + i.ToString(CultureInfo.InvariantCulture));
        }

        var intField = ints.Build(0, "i", null, 0);
        var idField = ids.Build(0, "s", null, 0);
        Assert.Equal(1.04 / Math.Sqrt(512), intField.Distinct!.RelativeStdError);
        Assert.Equal(1.04 / Math.Sqrt(512), idField.Distinct!.RelativeStdError);
        Assert.Equal(1.0 / 32, intField.Numeric!.RankError);

        // The sketches really are of that precision, not only reported as such: a summary at 1 / 32 holds a few dozen
        // tuples where the default one holds 500 values (12 KB) and a buffer of 4 KB, and a HyperLogLog of 2^9 registers is 576 bytes.
        output.WriteLine($"scaled accumulators: ints {ints.EstimatedBytes}, ids {ids.EstimatedBytes}, dates {dates.EstimatedBytes}");
        Assert.True(ints.EstimatedBytes < 6_000, $"{ints.EstimatedBytes} bytes");
        Assert.True(ids.EstimatedBytes < 4_000, $"{ids.EstimatedBytes} bytes");

        // The date has two readings, so two summaries, and both are at the scaled precision; the field reads as ambiguous.
        var dateField = dates.Build(0, "d", null, 0);
        Assert.True(dateField.Temporal!.Ambiguous);
        Assert.Equal(1.04 / Math.Sqrt(512), dateField.Distinct!.RelativeStdError);
        Assert.True(dates.EstimatedBytes < 2 * 2_200 + HyperLogLog.BytesOf(9) + 3_000, $"{dates.EstimatedBytes} bytes");

        // A value that settles the order leaves one summary, and the error is the one of the scaled precision.
        for (var i = 0; i < 100; i++)
        {
            dates.AddText("25/03/2000");
        }

        Assert.Equal(1.0 / 32, dates.Build(0, "d", null, 0).Temporal!.RankError);
    }

    /// <summary>A stream of fixed-width rows made on the fly, so that 2,000 columns of thousands of rows need no memory.</summary>
    private sealed class KindsSource(int columns, int rows) : IProfileSource
    {
        private readonly byte[] _header = Encoding.ASCII.GetBytes(string.Join(',', Enumerable.Range(0, columns).Select(static c => "c" + c.ToString(CultureInfo.InvariantCulture))) + "\n");
        private readonly int _rowLength = Enumerable.Range(0, columns).Sum(static c => Widths[c % 5]) + columns;

        public string Name => "wide.csv";

        public long Length => _header.Length + ((long)rows * _rowLength);

        public Stream Open()
        {
            return new RowStream(this, columns);
        }

        private sealed class RowStream(KindsSource owner, int columns) : Stream
        {
            private readonly byte[] _row = new byte[owner._rowLength];
            private ulong _state = 0x9E3779B97F4A7C15UL;
            private long _position;
            private int _nextRow;
            private int _rowOffset = owner._rowLength;

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => owner.Length;

            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var written = 0;
                while (written < count && _position < Length)
                {
                    if (_position < owner._header.Length)
                    {
                        var take = Math.Min(count - written, owner._header.Length - (int)_position);
                        Array.Copy(owner._header, (int)_position, buffer, offset + written, take);
                        written += take;
                        _position += take;
                        continue;
                    }

                    if (_rowOffset == _row.Length)
                    {
                        FillRow();
                        _nextRow++;
                        _rowOffset = 0;
                    }

                    var n = Math.Min(count - written, _row.Length - _rowOffset);
                    Array.Copy(_row, _rowOffset, buffer, offset + written, n);
                    _rowOffset += n;
                    written += n;
                    _position += n;
                }

                return written;
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            private static void Digits(Span<byte> destination, ulong value)
            {
                for (var i = destination.Length - 1; i >= 0; i--)
                {
                    destination[i] = (byte)('0' + (int)(value % 10));
                    value /= 10;
                }
            }

            private ulong Next()
            {
                var x = _state;
                x ^= x >> 12;
                x ^= x << 25;
                x ^= x >> 27;
                _state = x;
                return x * 0x2545F4914F6CDD1DUL;
            }

            private void FillRow()
            {
                var at = 0;
                for (var c = 0; c < columns; c++)
                {
                    if (c > 0)
                    {
                        _row[at++] = (byte)',';
                    }

                    var kind = c % 5;
                    var cell = _row.AsSpan(at, Widths[kind]);
                    var x = Next();
                    switch (kind)
                    {
                        case 0:
                            Digits(cell, 100_000_000UL + (x % 900_000_000UL));
                            break;
                        case 1:
                            Digits(cell[..6], 100_000UL + (x % 900_000UL));
                            cell[6] = (byte)'.';
                            Digits(cell[7..], (x >> 32) % 1000UL);
                            break;
                        case 2:
                            cell[0] = (byte)'i';
                            cell[1] = (byte)'d';
                            cell[2] = (byte)'-';
                            Digits(cell[3..], 1_000_000_000UL + (x % 9_000_000_000UL));
                            break;
                        case 3:
                            var day = new DateTime(1950, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(x % 36_500UL);
                            Digits(cell[..4], (ulong)day.Year);
                            cell[4] = (byte)'-';
                            Digits(cell[5..7], (ulong)day.Month);
                            cell[7] = (byte)'-';
                            Digits(cell[8..], (ulong)day.Day);
                            break;
                        default:
                            Digits(cell[..2], 1 + (x % 12UL));
                            cell[2] = (byte)'/';
                            Digits(cell[3..5], 1 + ((x >> 8) % 12UL));
                            cell[5] = (byte)'/';
                            Digits(cell[6..], 1990 + ((x >> 16) % 36UL));
                            break;
                    }

                    at += cell.Length;
                }

                _row[at] = (byte)'\n';
            }
        }
    }
}
