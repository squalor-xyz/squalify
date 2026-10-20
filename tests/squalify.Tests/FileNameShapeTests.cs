// SPDX-License-Identifier: Apache-2.0
using squalor.squalify;

namespace squalor.squalify.Tests;

public class FileNameShapeTests
{
    [Theory]
    [InlineData("alpha_123.csv", "a{5}_9{3}.a{3}")]
    [InlineData("Lot42.CSV", "Aa{2}9{2}.A{3}")]
    [InlineData("a b.txt", "a a.a{3}")]
    [InlineData("café.txt", "a{3}?.a{3}")]
    [InlineData("x", "a")]
    [InlineData("", "")]
    [InlineData("a--b", "a-{2}a")]
    public void FileNameShape_MapsClassesAndRuns(string name, string expected)
    {
        Assert.Equal(expected, FileNameShape.Of(name));
    }

    [Fact]
    public void FileNameShape_LongerThan64Tokens_IsTruncatedWithPlus()
    {
        var name = string.Concat(Enumerable.Repeat("a1", 40));
        var shape = FileNameShape.Of(name);
        Assert.EndsWith("+", shape);
        Assert.Equal(string.Concat(Enumerable.Repeat("a9", 32)) + "+", shape);
    }

    [Fact]
    public void FileNameShape_Exactly64Tokens_IsNotTruncated()
    {
        var name = string.Concat(Enumerable.Repeat("a1", 32));
        Assert.Equal(string.Concat(Enumerable.Repeat("a9", 32)), FileNameShape.Of(name));
    }
}
