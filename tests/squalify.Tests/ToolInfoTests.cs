// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;
using squalor.squalify;

namespace squalor.squalify.Tests;

public class ToolInfoTests
{
    [Fact]
    public void ToolInfo_Version_HasNoSourceRevision()
    {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), ToolInfo.Version);
    }

    [Fact]
    public void ToolInfo_Version_MatchesAssemblyVersion()
    {
        Assert.Equal(typeof(ToolInfo).Assembly.GetName().Version!.ToString(3), ToolInfo.Version);
    }
}
