// SPDX-License-Identifier: Apache-2.0
using System.Text;
using squalor.squalify;

namespace squalor.squalify.Tests;

/// <summary>Parquet is not in v1 (sq-31): a Parquet file is profiled as any unsupported binary, and its members are gone from the schema.</summary>
public sealed class ParquetRemovalTests
{
    [Fact]
    public void Parquet_File_ProfiledAsBinary()
    {
        // "PAR1", a body, "PAR1": written here, no fixture.
        var bytes = TestBytes.Concat(TestBytes.Ascii("PAR1"), TestBytes.Hex("00 01 02 03 04 05 06 07"), TestBytes.Ascii("PAR1"));
        var descriptor = Profiler.Profile(new BytesSource("a.parquet", bytes), ProfileOptions.Create(false, Array.Empty<string>()));

        Assert.Equal("binary", descriptor.Format.Family);
        Assert.NotNull(descriptor.Binary);
        Assert.Equal(new MagicHint("parquet", 0, "50415231"), descriptor.Hints.Magic);
    }

    [Fact]
    public void Descriptor_HasNoParquetMembers()
    {
        var goldens = Directory.GetFiles(Golden.SourceFixturesRoot(), "*.squalify.json", SearchOption.AllDirectories);
        Assert.NotEmpty(goldens);
        foreach (var golden in goldens)
        {
            AssertNoParquetMembers(Path.GetFileName(golden), File.ReadAllText(golden, Encoding.UTF8));
        }

        // The goldens are files; the writer is what makes them, so check its output too.
        AssertNoParquetMembers("FullDelimited", Encoding.UTF8.GetString(DescriptorWriter.ToBytes(TestModels.FullDelimited())));
        AssertNoParquetMembers("FullBinaryStdf", Encoding.UTF8.GetString(DescriptorWriter.ToBytes(TestModels.FullBinaryStdf())));
        var csv = Profiler.Profile(new BytesSource("t.csv", Encoding.UTF8.GetBytes("a,b\n1,2\n3,4\n")), ProfileOptions.Create(false, Array.Empty<string>()));
        AssertNoParquetMembers("profiled csv", Encoding.UTF8.GetString(DescriptorWriter.ToBytes(csv)));
    }

    private static void AssertNoParquetMembers(string what, string json)
    {
        foreach (var member in new[] { "sourceType", "parquetColumns", "kvKeys" })
        {
            Assert.False(json.Contains($"\"{member}\"", StringComparison.Ordinal), $"{what} contains \"{member}\"");
        }
    }
}
