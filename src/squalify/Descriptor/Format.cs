// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>The <c>format</c> section.</summary>
/// <param name="Family"><c>delimited</c>, <c>touchstone</c>, <c>keyValue</c>, <c>stdf</c>, <c>text</c>, <c>binary</c> or <c>empty</c>.</param>
/// <param name="Dialect">The family-specific dialect, or null for families without one.</param>
/// <param name="Score">The winning profiler's score.</param>
public sealed record FormatSection(string Family, Dialect? Dialect, double Score);

/// <summary>Base of the family-specific <c>format.dialect</c> objects.</summary>
public abstract record Dialect
{
    private protected Dialect()
    {
    }
}

/// <summary>Dialect of a delimited table.</summary>
/// <param name="Delimiter">The delimiter text; <c>whitespace</c> for runs of white space; <c>none</c> for a one-column table.</param>
/// <param name="Quote">The quote character, or null when there is none.</param>
/// <param name="HasHeader">True when a header row was found.</param>
/// <param name="UnitsRow">True when a units row was found.</param>
/// <param name="DecimalSeparator">The decimal separator.</param>
/// <param name="Padding">Padding of the data run.</param>
/// <param name="Blocks">Number of repeated blocks.</param>
public sealed record DelimitedDialect(
    string Delimiter,
    string? Quote,
    bool HasHeader,
    bool UnitsRow,
    string DecimalSeparator,
    PaddingInfo Padding,
    long Blocks) : Dialect;

/// <summary>Padding of a delimited data run.</summary>
/// <param name="Trim">True when cells were trimmed.</param>
/// <param name="PaddedShare">Share of padded cells.</param>
/// <param name="ConstantLineLength">True when every data line has the same length.</param>
/// <param name="LineLength">The constant line length, or null when it is not constant.</param>
public sealed record PaddingInfo(bool Trim, double PaddedShare, bool ConstantLineLength, long? LineLength);

/// <summary>Dialect of a Touchstone file.</summary>
/// <param name="Version">The Touchstone version.</param>
/// <param name="Ports">The port count.</param>
/// <param name="FrequencyUnit">The frequency unit.</param>
/// <param name="Parameter">The network parameter kind.</param>
/// <param name="DataFormat">The data format.</param>
/// <param name="ReferenceOhms">The reference impedance.</param>
/// <param name="TwoPortOrder">The two-port data order, or null when not applicable.</param>
/// <param name="MatrixFormat">The matrix format.</param>
/// <param name="Keywords">Keywords seen, in file order.</param>
/// <param name="ValuesPerLine">Histogram of values per line, keyed by the count.</param>
/// <param name="Noise">True when noise data is present.</param>
public sealed record TouchstoneDialect(
    string Version,
    long Ports,
    string FrequencyUnit,
    string Parameter,
    string DataFormat,
    double ReferenceOhms,
    string? TwoPortOrder,
    string MatrixFormat,
    IReadOnlyList<string> Keywords,
    CountMap ValuesPerLine,
    bool Noise) : Dialect
{
    /// <inheritdoc />
    public bool Equals(TouchstoneDialect? other)
    {
        return other is not null
            && EqualityComparer<string>.Default.Equals(Version, other.Version)
            && EqualityComparer<long>.Default.Equals(Ports, other.Ports)
            && EqualityComparer<string>.Default.Equals(FrequencyUnit, other.FrequencyUnit)
            && EqualityComparer<string>.Default.Equals(Parameter, other.Parameter)
            && EqualityComparer<string>.Default.Equals(DataFormat, other.DataFormat)
            && EqualityComparer<double>.Default.Equals(ReferenceOhms, other.ReferenceOhms)
            && EqualityComparer<string?>.Default.Equals(TwoPortOrder, other.TwoPortOrder)
            && EqualityComparer<string>.Default.Equals(MatrixFormat, other.MatrixFormat)
            && ValueEquality.ListEquals(Keywords, other.Keywords)
            && EqualityComparer<CountMap>.Default.Equals(ValuesPerLine, other.ValuesPerLine)
            && EqualityComparer<bool>.Default.Equals(Noise, other.Noise);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Version);
        hash.Add(Ports);
        hash.Add(FrequencyUnit);
        hash.Add(Parameter);
        hash.Add(DataFormat);
        hash.Add(ReferenceOhms);
        hash.Add(TwoPortOrder);
        hash.Add(MatrixFormat);
        ValueEquality.AddList(ref hash, Keywords);
        hash.Add(ValuesPerLine);
        hash.Add(Noise);
        return hash.ToHashCode();
    }
}

/// <summary>Dialect of a key=value document.</summary>
/// <param name="Separator"><c>=</c> or <c>:</c>.</param>
/// <param name="HasSections">True when <c>[section]</c> headers are present.</param>
public sealed record KeyValueDialect(string Separator, bool HasSections) : Dialect;

/// <summary>Dialect of an STDF file.</summary>
/// <param name="CpuType">The CPU_TYPE byte of the FAR record.</param>
/// <param name="Endianness"><c>little</c>, <c>big</c> or <c>vax</c>.</param>
/// <param name="Version">The STDF version.</param>
public sealed record StdfDialect(long CpuType, string Endianness, string Version) : Dialect;

/// <summary>A minimum and a maximum count.</summary>
/// <param name="Min">The minimum.</param>
/// <param name="Max">The maximum.</param>
public sealed record MinMax(long Min, long Max);
