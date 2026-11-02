// SPDX-License-Identifier: Apache-2.0
namespace squalor.squalify.Tests.Schema;

/// <summary>
/// Code for <see cref="CodeVocabulary.IlIssueSitesWithoutARegistryCode"/> to read, not to run: methods that make an <see cref="Issue"/>
/// the way a call site of the library might, some from the registry and some not. The scan is pointed at this type, never at the
/// library, by the test that proves the scan itself (<c>IssueSiteScan_FlagsExactlyTheProbeSitesThatDoNotLoadARegistryField</c>); the
/// production check reads only the library assembly. The order of the methods matters: the scan walks them in declaration order.
/// </summary>
internal static class IssueSiteProbe
{
    /// <summary>A registry site, then a site that passes a variable: only the second is not from the registry.</summary>
    public static Issue[] RegistryThenVariable(string variable)
    {
        var first = new Issue(IssueCodes.RaggedRows, 1);
        var second = new Issue(variable, 1);
        return [first, second];
    }

    /// <summary>A registry site, then a site that passes a static readonly field of another class.</summary>
    public static Issue[] RegistryThenOtherClassField()
    {
        var first = new Issue(IssueCodes.RaggedRows, 1);
        var second = new Issue(ProbeCodes.Other, 1);
        return [first, second];
    }

    /// <summary>A lambda that builds an Issue from its argument.</summary>
    public static Func<string, Issue> LambdaFromVariable()
    {
        return static code => new Issue(code, 1);
    }

    /// <summary>A local function that builds an Issue from its argument.</summary>
    public static Issue LocalFunctionFromVariable(string variable)
    {
        return Make(variable);

        static Issue Make(string code) => new Issue(code, 1);
    }

    /// <summary>A method that loads a registry field and makes no Issue: the load must not carry over to the next method.</summary>
    public static int LoadsRegistryAndMakesNoIssue()
    {
        return IssueCodes.All.Count;
    }

    /// <summary>The method right after the one above: a site with no registry field of its own.</summary>
    public static Issue SiteAloneAfterAMethodThatLoadedTheRegistry(string variable)
    {
        return new Issue(variable, 1);
    }

    /// <summary>Clean: two sites, each with its own registry field.</summary>
    public static Issue[] TwoRegistrySites()
    {
        return [new Issue(IssueCodes.RaggedRows, 1), new Issue(IssueCodes.NoDataRows, 1)];
    }
}

/// <summary>A static readonly string in a class that is not the registry.</summary>
internal static class ProbeCodes
{
    /// <summary>A code that does not come from the registry.</summary>
    public static readonly string Other = "other";
}
