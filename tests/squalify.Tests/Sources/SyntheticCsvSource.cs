// SPDX-License-Identifier: Apache-2.0
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>
/// An <see cref="IProfileSource"/> that generates N rows of M columns of mixed types on the fly, from a seeded
/// xorshift generator, so a stream of any size needs no memory and no disk. Every column has a fixed width, so a row has
/// one length and <see cref="Length"/> is exact without generating anything. The columns cycle through eight kinds:
/// a sequential id, a six-digit integer, a float, a date, a low-cardinality code, an eight-letter string, a time and a
/// three-digit integer. <see cref="PaddedWide"/> makes a wide table of right-padded fixed-width cells instead. Nothing
/// here depends on the culture or the machine.
/// </summary>
internal sealed class SyntheticCsvSource : IProfileSource
{
    private const int KindCount = 8;
    private const int DateCount = 7_300;

    // The fixed width of each kind, in bytes.
    private static readonly int[] KindWidths = [9, 6, 8, 10, 3, 8, 8, 3];

    private const int MinCellWidth = 11; // the widest data value is 10; the annotation label "Upper_limit" is 11
    private const int AnnotationRows = 4;

    private static readonly byte[][] Dates = BuildDates();

    // The preamble of the padded mode: the keys, padded to 14 characters before the colon, and their values.
    private static readonly (string Key, string Value)[] PreambleKeys =
    [
        ("Lot", "A12"),
        ("Wafer", "7"),
        ("Tester", "T-05"),
        ("Program", "ft_s100"),
        ("Date", "2026-01-05"),
    ];

    private readonly long _rows;
    private readonly int _columns;
    private readonly ulong _seed;
    private readonly byte[] _header;
    private readonly int _rowLength;
    private readonly int _cellWidth;

    /// <summary>Creates a source.</summary>
    /// <param name="rows">The number of data rows.</param>
    /// <param name="columns">The number of columns, at least 1.</param>
    /// <param name="seed">The seed of the generator, not 0.</param>
    public SyntheticCsvSource(long rows, int columns, ulong seed = 1)
        : this(rows, columns, seed, 0)
    {
    }

    private SyntheticCsvSource(long rows, int columns, ulong seed, int cellWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfZero(seed);
        _rows = rows;
        _columns = columns;
        _seed = seed;
        _cellWidth = cellWidth;
        if (cellWidth > 0)
        {
            _header = Encoding.ASCII.GetBytes(BuildPaddedHead(columns, cellWidth));
            _rowLength = PaddedRowLength(columns, cellWidth);
            PreambleLineCount = PreambleKeys.Length;
            AnnotationLineCount = AnnotationRows;
            return;
        }

        var header = new StringBuilder();
        var width = columns;
        for (var c = 0; c < columns; c++)
        {
            header.Append(c == 0 ? string.Empty : ",").Append('c').Append(c.ToString(System.Globalization.CultureInfo.InvariantCulture));
            width += KindWidths[c % KindCount];
        }

        header.Append('\n');
        _header = Encoding.ASCII.GetBytes(header.ToString());
        _rowLength = width;
    }

    /// <summary>The rows that make a stream of about the given size, for the given number of columns.</summary>
    /// <param name="bytes">The wanted size.</param>
    /// <param name="columns">The number of columns.</param>
    /// <returns>The number of rows.</returns>
    public static long RowsFor(long bytes, int columns)
    {
        var width = columns;
        for (var c = 0; c < columns; c++)
        {
            width += KindWidths[c % KindCount];
        }

        return Math.Max(1, bytes / width);
    }

    /// <summary>
    /// A wide, right-padded fixed-width table, like a production-test export: a key preamble (<see cref="PreambleLineCount"/>
    /// lines, <c>key :,value</c>), annotation rows (<see cref="AnnotationLineCount"/> lines: program line, upper and lower
    /// limit and unit), a header and the data rows. Every cell of every table line (annotation rows, header and data rows)
    /// is <paramref name="cellWidth"/> bytes, its text right-padded with spaces, so every table line has one length. The
    /// data cells are the same eight kinds as the plain source, in the same seeded sequence.
    /// </summary>
    /// <param name="rows">The number of data rows.</param>
    /// <param name="columns">The number of columns, at least 1.</param>
    /// <param name="cellWidth">The width of every cell in bytes, at least 11 (the label "Upper_limit").</param>
    /// <param name="seed">The seed of the generator, not 0.</param>
    /// <returns>The source.</returns>
    public static SyntheticCsvSource PaddedWide(long rows, int columns, int cellWidth, ulong seed = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cellWidth, MinCellWidth);
        return new SyntheticCsvSource(rows, columns, seed, cellWidth);
    }

    /// <summary>The rows of a padded wide stream of about the given size.</summary>
    /// <param name="bytes">The wanted size.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="cellWidth">The width of every cell.</param>
    /// <returns>The number of rows.</returns>
    public static long PaddedRowsFor(long bytes, int columns, int cellWidth)
    {
        return Math.Max(1, bytes / PaddedRowLength(columns, cellWidth));
    }

    /// <summary>The number of data rows.</summary>
    public long Rows => _rows;

    /// <summary>The number of columns.</summary>
    public int Columns => _columns;

    /// <summary>The number of preamble lines before the table; 0 for the plain source.</summary>
    public int PreambleLineCount { get; }

    /// <summary>The number of annotation rows between the preamble and the header; 0 for the plain source.</summary>
    public int AnnotationLineCount { get; }

    /// <inheritdoc />
    public string Name => "synthetic.csv";

    /// <inheritdoc />
    public long Length => _header.Length + (_rows * _rowLength);

    /// <inheritdoc />
    public Stream Open()
    {
        return new RowStream(this);
    }

    private static int PaddedRowLength(int columns, int cellWidth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        return (columns * cellWidth) + columns;
    }

    // The preamble, the four annotation rows and the header of the padded mode, as one text.
    private static string BuildPaddedHead(int columns, int cellWidth)
    {
        var text = new StringBuilder();
        foreach (var (key, value) in PreambleKeys)
        {
            text.Append(key.PadRight(14)).Append(':').Append(',').Append(value).Append('\n');
        }

        // Which kinds carry limits and a unit: the six-digit integer (mA), the float (dBm) and the three-digit integer (dB).
        string[] unit = ["", "mA", "dBm", "", "", "", "", "dB"];
        string[] upper = ["", "999999", "9999.99", "", "", "", "", "999"];
        string[] lower = ["", "100000", "1000.00", "", "", "", "", "100"];
        AppendPaddedLine(text, columns, cellWidth, "Prog_line", static c => (c % 7 == 3) ? string.Empty : (100 + c).ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendPaddedLine(text, columns, cellWidth, "Upper_limit", c => upper[c % KindCount]);
        AppendPaddedLine(text, columns, cellWidth, "Lower_limit", c => lower[c % KindCount]);
        AppendPaddedLine(text, columns, cellWidth, "Unit", c => unit[c % KindCount]);
        AppendPaddedLine(text, columns, cellWidth, "Site", static c => "c" + c.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return text.ToString();
    }

    // One table line: the label (or the cell text for column 0), then the cells of columns 1 and up, every one right-padded.
    private static void AppendPaddedLine(StringBuilder text, int columns, int cellWidth, string label, Func<int, string> cell)
    {
        for (var c = 0; c < columns; c++)
        {
            if (c > 0)
            {
                text.Append(',');
            }

            var value = c == 0 ? label : cell(c);
            text.Append(value.PadRight(cellWidth));
        }

        text.Append('\n');
    }

    private static byte[][] BuildDates()
    {
        var dates = new byte[DateCount][];
        var start = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < dates.Length; i++)
        {
            var day = start.AddDays(i);
            dates[i] = Encoding.ASCII.GetBytes(
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{day.Year:D4}-{day.Month:D2}-{day.Day:D2}"));
        }

        return dates;
    }

    private static void Digits(Span<byte> destination, ulong value)
    {
        for (var i = destination.Length - 1; i >= 0; i--)
        {
            destination[i] = (byte)('0' + (int)(value % 10));
            value /= 10;
        }
    }

    private sealed class RowStream(SyntheticCsvSource owner) : Stream
    {
        private readonly byte[] _row = new byte[owner._rowLength];
        private ulong _state = owner._seed;
        private long _position;
        private long _nextRow;
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
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            var written = 0;
            var headerLength = owner._header.Length;
            while (written < buffer.Length && _position < owner.Length)
            {
                if (_position < headerLength)
                {
                    var take = Math.Min(buffer.Length - written, headerLength - (int)_position);
                    owner._header.AsSpan((int)_position, take).CopyTo(buffer[written..]);
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

                var count = Math.Min(buffer.Length - written, _row.Length - _rowOffset);
                _row.AsSpan(_rowOffset, count).CopyTo(buffer[written..]);
                _rowOffset += count;
                written += count;
                _position += count;
            }

            return written;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

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
            for (var c = 0; c < owner._columns; c++)
            {
                if (c > 0)
                {
                    _row[at++] = (byte)',';
                }

                var kind = c % KindCount;
                var cellLength = owner._cellWidth > 0 ? owner._cellWidth : KindWidths[kind];
                var field = _row.AsSpan(at, cellLength);
                var cell = field[..KindWidths[kind]];
                field[cell.Length..].Fill((byte)' ');
                var x = Next();
                switch (kind)
                {
                    case 0:
                        Digits(cell, 100_000_000UL + (ulong)_nextRow);
                        break;
                    case 1:
                        Digits(cell, 100_000UL + (x % 900_000UL));
                        break;
                    case 2:
                        Digits(cell[..7], 1_000_000UL + (x % 9_000_000UL));
                        cell[7] = cell[6];
                        cell[6] = cell[5];
                        cell[5] = cell[4];
                        cell[4] = cell[3];
                        cell[3] = (byte)'.';
                        break;
                    case 3:
                        Dates[(int)(x % DateCount)].CopyTo(cell);
                        break;
                    case 4:
                        cell[0] = (byte)'C';
                        Digits(cell[1..], 10UL + (x % 90UL));
                        break;
                    case 5:
                        for (var j = 0; j < cell.Length; j++)
                        {
                            cell[j] = (byte)('a' + (int)((x >> (5 * j)) % 26UL));
                        }

                        break;
                    case 6:
                        var second = x % 86_400UL;
                        Digits(cell[0..2], second / 3600UL);
                        cell[2] = (byte)':';
                        Digits(cell[3..5], (second / 60UL) % 60UL);
                        cell[5] = (byte)':';
                        Digits(cell[6..8], second % 60UL);
                        break;
                    default:
                        Digits(cell, 100UL + (x % 900UL));
                        break;
                }

                at += field.Length;
            }

            _row[at] = (byte)'\n';
        }
    }
}
