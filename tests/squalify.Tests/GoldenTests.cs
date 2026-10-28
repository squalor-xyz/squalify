// SPDX-License-Identifier: Apache-2.0
using System.Text;
using Xunit.Sdk;
using squalor.squalify;

namespace squalor.squalify.Tests;

public sealed class GoldenTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "squalify-golden-" + Guid.NewGuid().ToString("N"));

    public GoldenTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Golden_Mismatch_FailsWithDiff()
    {
        var descriptor = TestModels.FullDelimited();
        var good = DescriptorWriter.ToBytes(descriptor);
        var text = Encoding.UTF8.GetString(good);
        var changed = text.Replace("        \"name\": \"Frequency(MHz)\",", "        \"name\": \"Frequency(GHz)\",", StringComparison.Ordinal);
        Assert.NotEqual(text, changed);
        File.WriteAllBytes(Path.Combine(_root, "sample.csv.squalify.json"), Encoding.UTF8.GetBytes(changed));

        var error = Assert.ThrowsAny<XunitException>(() => Golden.Verify("sample.csv", descriptor, _root, update: false));
        Assert.Contains("sample.csv.squalify.json", error.Message);
        Assert.Contains("--- expected", error.Message);
        Assert.Contains("+++ actual", error.Message);
        Assert.Contains("-        \"name\": \"Frequency(GHz)\",", error.Message);
        Assert.Contains("+        \"name\": \"Frequency(MHz)\",", error.Message);
        Assert.Contains("@@", error.Message);
    }

    [Fact]
    public void Golden_Match_Passes()
    {
        var descriptor = TestModels.FullBinaryStdf();
        File.WriteAllBytes(Path.Combine(_root, "run.stdf.squalify.json"), DescriptorWriter.ToBytes(descriptor));
        Golden.Verify("run.stdf", descriptor, _root, update: false);
    }

    [Fact]
    public void Golden_CrLfGolden_IsAMismatch()
    {
        var descriptor = TestModels.Minimal();
        var crlf = Encoding.UTF8.GetString(DescriptorWriter.ToBytes(descriptor)).Replace("\n", "\r\n", StringComparison.Ordinal);
        File.WriteAllBytes(Path.Combine(_root, "a.squalify.json"), Encoding.UTF8.GetBytes(crlf));
        Assert.ThrowsAny<XunitException>(() => Golden.Verify("a", descriptor, _root, update: false));
    }

    [Fact]
    public void Golden_MissingGolden_FailsAndNamesTheUpdateVariable()
    {
        var error = Assert.ThrowsAny<XunitException>(() => Golden.Verify("nothing-here", TestModels.Minimal(), _root, update: false));
        Assert.Contains(Golden.UpdateVariable, error.Message);
        Assert.False(File.Exists(Path.Combine(_root, "nothing-here.squalify.json")));
    }

    [Fact]
    public void Golden_UpdateMode_Writes()
    {
        var descriptor = TestModels.FullDelimited();
        Golden.Verify("sub/x.csv", descriptor, _root, update: true);

        var written = File.ReadAllBytes(Path.Combine(_root, "sub", "x.csv.squalify.json"));
        Assert.Equal(DescriptorWriter.ToBytes(descriptor), written);

        // A second, non-update verify of the freshly written golden passes.
        Golden.Verify("sub/x.csv", descriptor, _root, update: false);
    }

    [Fact]
    public void Golden_UpdateMode_OverwritesAnExistingGolden()
    {
        var descriptor = TestModels.Minimal();
        File.WriteAllText(Path.Combine(_root, "y.squalify.json"), "stale");
        Golden.Verify("y", descriptor, _root, update: true);
        Assert.Equal(DescriptorWriter.ToBytes(descriptor), File.ReadAllBytes(Path.Combine(_root, "y.squalify.json")));
    }

    [Fact]
    public void Golden_SourceFixturesRoot_IsTheTestProjectFixturesFolder()
    {
        var root = Golden.SourceFixturesRoot();
        Assert.True(File.Exists(Path.Combine(root, "full-delimited.squalify.json")), root);
        Assert.Equal("squalify.Tests", Path.GetFileName(Path.GetDirectoryName(root)));
    }

    [Fact]
    public void Golden_UpdateVariable_IsTheDocumentedName()
    {
        Assert.Equal("SQUALIFY_UPDATE_GOLDEN", Golden.UpdateVariable);
    }

    [Fact]
    public void UnifiedDiff_ShowsChangedAddedAndRemovedLines_WithContext()
    {
        var diff = Golden.UnifiedDiff("a\nb\nc\nd\ne\nf\ng\nh\n", "a\nb\nc\nX\ne\nf\ng\nh\ni\n");
        Assert.Contains("-d", diff.Split('\n'));
        Assert.Contains("+X", diff.Split('\n'));
        Assert.Contains("+i", diff.Split('\n'));
        Assert.Contains(" c", diff.Split('\n'));
        Assert.DoesNotContain("-a", diff.Split('\n'));
    }
}
