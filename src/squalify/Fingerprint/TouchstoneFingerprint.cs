// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify;

/// <summary>
/// The fingerprint contribution of the Touchstone family. The <c>structure</c> and <c>layout</c> projections get the members of the
/// dialect that say what the file is made of: the version, the port count, the parameter, the data format, the matrix format and
/// whether it has noise data. The frequency unit, the reference impedance, the two-port order, the keywords and the histogram of
/// values per line are left out: they are facts about this file's numbers or its line breaks, not its kind. The field names (which
/// follow the port count, the matrix format, the data format and the two-port order) enter every projection as for any table.
/// </summary>
internal sealed class TouchstoneFingerprint : IFingerprintContributor
{
    /// <inheritdoc />
    public string Family => "touchstone";

    /// <inheritdoc />
    public FingerprintContribution Contribute(ProjectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Dialect is not TouchstoneDialect dialect)
        {
            return FingerprintContribution.None;
        }

        Action<ProjectionJson> write = w =>
        {
            w.Name("dialect").BeginObject();
            w.Name("version").String(dialect.Version);
            w.Name("ports").String(Ordinal.Invariant(dialect.Ports));
            w.Name("parameter").String(dialect.Parameter);
            w.Name("dataFormat").String(dialect.DataFormat);
            w.Name("matrixFormat").String(dialect.MatrixFormat);
            w.Name("noise").Bool(dialect.Noise);
            w.EndObject();
        };
        return new FingerprintContribution(write, write, Typed: null, Names: Array.Empty<string>(), Excluded: 0);
    }
}
