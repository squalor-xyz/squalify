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

/// <summary>
/// Code for <see cref="CodeVocabulary.IlIssueCodes"/> to read, not to run. Each method makes an <see cref="Issue"/> whose code is its first
/// argument, and puts a string literal where the scan must not take it for a code: in a later argument, as a value that a call consumes, and
/// as the argument of a call that makes the code. The codes are the first arguments, in the shapes the compiler gives them: a direct literal,
/// the arms of a branch or of a switch expression, a literal that goes through a local or a parameter. Each shape has its own method.
/// </summary>
internal static class CodeArgumentProbe
{
    /// <summary>The code is the first argument; a later argument is a literal in an array.</summary>
    public static Issue CodeThenLaterLiteral(int count)
    {
        return new Issue("probeCode", count, Names: new[] { "later" });
    }

    /// <summary>A literal that is only passed to another method, then a registry code.</summary>
    public static Issue UnrelatedLiteralBeforeCode(int count)
    {
        Consume("unrelated");
        return new Issue(IssueCodes.RaggedRows, count);
    }

    /// <summary>A literal that a call turns into the code: the literal is an argument of that call, not the code.</summary>
    public static Issue DescribedCode(int count)
    {
        return new Issue(Describe("probeDescribed"), count);
    }

    /// <summary>A code chosen by a branch: each arm is the first argument, so each arm is a code.</summary>
    public static Issue TernaryCode(bool flag, int count)
    {
        return new Issue(flag ? "probeTernary" : "probeTernaryOther", count);
    }

    /// <summary>A literal that is the first argument of an outer call, with an Issue built as its later argument: not a code.</summary>
    public static void OuterArgumentLiteral(int count)
    {
        Keep("probeOuter", new Issue(IssueCodes.RaggedRows, count));
    }

    /// <summary>A code held in a local: the literal is stored, then loaded as the first argument.</summary>
    public static Issue ViaLocal(int count)
    {
        var code = "probeLocal";
        return new Issue(code, count);
    }

    /// <summary>A code held in a local that is loaded for another call first, then used as the code twice.</summary>
    public static Issue[] ViaLocalUsedTwice(int count)
    {
        var code = "probeLocalTwice";
        Consume(code);
        return [new Issue(code, count), new Issue(code, count)];
    }

    /// <summary>A code chosen by a switch expression, which the compiler lowers through a temporary local.</summary>
    public static Issue SwitchCode(int which, int count)
    {
        return new Issue(which switch { 0 => "probeSwitchA", 1 => "probeSwitchB", _ => "probeSwitchC" }, count);
    }

    /// <summary>A code stored into a parameter, then loaded as the first argument.</summary>
    public static Issue ViaArgument(string code, int count)
    {
        code = "probeArgument";
        return new Issue(code, count);
    }

    private static void Consume(string text)
    {
        _ = text;
    }

    private static void Keep(string label, Issue issue)
    {
        _ = label;
        _ = issue;
    }

    private static string Describe(string text) => text.ToUpperInvariant();
}
