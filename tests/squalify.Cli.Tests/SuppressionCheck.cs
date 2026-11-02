// SPDX-License-Identifier: Apache-2.0
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace squalor.squalify.Cli.Tests;

/// <summary>
/// The mechanical check of package-validation suppressions (sq-74). It holds every entry of
/// <c>src/squalify/CompatibilitySuppressions.xml</c> to the rule of docs/release.md, "Package validation": the target is
/// (a) a constructor or <c>Deconstruct</c> of a model record, or (b) a provisional model type or member that the user guide
/// lists, and its type is named in a "Breaking changes in X (library)" user-guide entry of a version newer than the baseline.
/// No file means no suppression, and passes. The XML is read with the BCL; the user guide is Markdown text.
/// </summary>
internal static class SuppressionCheck
{
    private const string LibraryNamespace = "squalor.squalify.";

    private static readonly Regex Entry = new(@"^- \*\*Breaking changes in (\d+\.\d+\.\d+)\S* \(library", RegexOptions.CultureInvariant);
    private static readonly Regex Provisional = new(@"The model types of planned members \((?<list>[^)]*)\) are provisional", RegexOptions.CultureInvariant);
    private static readonly Regex CodeSpan = new(@"`([^`]+)`", RegexOptions.CultureInvariant);
    private static readonly Regex Identifier = new(@"[A-Za-z_]\w*", RegexOptions.CultureInvariant);

    /// <summary>Where package validation reads the suppressions of the library: beside its project.</summary>
    public static string PathIn(string root) => Path.Combine(root, "src", "squalify", "CompatibilitySuppressions.xml");

    /// <summary>Checks the suppression file of a repository, if it has one, against the repository's user guide and baseline.</summary>
    public static List<string> Problems(string root)
    {
        var path = PathIn(root);
        if (!File.Exists(path))
        {
            return [];
        }

        var project = XDocument.Load(Path.Combine(root, "src", "squalify", "squalify.csproj"));
        var text = project.Descendants("PackageValidationBaselineVersion").Select(element => element.Value.Trim()).SingleOrDefault();
        if (!Version.TryParse(text, out var baseline))
        {
            return [$"PackageValidationBaselineVersion '{text}' in squalify.csproj is not a stable release (X.Y.Z)."];
        }

        var guide = File.ReadAllText(Path.Combine(root, "docs", "user-guide.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Problems(File.ReadAllText(path), guide, baseline);
    }

    /// <summary>Checks the text of a suppression file against the text of the user guide and the baseline version.</summary>
    public static List<string> Problems(string suppressionsXml, string userGuide, Version baseline)
    {
        XElement root;
        try
        {
            root = XDocument.Parse(suppressionsXml).Root ?? throw new InvalidDataException("no root element");
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or InvalidDataException)
        {
            return [$"CompatibilitySuppressions.xml is not XML: {exception.Message}"];
        }

        if (root.Name.LocalName != "Suppressions")
        {
            return [$"CompatibilitySuppressions.xml has root <{root.Name.LocalName}>, not <Suppressions>."];
        }

        var provisional = ProvisionalNames(userGuide);
        var entries = EntryTexts(userGuide, baseline);
        var problems = new List<string>();
        foreach (var suppression in root.Elements("Suppression"))
        {
            var target = suppression.Element("Target")?.Value.Trim();
            if (string.IsNullOrEmpty(target))
            {
                problems.Add("a Suppression has no Target.");
                continue;
            }

            var problem = Check(target, provisional, entries, baseline);
            if (problem is not null)
            {
                problems.Add($"{target}: {problem}");
            }
        }

        return problems;
    }

    /// <summary>The provisional model types and members that the "Model records" bullet of the user guide lists, such as TouchstoneDialect and Descriptor.Sections.</summary>
    public static IReadOnlyList<string> ProvisionalNames(string userGuide)
    {
        var match = Provisional.Match(userGuide.Replace('\n', ' '));
        return match.Success
            ? CodeSpan.Matches(match.Groups["list"].Value).Select(span => span.Groups[1].Value).ToArray()
            : [];
    }

    /// <summary>The text of each "Breaking changes in X (library)" entry whose version is newer than the baseline.</summary>
    private static List<string> EntryTexts(string userGuide, Version baseline) =>
        userGuide.Split('\n')
            .Where(line => Entry.Match(line) is { Success: true } match && Version.Parse(match.Groups[1].Value) > baseline)
            .ToList();

    private static string? Check(string target, IReadOnlyList<string> provisional, List<string> entries, Version baseline)
    {
        var colon = target.IndexOf(':', StringComparison.Ordinal);
        if (colon != 1 || !target[2..].StartsWith(LibraryNamespace, StringComparison.Ordinal))
        {
            return $"the target is not a member or type of {LibraryNamespace.TrimEnd('.')}.";
        }

        var kind = target[0];
        var name = target[(colon + 1 + LibraryNamespace.Length)..];
        var open = name.IndexOf('(', StringComparison.Ordinal);
        if (open >= 0)
        {
            name = name[..open];
        }

        string type;
        string? member = null;
        if (kind == 'T')
        {
            type = name;
        }
        else
        {
            var dot = name.LastIndexOf('.');
            if (dot < 0)
            {
                return "the target has no type.";
            }

            type = name[..dot];
            member = name[(dot + 1)..];
        }

        var allowed = (kind == 'M' && member is ("#ctor" or "Deconstruct") && IsModelRecord(type))
            || provisional.Contains(type)
            || (member is not null && provisional.Contains(type + "." + PropertyName(member)));
        if (!allowed)
        {
            return "not one of the two kinds that release.md allows to be suppressed in 1.x (a constructor or Deconstruct of a model record, or a provisional model type or member of the user guide).";
        }

        var simple = Regex.Replace(type[(type.LastIndexOf('.') + 1)..], "`.*$", string.Empty);
        return entries.Any(entry => NamesType(entry, simple))
            ? null
            : $"no 'Breaking changes in X (library)' entry of the user guide for a version newer than {baseline} names `{simple}` in a code span.";
    }

    /// <summary>The property or event that an accessor method belongs to, or the member's own name.</summary>
    private static string PropertyName(string member) =>
        Regex.Replace(member, "^(get_|set_|add_|remove_)", string.Empty);

    /// <summary>True when the type is a record of the library (a class or struct with the compiler's clone method).</summary>
    private static bool IsModelRecord(string type)
    {
        var resolved = typeof(ToolInfo).Assembly.GetType(LibraryNamespace + type.Replace('.', '+'), throwOnError: false);
        return resolved?.GetMethod("<Clone>$") is not null;
    }

    /// <summary>True when a code span of the entry holds the type's name as an identifier (`Issue`, `new Issue(...)`, `Descriptor.Sections`).</summary>
    private static bool NamesType(string entry, string simpleName) =>
        CodeSpan.Matches(entry).Any(span => Identifier.Matches(span.Groups[1].Value).Any(token => token.Value == simpleName));
}
